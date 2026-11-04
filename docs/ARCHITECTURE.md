# Config Manager — Architecture & Contracts

MVP of a feature-flag / configuration manager compatible with **OpenFeature**.
This document is the **source of truth** for contracts between components. Do not deviate
from table names, routes, claim names or JSON shapes described here.

```
                 ┌──────────────────────────┐
  browser ──────▶│  web (React + shadcn/ui) │──── OIDC (PKCE) ───────┐
                 │  nginx :3000  /api proxy │                        │
                 └────────────┬─────────────┘                        ▼
                              │ /api/*  (Bearer user JWT)     ┌──────────────┐
                              ▼                               │  Keycloak    │
                 ┌──────────────────────────┐                 │  (RHBK-comp.)│
                 │  Management API  :5001   │◀── JWKS ────────│  :8080       │
                 │  flags, CRs, versions,   │                 └──────▲───────┘
                 │  RBAC, audit, EF Core    │                        │ client_credentials
                 └────────────┬─────────────┘                        │
                   writes     │ INSERT published_snapshots           │
                   + NOTIFY   ▼ + pg_notify('config_published')      │
                 ┌──────────────────────────┐                        │
                 │  PostgreSQL :5432        │                        │
                 └────────────┬─────────────┘                        │
                   LISTEN +   │ SELECT latest snapshot               │
                   poll       ▼                                      │
                 ┌──────────────────────────┐   Bearer service JWT   │
                 │ Distribution API :5002   │◀───────────────┐       │
                 │ snapshot+ETag, SSE, OFREP│                │       │
                 └──────────────────────────┘                │       │
                                                  ┌──────────┴───────┴──────┐
                                                  │ ConfigManager.OpenFeature│
                                                  │ provider (local eval)   │
                                                  │  in orders-api :5101    │
                                                  │  in payments-api :5102  │
                                                  └─────────────────────────┘
```

## Concepts

| Concept | Description |
|---|---|
| **Environment** | Global list (`dev`, `hml`, `prod`). Key is a slug (`KeyRules.IsValidKey`). |
| **Namespace** | Isolation unit (team/service domain), e.g. `orders`, `payments`. |
| **Flag** | Metadata in a namespace: `key`, `name`, `description`, `type` (boolean/string/integer/double/object). Type is immutable. |
| **Flag environment config** | `FlagDefinition` (see `ConfigManager.Contracts`) of a flag in one env: enabled, variants, defaultVariant, rules. A flag only appears in an env snapshot once it has a config there. |
| **Change** | A set of operations against one `(namespace, environment)`: `upsert` a flag config or `delete` it. |
| **Policy** | Per `(namespace, environment)`: `requireChangeRequest` (bool) + `requiredApprovals` (int ≥ 1). |
| **Change Request (CR)** | A pending change awaiting approvals. Four-eyes: author cannot approve. Auto-applied when approvals ≥ required. |
| **Snapshot / Version** | Immutable full `ConfigSnapshot` of `(namespace, environment)` produced by every applied change. Monotonic `version` per pair, starting at 1. |
| **Rollback** | A change whose operations restore the flag set of version N (upserts for flags in N, deletes for flags not in N). Goes through policy like any change; produces a NEW version. |
| **Promotion** | A change copying flag configs from env A to env B (all flags or a subset). Goes through policy of env B. |
| **Service access grant** | `(clientId, namespace, environment)` — allows an OIDC client (service account) to read that snapshot from the Distribution API. |

## Shared library `ConfigManager.Contracts` (already implemented)

