#!/bin/sh
# Starts every part of the demo on local ports behind nginx (8080, the port Render routes to). The public address
# comes from Render (RENDER_EXTERNAL_URL), so payment pages send buyers back to the right place.
set -eu

public="${RENDER_EXTERNAL_URL:-http://localhost:8080}"

mailpit --smtp 127.0.0.1:1025 --listen 127.0.0.1:8025 --webroot /mail/ --max 500 &

cd /app/payments
ASPNETCORE_HTTP_PORTS=8082 \
Sandbox__ApiKey="$PAYMENTS_API_KEY" \
Sandbox__WebhookSecret="$PAYMENTS_WEBHOOK_SECRET" \
Sandbox__WebhookUrl=http://127.0.0.1:8081/api/payments/webhooks/sandbox \
Sandbox__PublicUrl="$public/sandbox" \
  dotnet Gise.PaymentSandbox.dll &

cd /app/api
ASPNETCORE_HTTP_PORTS=8081 \
Payments__BaseUrl=http://127.0.0.1:8082/ \
Payments__ApiKey="$PAYMENTS_API_KEY" \
Payments__WebhookSecret="$PAYMENTS_WEBHOOK_SECRET" \
Payments__ReturnUrl="$public/orders/{orderId}" \
Mail__Host=127.0.0.1 \
Mail__Port=1025 \
Demo__BaseUrl=http://127.0.0.1:8081 \
  dotnet Gise.Api.dll &
api=$!

nginx -e stderr -c /app/nginx.conf -g 'daemon off;' &
web=$!

trap 'kill 0' TERM INT
# The container lives as long as the API and nginx do.
wait "$api" "$web"
