#!/usr/bin/env bash
# Sets the two local secrets that must not be committed: Identity's signing key and the Stripe key.
# Everything else is in each service's appsettings.Development.json.
set -euo pipefail
cd "$(dirname "$0")/.."

set -a; source .env; set +a
: "${STRIPE_SECRET_KEY:?Set STRIPE_SECRET_KEY (sk_test_...) in .env}"

KEY=~/shopshop-keys/jwt-private.pem
if [[ ! -f $KEY ]]; then
  mkdir -p "$(dirname "$KEY")"
  openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -out "$KEY"
fi

dotnet user-secrets set "JwtSettings:PrivateKeyPem" "$(cat "$KEY")" --project src/Services/Identity/Identity.Api >/dev/null
dotnet user-secrets set "Stripe:SecretKey" "$STRIPE_SECRET_KEY" --project src/Services/Payment/Payment.Api >/dev/null

echo "User secrets set for Identity (signing key) and Payment (Stripe key)."