* `ConfigSnapshot`, `FlagDefinition`, `TargetingRule`, `Condition`, `WeightedVariant`, `FlagType`, `ConditionOperator` (`Models.cs`)
* `SnapshotJson.Options` — **the** JSON options (web defaults/camelCase, nulls omitted). Use it for snapshot/flag JSON everywhere.
* `FlagEvaluator.Evaluate(snapshot|flag, flagKey, EvaluationInput)` → `EvaluationResult { Variant, Value (JsonElement?), Reason, ErrorCode, ErrorMessage }`
  * disabled → `Reason=Disabled`, `Value=null` (caller returns its own default)
  * rules in order, ALL conditions must match; `variant` → `TargetingMatch`; `rollout` → `Split` (skipped without targetingKey)
  * no match → defaultVariant with `Static` (no rules) or `Default`
* `FlagValidator.Validate(flagKey, flag)` → list of error strings. `KeyRules.IsValidKey(key)`.

Snapshot JSON example:
```json
{
  "namespace": "orders", "environment": "dev", "version": 3, "publishedAt": "2026-11-04T10:00:00Z",
  "flags": {
    "new-checkout": {
      "type": "boolean", "enabled": true,
      "variants": { "on": true, "off": false }, "defaultVariant": "off",
      "rules": [
        { "name": "beta tenants", "conditions": [ { "attribute": "tenant", "operator": "in", "values": ["acme","globex"] } ], "variant": "on" },
        { "name": "10% rollout", "conditions": [], "rollout": [ { "variant": "on", "weight": 10 }, { "variant": "off", "weight": 90 } ] }
      ],
      "metadata": {}
    }
  }
}
```

## Database (PostgreSQL, database `configmanager`)

Owned by the **Management API** (EF Core, migrations applied at startup, snake_case naming via
`EFCore.NamingConventions`). The **Distribution API reads ONLY the two tables below** using plain
Npgsql (no EF, no dependency on Management code). These two tables are a hard contract:

```sql
CREATE TABLE published_snapshots (
  id                 uuid PRIMARY KEY,
  namespace_key      text        NOT NULL,
  environment_key    text        NOT NULL,
  version            bigint      NOT NULL,
  content            jsonb       NOT NULL,      -- ConfigSnapshot serialized with SnapshotJson.Options
  published_at       timestamptz NOT NULL,
  published_by       text        NOT NULL,      -- username
  change_request_id  uuid        NULL,
  message            text        NULL,
  UNIQUE (namespace_key, environment_key, version)
);

CREATE TABLE service_access_grants (
  id               uuid PRIMARY KEY,
  client_id        text        NOT NULL,   -- OIDC client id (token claim `azp`, fallback `client_id`)
  namespace_key    text        NOT NULL,
  environment_key  text        NOT NULL,   -- or '*' for all environments
  created_at       timestamptz NOT NULL,
  created_by       text        NOT NULL,
  UNIQUE (client_id, namespace_key, environment_key)
);
```

After inserting a snapshot (same transaction), Management executes
`SELECT pg_notify('config_published', '<namespace>/<environment>/<version>')`.
After changing service access grants it executes `SELECT pg_notify('access_changed', '')`.

Other Management tables (free design, suggested): `environments`, `namespaces`,
`namespace_policies`, `namespace_role_bindings`, `flags`, `flag_environment_configs`
(content jsonb = FlagDefinition), `change_requests` (operations jsonb, status, base_version),
`change_request_reviews`, `audit_entries`.

## Identity (Keycloak realm `config-manager`, RHBK compatible)

Issuer (as seen by everyone): `http://localhost:8080/realms/config-manager`.
Inside docker, containers reach Keycloak at `http://keycloak:8080` (Keycloak runs with
`KC_HOSTNAME=http://localhost:8080` + `KC_HOSTNAME_BACKCHANNEL_DYNAMIC=true`), so backend services must
configure `Authority`/`ValidIssuer` = the localhost issuer and `MetadataAddress` =
`http://keycloak:8080/realms/config-manager/.well-known/openid-configuration`, `RequireHttpsMetadata=false`.
Service clients request tokens at `http://keycloak:8080/realms/config-manager/protocol/openid-connect/token`.

