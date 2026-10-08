# Configuration

What each service needs to run locally. Connection strings and secrets are empty in `appsettings.json` and come from **user secrets**. Never put them in a committed file.

## Contents

- [Docker environment](#docker-environment)
- [User secrets](#user-secrets)
- [Identity signing key](#identity-signing-key)
- [Ports](#ports)

## Docker environment

Create a `.env` file next to `docker-compose.yml`:

```text
POSTGRES_USER=
POSTGRES_PASSWORD=
POSTGRES_DB=
RABBITMQ_DEFAULT_USER=
RABBITMQ_DEFAULT_PASS=
```

```bash
docker compose up -d
```

These credentials are applied only the first time a container starts with an empty volume. Redis needs no `.env` values.

## User secrets

```bash
dotnet user-secrets set "<Key>" "<Value>" --project src/Services/<Svc>/<Svc>.Api
```

| Service | Keys |
|---|---|
| Identity | `ConnectionStrings:AuthDb`, `JwtSettings:PrivateKeyPem`, `JwtSettings:KeyId`, `JwtSettings:Issuer`, `JwtSettings:Audience`, `JwtSettings:ExpirationInMinutes` |
| Catalog | `ConnectionStrings:CatalogDb`, `ConnectionStrings:Redis`, `JwtSettings:MetadataAddress`, `JwtSettings:Issuer`, `JwtSettings:Audience` |
| Ordering | `ConnectionStrings:OrderDb`, `JwtSettings:MetadataAddress`, `JwtSettings:Issuer`, `JwtSettings:Audience`, `CatalogService:BaseUrl`, `RabbitMq:HostName`, `RabbitMq:UserName`, `RabbitMq:Password` (optional `Port`, `VirtualHost`) |
| Payment | `ConnectionStrings:PaymentDb`, `RabbitMq:*`, `Stripe:SecretKey` (a test key, `sk_test_...`) |
| Notification | `RabbitMq:*` |

Notes:

- `RabbitMq:*` uses the same values for Ordering, Payment and Notification, and must match `RABBITMQ_DEFAULT_USER` / `RABBITMQ_DEFAULT_PASS`.
- `JwtSettings:Issuer` and `Audience` in Catalog and Ordering must match Identity's.
- `JwtSettings:MetadataAddress` is Identity's discovery URL, for example `https://localhost:7244/.well-known/openid-configuration`.
- `CatalogService:BaseUrl` must match the URL Catalog is running on.
- `ConnectionStrings:Redis` is for example `localhost:6379,connectTimeout=1000,asyncTimeout=500`. The short timeouts keep requests fast when Redis is down.
- `PaymentDb` is created on first startup.
- Ordering's `Payment:MaxRetries` (default 3) and Catalog's `Cache:*` TTLs are not secrets and live in their `appsettings.json`.

## Identity signing key

Only Identity holds a key. Generate the pair once and keep the private key outside the repo:

```bash
openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -out ~/shopshop-keys/jwt-private.pem
```

`JwtSettings:PrivateKeyPem` needs the full PEM text, line breaks included:

| Shell | How to set it |
|---|---|
| Git Bash | `dotnet user-secrets set "JwtSettings:PrivateKeyPem" "$(cat ~/shopshop-keys/jwt-private.pem)"` |
| PowerShell | `dotnet user-secrets set "JwtSettings:PrivateKeyPem" (Get-Content <file> -Raw)` |

`JwtSettings:KeyId` is a label for the key (for example `shopshop-2026-10`). It goes into each token header and is how key rotation would work. See the [authentication flow](architecture.md#authentication-flow).

## Ports

| Service | URLs (from `launchSettings.json`) | Swagger |
|---|---|---|
| Identity | https://localhost:7244, http://localhost:5178 | `/swagger` |
| Catalog | https://localhost:7151, http://localhost:5016 | `/swagger` |
| Ordering | https://localhost:7055, http://localhost:5077 | `/swagger` |
| Payment | https://localhost:7075, http://localhost:5051 | none (`/health` only) |
| Notification | https://localhost:7062, http://localhost:5062 | none (`/health` only) |
| Gateway | https://localhost:7080, http://localhost:5080 | `/swagger` (combined, Development only) |
| RabbitMQ management | http://localhost:15672 | |
| Redis | localhost:6379 | |

The gateway calls Identity, Catalog and Ordering on their HTTPS ports, so run those with the `https` profile. Its addresses and `Cors:AllowedOrigins` are in `Gateway.Api/appsettings.json` and are not secret.

Run a service with the `https` profile, for example `dotnet run --project src/Services/Identity/Identity.Api --launch-profile https`. Migrations are applied automatically on startup in Development.
