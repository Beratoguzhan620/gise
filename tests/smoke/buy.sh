#!/usr/bin/env bash
# Against a running stack: an organizer puts an event on sale, a buyer takes two seats, pays on the
# provider's page, the webhook settles the order, then the buyer refunds it.
set -euo pipefail

API=${API:-http://localhost:8080}
PAY=${PAY:-http://localhost:8081}
RUN=$(date +%s%N)

json() { python3 -c "import json,sys; d=json.load(sys.stdin); print(eval('d' + sys.argv[1]))" "$1"; }
post() {
  local body=${2:-}
  [ -n "$body" ] || body='{}'
  curl -fsS -X POST "$API$1" -H 'Content-Type: application/json' ${TOKEN:+-H "Authorization: Bearer $TOKEN"} -d "$body"
}
get() { curl -fsS "$API$1" ${TOKEN:+-H "Authorization: Bearer $TOKEN"}; }

TOKEN=$(post /api/auth/register "{\"email\":\"organizer-$RUN@example.com\",\"displayName\":\"Organizatör\",\"password\":\"correct-horse-battery\"}" | json "['accessToken']")
ORG=$(post /api/organizations '{"name":"Duman Sahne"}' | json "['id']")
VENUE=$(post "/api/organizations/$ORG/venues" '{"name":"Atatürk Kültür Merkezi","city":"Samsun","sections":[{"key":"A","name":"Parter","kind":"Seated","rows":[{"label":"A","seats":12},{"label":"B","seats":12}]},{"key":"floor","name":"Ayakta","kind":"Standing","capacity":100}]}' | json "['id']")
STARTS=$(date -u -d '+30 days' +%Y-%m-%dT%H:%M:%SZ)
SALE=$(date -u -d '-1 minute' +%Y-%m-%dT%H:%M:%SZ)
EVENT=$(post "/api/organizations/$ORG/events" "{\"venueId\":\"$VENUE\",\"title\":\"Smoke $RUN\",\"startsAt\":\"$STARTS\",\"saleStartsAt\":\"$SALE\",\"categories\":[{\"name\":\"Parter\",\"price\":650},{\"name\":\"Ayakta\",\"price\":350}],\"pricing\":[{\"section\":\"A\",\"category\":\"Parter\"},{\"section\":\"floor\",\"category\":\"Ayakta\"}]}" | json "['id']")
post "/api/organizations/$ORG/events/$EVENT/publish" > /dev/null
echo "Event $EVENT on sale"

TOKEN=$(post /api/auth/register "{\"email\":\"buyer-$RUN@example.com\",\"displayName\":\"Alıcı\",\"password\":\"correct-horse-battery\"}" | json "['accessToken']")
HOLD=$(post "/api/events/$EVENT/holds" '{"seats":[{"section":"A","row":"A","number":6},{"section":"A","row":"A","number":7}]}' | json "['id']")
ORDER_JSON=$(post "/api/holds/$HOLD/checkout")
ORDER=$(echo "$ORDER_JSON" | json "['id']")
CHECKOUT=$(echo "$ORDER_JSON" | json "['checkoutUrl']")
echo "Order $ORDER: paying at $CHECKOUT"

PAYMENT=${CHECKOUT##*/}
curl -fsS -o /dev/null -X POST "$PAY/checkout/$PAYMENT/pay"

for _ in $(seq 1 30); do
  STATUS=$(get "/api/orders/$ORDER" | json "['status']")
  [ "$STATUS" = "Paid" ] && break
  sleep 1
done
[ "$STATUS" = "Paid" ] || { echo "Order is $STATUS, expected Paid"; exit 1; }
echo "Paid; the webhook arrived"

SEATS=$(get "/api/events/$EVENT/availability" | json "['sections'][0]['rows'][0]['seats']")
[ "$SEATS" = "111110011111" ] || { echo "Seat map is $SEATS"; exit 1; }

STATUS=$(post "/api/orders/$ORDER/refund" | json "['status']")
[ "$STATUS" = "Refunded" ] || { echo "Refund is $STATUS"; exit 1; }
echo "Refunded"