Clients:
| clientId | type | purpose |
|---|---|---|
| `config-manager-web` | public, PKCE S256, std flow | SPA. Redirects `http://localhost:3000/*`, `http://localhost:5173/*`. Audience mapper adds `config-manager-api`. |
| `config-manager-api` | bearer-only (no flows) | audience of Management API |
| `config-distribution` | bearer-only | audience of Distribution API |
| `orders-service` | confidential, service accounts only, secret `orders-service-secret` | sample service; audience mapper adds `config-distribution` |
| `payments-service` | confidential, service accounts only, secret `payments-service-secret` | sample service; audience mapper adds `config-distribution` |

Token claims used:
* `sub`, `preferred_username`, `name`, `email`
* `realm_access.roles` — realm role **`cm-admin`** = global administrator
* `groups` — group names (no leading slash, "full path" off) from a group-membership mapper on a default client scope
* `azp` — client id for service tokens (fallback `client_id`)

Users (password = username): `admin` (cm-admin), `alice` (group `orders-editors`), `bob` (group `orders-approvers`),
`carol` (group `payments-team`), `dave` (group `viewers`).

### Authorization (Management API)

Authentication comes from the IdP; **authorization is stored in the app** (portable across IdPs):
`namespace_role_bindings(namespace, subject_type: user|group, subject, role)`.
`subject` = `preferred_username` for users, group name for groups.

Roles (hierarchical): `viewer` < `editor` < `approver` < `owner`.
| Action | Min role |
|---|---|
| read namespace, flags, versions, CRs, audit | viewer |
| create/update flag metadata, submit change (apply or open CR), promote, request rollback, cancel own CR | editor |
| approve / reject CR (never own) | approver |
| manage policies, members, service access grants | owner |
| create/delete namespaces and environments | realm role `cm-admin` (implicit owner everywhere) |

Seeded bindings: `orders`: group `orders-editors`→editor, group `orders-approvers`→approver, group `viewers`→viewer;
`payments`: group `payments-team`→approver, user `alice`→viewer, group `viewers`→viewer.

## Management API (ASP.NET Core, port 8080 in container, 5001 on host)

All routes under `/api`, JSON camelCase, errors as RFC7807 `ProblemDetails` (validation → 400 with
`errors` list, forbidden → 403, not found → 404, conflict → 409). Auth: JWT Bearer (audience `config-manager-api`).

