#!/usr/bin/env bash
# End-to-end smoke test against the running docker compose stack.
# Uses real Keycloak tokens and verifies: direct publish -> SSE propagation to a sample service,
# change request + four-eyes approval in prod, rollback, and distribution authorization.
set -euo pipefail

KC=${KC:-http://localhost:8080/realms/config-manager/protocol/openid-connect/token}
API=${API:-http://localhost:5001/api}
DIST=${DIST:-http://localhost:5002}
ORDERS=${ORDERS:-http://localhost:5101}
PAYMENTS=${PAYMENTS:-http://localhost:5102}

pass=0
ok()   { echo "  ✔ $*"; pass=$((pass+1)); }
fail() { echo "  ✘ $*" >&2; exit 1; }

user_token() { curl -fsS "$KC" -d grant_type=password -d client_id=config-manager-web -d "username=$1" -d "password=$1" | jq -r .access_token; }
svc_token()  { curl -fsS "$KC" -d grant_type=client_credentials -d "client_id=$1" -d "client_secret=$1-secret" | jq -r .access_token; }
api() { local tok=$1; shift; curl -sS -H "Authorization: Bearer $tok" -H 'Content-Type: application/json' "$@"; }

# wait_for <description> <command producing value> <expected>
wait_for() {
  local desc=$1 cmd=$2 expected=$3 got=""
  for _ in $(seq 1 40); do
    got=$(eval "$cmd" 2>/dev/null || true)
    [[ "$got" == "$expected" ]] && { ok "$desc ($got)"; return; }
    sleep 0.5
  done
  fail "$desc: expected '$expected' got '$got'"
}

# Build a change body that sets defaultVariant of <flag> in <env> to <variant>
default_variant_change() {
  local tok=$1 ns=$2 env=$3 flag=$4 variant=$5 title=$6
  api "$tok" "$API/namespaces/$ns/flags/$flag" \
    | jq --arg env "$env" --arg f "$flag" --arg v "$variant" --arg t "$title" \
        '{title: $t, operations: [{type: "upsert", flagKey: $f, flag: (.configs[$env] | .defaultVariant = $v)}]}'
}

echo "▶ Tokens"
ADMIN=$(user_token admin); ALICE=$(user_token alice); BOB=$(user_token bob); CAROL=$(user_token carol); DAVE=$(user_token dave)
ORDERS_SVC=$(svc_token orders-service)
ok "obtained user and service tokens from Keycloak"

echo "▶ Health"
for u in "$API/../health/ready" "$DIST/health/ready" "$ORDERS/health/ready" "$PAYMENTS/health/ready"; do
  curl -fsS "$u" >/dev/null || fail "not ready: $u"
done
ok "all services ready"

echo "▶ Authorization"
[[ $(api "$DAVE" -o /dev/null -w '%{http_code}' -X POST "$API/namespaces/orders/environments/dev/changes" -d '{"title":"x","operations":[]}') == 403 ]] \
  && ok "viewer cannot submit changes (403)" || fail "viewer could submit change"
[[ $(curl -s -o /dev/null -w '%{http_code}' -H "Authorization: Bearer $ORDERS_SVC" "$DIST/api/v1/snapshots/payments/prod") == 403 ]] \
  && ok "orders-service cannot read payments snapshot (403)" || fail "cross-namespace read allowed"
[[ $(curl -s -o /dev/null -w '%{http_code}' "$DIST/api/v1/snapshots/orders/dev") == 401 ]] \
  && ok "anonymous distribution access rejected (401)" || fail "anonymous access allowed"

echo "▶ Targeting via sample service (orders/dev)"
wait_for "tenant acme gets new checkout" "curl -fsS '$ORDERS/checkout?tenant=acme&userId=u1' | jq -r .newCheckout" "true"

echo "▶ Direct publish in dev propagates via SSE"
ETAG=$(curl -fsS -D - -o /dev/null -H "Authorization: Bearer $ORDERS_SVC" "$DIST/api/v1/snapshots/orders/dev" | tr -d '\r' | awk -F': ' 'tolower($1)=="etag"{print $2}')
[[ $(curl -s -o /dev/null -w '%{http_code}' -H "Authorization: Bearer $ORDERS_SVC" -H "If-None-Match: $ETAG" "$DIST/api/v1/snapshots/orders/dev") == 304 ]] \
  && ok "ETag $ETAG -> 304 Not Modified" || fail "ETag not honored"
BODY=$(default_variant_change "$ALICE" orders dev checkout-banner blackfriday "Black friday banner")
OUT=$(api "$ALICE" -X POST "$API/namespaces/orders/environments/dev/changes" -d "$BODY")
[[ $(jq -r .outcome <<<"$OUT") == applied ]] && ok "alice published orders/dev v$(jq -r .version <<<"$OUT")" || fail "publish failed: $OUT"
wait_for "orders-api sees new banner" "curl -fsS '$ORDERS/checkout?tenant=x&userId=u1' | jq -r .banner" "Black Friday: 30% off!"

echo "▶ Change request with four-eyes in prod (payments/prod)"
BEFORE=$(curl -fsS "$PAYMENTS/payment-options?country=BR&userId=u1" | jq -r .feePercentage)
BODY=$(default_variant_change "$CAROL" payments prod fee-percentage promo "Promo fee")
OUT=$(api "$CAROL" -X POST "$API/namespaces/payments/environments/prod/changes" -d "$BODY")
[[ $(jq -r .outcome <<<"$OUT") == changeRequestCreated ]] && ok "prod change opened a CR" || fail "expected CR: $OUT"
CR=$(jq -r .changeRequest.id <<<"$OUT")
[[ $(api "$CAROL" -o /dev/null -w '%{http_code}' -X POST "$API/change-requests/$CR/approve" -d '{}') == 403 ]] \
  && ok "author cannot approve own CR (403)" || fail "four-eyes violated"
[[ $(curl -fsS "$PAYMENTS/payment-options?country=BR&userId=u1" | jq -r .feePercentage) == "$BEFORE" ]] && ok "pending CR not yet live (fee=$BEFORE)"
api "$ADMIN" -X POST "$API/change-requests/$CR/approve" -d '{"comment":"lgtm"}' >/dev/null
[[ $(api "$ADMIN" "$API/change-requests/$CR" | jq -r .status) == applied ]] && ok "admin approved -> applied" || fail "CR not applied"
wait_for "payments-api sees promo fee" "curl -fsS '$PAYMENTS/payment-options?country=BR&userId=u1' | jq -r .feePercentage" "1"

echo "▶ Rollback payments/prod to v1 (goes through CR)"
OUT=$(api "$ADMIN" -X POST "$API/namespaces/payments/environments/prod/versions/1/rollback" -d '{"reason":"smoke rollback"}')
[[ $(jq -r .outcome <<<"$OUT") == changeRequestCreated ]] && ok "rollback opened a CR" || fail "rollback: $OUT"
api "$CAROL" -X POST "$API/change-requests/$(jq -r .changeRequest.id <<<"$OUT")/approve" -d '{}' >/dev/null
wait_for "payments-api back to original fee" "curl -fsS '$PAYMENTS/payment-options?country=BR&userId=u1' | jq -r .feePercentage" "$BEFORE"

echo "▶ OFREP remote evaluation"
R=$(curl -fsS -H "Authorization: Bearer $ORDERS_SVC" -H 'Content-Type: application/json' \
  -X POST "$DIST/ns/orders/env/dev/ofrep/v1/evaluate/flags/new-checkout" -d '{"context":{"targetingKey":"u1","tenant":"acme"}}')
[[ $(jq -r '"\(.value)/\(.reason)"' <<<"$R") == "true/TARGETING_MATCH" ]] && ok "OFREP new-checkout => $(jq -c '{value,variant,reason}' <<<"$R")" || fail "OFREP: $R"

echo "▶ Audit"
[[ $(api "$ADMIN" "$API/audit?namespace=payments&take=50" | jq 'length') -ge 3 ]] && ok "audit entries recorded"

echo; echo "✅ smoke test passed ($pass checks)"
