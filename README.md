# Config Manager (OpenFeature)

MVP de um gerenciador de configurações / feature flags compatível com **OpenFeature**, com
namespaces × ambientes, variantes tipadas, regras de targeting, change requests com aprovação (four-eyes),
versionamento imutável com rollback, promoção entre ambientes e autenticação OIDC (Keycloak / RHBK).

Arquitetura e contratos detalhados: [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md).

| Componente | Pasta | Stack |
|---|---|---|
| Front de gestão | `web/` | React + Vite + Tailwind + shadcn/ui, TanStack Query, OIDC PKCE |
| Management API | `src/Management` | ASP.NET Core 10, EF Core, PostgreSQL |
| Distribution API | `src/Distribution` | ASP.NET Core 10, Npgsql LISTEN/NOTIFY, SSE, OFREP |
| SDK OpenFeature .NET | `src/Sdk/ConfigManager.OpenFeature` | Provider com avaliação local + push SSE ([README](src/Sdk/ConfigManager.OpenFeature/README.md)) |
| Motor compartilhado | `src/Contracts` | Modelo de snapshot, avaliador, validador |
| Exemplos | `src/Samples` | `orders-api` (orders/dev), `payments-api` (payments/prod) |
| IdP | `keycloak/` | Realm `config-manager` importado no Keycloak 26 (RHBK-compatível) |

## Subir tudo

```bash
docker compose up -d --build
./scripts/smoke.sh            # teste E2E contra a stack real (tokens reais do Keycloak)
```

| URL | O quê |
|---|---|
| http://localhost:3000 | Front (login SSO) |
| http://localhost:8080 | Keycloak (admin console: `kcadmin` / `kcadmin`) |
| http://localhost:5001/openapi/v1.json | Management API |
| http://localhost:5002 | Distribution API (snapshots, SSE, OFREP) |
| http://localhost:5101/checkout?tenant=acme&userId=u1 | Sample orders |
| http://localhost:5102/payment-options?country=BR&userId=u1 | Sample payments |

Usuários (senha = usuário):

| Usuário | Papel |
|---|---|
| `admin` | `cm-admin` (owner em tudo) |
| `alice` | editor em `orders`, viewer em `payments` |
| `bob` | approver em `orders` |
| `carol` | approver em `payments` |
| `dave` | viewer |

Clients de serviço: `orders-service` / `orders-service-secret`, `payments-service` / `payments-service-secret`.

### Roteiro de demo
1. Logue como **alice**, abra `orders` → `dev`, mude o default de `checkout-banner` para `blackfriday`.
   Publica direto (dev não exige CR) e `curl localhost:5101/checkout` muda em < 1s (SSE).
2. Logue como **carol**, em `payments` → `prod` mude `fee-percentage` → vira **change request**.
3. Logue como **admin**, aprove o CR → `curl localhost:5102/payment-options` reflete a mudança.
4. Em *History*, faça **rollback** para a v1 (também passa por CR em prod).

## Testes

```bash
dotnet test ConfigManager.slnx                      # unit + integração (Testcontainers, requer Docker)
cd web && npm test                                  # Vitest
docker compose --profile test run --rm --build tests  # suíte .NET dentro de container
```

| Projeto | Tipo |
|---|---|
| `ConfigManager.Contracts.Tests` | unit: avaliador, operadores, bucketing, validador, JSON |
| `ConfigManager.Management.Tests` | unit: RBAC, claims, máquina de estados de CR, rollback/promoção |
| `ConfigManager.Management.IntegrationTests` | Postgres real: fluxo de CR, four-eyes, conflito, rollback, pg_notify, concorrência |
| `ConfigManager.Distribution.Tests` | unit + integração: ETag/304, grants, OFREP, SSE com NOTIFY |
| `ConfigManager.OpenFeature.Tests` | unit + servidor fake: token, SSE, fallback, eventos, tipos |
| `ConfigManager.EndToEnd.Tests` | Management + Distribution + Provider no mesmo Postgres |

## Notas
* SDK .NET 10.0.111 local precisa de `AllowMissingPrunePackageData` (já em `Directory.Build.props`) por causa do NETSDK1226.
* Autenticação vem do IdP; **autorização** (papéis por namespace) fica no app via bindings de usuário/grupo —
  troca de Keycloak por RHBK (ou outro OIDC) só exige mudar `Auth__Authority`/`Auth__MetadataAddress` e mapear o claim `groups`.