| Method & route | Notes |
|---|---|
| `GET /api/me` | `{ username, name, email, isAdmin, groups[], namespaceRoles: { "<ns>": "<role>" } }` |
| `GET /api/environments` · `POST` (admin) `{key,name,sortOrder}` | |
| `GET /api/namespaces` | only those the user can see (admin: all). Item: `{ key, name, description, myRole }` |
| `POST /api/namespaces` (admin) `{key,name,description}` | creates default policies for every env (prod: CR required, 1 approval; others: not required) |
| `GET/PUT /api/namespaces/{ns}` · `DELETE` (admin) | |
| `GET /api/namespaces/{ns}/policies` → `[{environment, requireChangeRequest, requiredApprovals}]` · `PUT /api/namespaces/{ns}/policies/{env}` (owner) | |
| `GET/POST /api/namespaces/{ns}/members` · `DELETE /api/namespaces/{ns}/members/{id}` (owner) | `{id, subjectType, subject, role}` |
| `GET/POST /api/namespaces/{ns}/service-access` · `DELETE …/{id}` (owner) | `{id, clientId, environment}` |
| `GET /api/namespaces/{ns}/flags` | `[{ key, name, description, type, environments: { "<env>": { enabled, defaultVariant, rulesCount } | null } }]` |
| `POST /api/namespaces/{ns}/flags` (editor) `{key,name,description,type}` | metadata only |
| `GET /api/namespaces/{ns}/flags/{key}` | metadata + `configs: { "<env>": FlagDefinition | null }` |
| `PUT /api/namespaces/{ns}/flags/{key}` (editor) `{name,description}` · `DELETE` (editor, only if not configured in any env → else 409) | |
| `GET /api/namespaces/{ns}/environments/{env}/config` | current live snapshot `ConfigSnapshot` (version 0 / empty flags if none) |
| `POST /api/namespaces/{ns}/environments/{env}/changes` (editor) | body `{ title, description?, forceChangeRequest?: bool, operations: [ {type:"upsert", flagKey, flag: FlagDefinition} | {type:"delete", flagKey} ] }`. Validates each op (`FlagValidator`, flag metadata exists, `flag.type` == metadata type). If policy requires CR or `forceChangeRequest` → **202** `{ outcome:"changeRequestCreated", changeRequest }` else **200** `{ outcome:"applied", version }` |
| `POST /api/namespaces/{ns}/environments/{env}/promote` (editor) `{ fromEnvironment, flagKeys?: [] , title?}` | same response shape as changes |
| `GET /api/namespaces/{ns}/environments/{env}/versions` → `[{version, publishedAt, publishedBy, message, changeRequestId}]` | newest first |
| `GET /api/namespaces/{ns}/environments/{env}/versions/{v}` → `{ ...meta, snapshot: ConfigSnapshot }` | |
| `POST /api/namespaces/{ns}/environments/{env}/versions/{v}/rollback` (editor) `{ reason? }` | same response shape as changes |
| `GET /api/change-requests?namespace=&environment=&status=` | statuses: `pending`, `applied`, `rejected`, `cancelled`, `conflict` |
| `GET /api/change-requests/{id}` | `{ id, namespace, environment, title, description, status, author, createdAt, baseVersion, appliedVersion, requiredApprovals, operations[], reviews[{reviewer, decision:"approved"|"rejected", comment, at}], diff: [{flagKey, type:"upsert"|"delete", before: FlagDefinition|null, after: FlagDefinition|null}], canApprove, canCancel }` |
| `POST /api/change-requests/{id}/approve` `{comment?}` (approver, not author) | when approvals ≥ required → apply → status `applied` |
| `POST /api/change-requests/{id}/reject` `{comment?}` (approver, not author) | status `rejected` |
| `POST /api/change-requests/{id}/cancel` (author or owner) | status `cancelled` |
| `GET /api/audit?namespace=&take=100` | `[{ at, actor, action, namespace, environment, details }]` |
| `GET /health/live`, `GET /health/ready` | anonymous |

Conflict rule when applying a CR: for every flag touched by the CR, if its current config differs from its config
at `base_version` → CR status `conflict`, nothing applied.

Every applied change: (1) update `flag_environment_configs`, (2) build full `ConfigSnapshot` with
`version = last + 1`, (3) insert into `published_snapshots`, (4) `pg_notify`, (5) audit entry — all in one transaction.

Seed (config `Seed:Enabled=true`, only when DB empty): envs `dev`,`hml`,`prod`; namespaces `orders`, `payments`;
policies (prod: CR required, 1 approval); bindings above; flags + configs in all envs, version 1 snapshots:
* orders: `new-checkout` (boolean, rule tenant in [acme] → on, rollout 10% on), `max-items-per-order` (integer: small=10, large=50, default small),
  `checkout-banner` (string: none="", blackfriday="Black Friday: 30% off!")
* payments: `pix-enabled` (boolean), `fee-percentage` (double: standard=2.5, promo=1.0), `payment-providers` (object: `{"primary":"stripe","fallback":"adyen"}`)
* service access: `orders-service` → orders/`*`; `payments-service` → payments/`*`.

## Distribution API (ASP.NET Core, port 8080 in container, 5002 on host)

Read-only, horizontally scalable, keeps an in-memory cache of the latest snapshot per (ns, env).
Auth: JWT Bearer (audience `config-distribution`). Authorization: token client id (`azp`/`client_id`) must have a
row in `service_access_grants` matching `(namespace, environment or '*')` → else **403**.
Cache refreshed by `LISTEN config_published` / `LISTEN access_changed` (dedicated Npgsql connection with reconnect)
plus a safety poll every 30s.

