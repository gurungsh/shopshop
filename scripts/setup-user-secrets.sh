#!/usr/bin/env bash
# Sets the local values that must not be committed: database and RabbitMQ credentials (from .env),
# Identity's signing key and the Stripe key. Everything else is in each service's appsettings.Development.json.
set -euo pipefail
cd "$(dirname "$0")/.."

set -a; source .env; set +a
: "${POSTGRES_USER:?Set POSTGRES_USER in .env}"
: "${POSTGRES_PASSWORD:?Set POSTGRES_PASSWORD in .env}"
: "${RABBITMQ_DEFAULT_USER:?Set RABBITMQ_DEFAULT_USER in .env}"
: "${RABBITMQ_DEFAULT_PASS:?Set RABBITMQ_DEFAULT_PASS in .env}"
: "${STRIPE_SECRET_KEY:?Set STRIPE_SECRET_KEY (sk_test_...) in .env}"

KEY=~/shopshop-keys/jwt-private.pem
if [[ ! -f $KEY ]]; then
  mkdir -p "$(dirname "$KEY")"
  openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -out "$KEY"
  chmod 600 "$KEY"
fi

secret() { dotnet user-secrets set "$2" "$3" --project "src/Services/$1/$1.Api" >/dev/null; }
db() { echo "Host=localhost;Port=5432;Database=$1;Username=$POSTGRES_USER;Password=$POSTGRES_PASSWORD"; }

secret Identity "ConnectionStrings:AuthDb" "$(db AuthDb)"
secret Catalog "ConnectionStrings:CatalogDb" "$(db CatalogDb)"
secret Ordering "ConnectionStrings:OrderDb" "$(db OrderDb)"
secret Payment "ConnectionStrings:PaymentDb" "$(db PaymentDb)"

for svc in Ordering Payment Notification; do
  secret "$svc" "RabbitMq:UserName" "$RABBITMQ_DEFAULT_USER"
  secret "$svc" "RabbitMq:Password" "$RABBITMQ_DEFAULT_PASS"
done

secret Identity "JwtSettings:PrivateKeyPem" "$(cat "$KEY")"
secret Payment "Stripe:SecretKey" "$STRIPE_SECRET_KEY"

echo "User secrets set: connection strings, RabbitMQ credentials, signing key and Stripe key."