| Route | Description |
|---|---|
| `GET /api/v1/snapshots/{ns}/{env}` | `ConfigSnapshot` JSON. Header `ETag: "<version>"`; `If-None-Match` → **304**. 404 if no snapshot. |
| `GET /api/v1/snapshots/{ns}/{env}/stream` | **SSE** (`text/event-stream`). Immediately sends `event: snapshot` + `id: <version>` + `data: <snapshot json single line>`; again on every new version; `: keepalive` comment every 15s. |
| `POST /ns/{ns}/env/{env}/ofrep/v1/evaluate/flags/{key}` | OFREP single evaluation. Body `{ "context": { "targetingKey": "...", ...attrs } }`. 200 `{ key, value, reason, variant, metadata:{} }`; 404 `{ key, errorCode:"FLAG_NOT_FOUND", errorDetails }`; disabled → 200 with `reason:"DISABLED"` and **no value** is not allowed by OFREP, so return `{key, reason:"DISABLED", value:null}`. |
| `POST /ns/{ns}/env/{env}/ofrep/v1/evaluate/flags` | OFREP bulk: `{ flags: [ ...single results ] }`, supports `ETag`/`If-None-Match`. |
| `GET /health/live`, `GET /health/ready` | anonymous; ready = DB reachable |

OFREP reasons: `STATIC`, `DEFAULT`, `TARGETING_MATCH`, `SPLIT`, `DISABLED`, `ERROR`. Context values are normalized
to strings (bool → `true`/`false`, numbers invariant) before calling `FlagEvaluator`.

## OpenFeature .NET provider `ConfigManager.OpenFeature`

`ConfigManagerProvider : OpenFeature.FeatureProvider` (OpenFeature .NET SDK 2.x), metadata name `ConfigManager`.

Options (`ConfigManagerProviderOptions`, bindable from config section `ConfigManager`):
`DistributionUrl`, `Namespace`, `Environment`, `TokenEndpoint`, `ClientId`, `ClientSecret`, `Scope?`,
`Mode` (`Streaming` default | `Polling`), `PollInterval` (30s), `CacheFilePath?` (optional last-known-good file),
`InitializationTimeout` (10s).

Behavior: client-credentials token cached until 30s before expiry; `InitializeAsync` fetches snapshot (falls back
to `CacheFilePath` if unreachable, else throws → provider `ERROR`); background loop consumes SSE with exponential
backoff reconnect (falls back to polling with ETag); on new version emits `ProviderConfigurationChanged` with
changed flag keys; evaluation is local via `FlagEvaluator`; type mismatch → `TYPE_MISMATCH`; disabled → caller
default with reason `DISABLED`; flag metadata exposes `version`.
DI helper: `services.AddConfigManagerOpenFeature(configuration.GetSection("ConfigManager"))` registering the provider
with `Api.Instance` at startup (hosted service) and an `IFeatureClient` singleton.

## Sample services

* **orders-api** (`:5101`): namespace `orders`, env `dev`, client `orders-service`.
  `GET /checkout?tenant=acme&userId=u1` → `{ newCheckout, maxItemsPerOrder, banner, details }`.
* **payments-api** (`:5102`): namespace `payments`, env `prod` (changes need CR approval), client `payments-service`.
  `GET /payment-options?country=BR&userId=u1` → `{ pixEnabled, feePercentage, providers }`.
* Both: `GET /flags` dumps current evaluations; `/health/live`.

## Ports (host)

| Service | Port |
|---|---|
| web | 3000 |
| keycloak | 8080 |
| management-api | 5001 |
| distribution-api | 5002 |
| orders-api | 5101 |
| payments-api | 5102 |
| postgres | 5432 |
