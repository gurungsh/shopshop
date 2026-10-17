# Configuration

What each service needs to run locally. Non-secret local values (connection strings with the throwaway `shop`/`shop` dev credentials, JWT issuer and audience, URLs) are committed in each service's `appsettings.Development.json`. Real secrets, the Stripe key and Identity's signing key, come from **user secrets**. Never put those in a committed file.

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
docker compose up -d postgres rabbitmq redis lgtm
```

To run the apps in containers as well, use `docker compose up -d --build`; see [containers](containers.md).

These credentials are applied only the first time a container starts with an empty volume. Redis and the Grafana `lgtm` container need no `.env` values.

## User secrets

Only two values are real secrets: `JwtSettings:PrivateKeyPem` (Identity) and `Stripe:SecretKey` (Payment). Copy `.env.example` to `.env`, put your Stripe test key in it, and run `scripts/setup-user-secrets.sh`. It also generates the signing key if `~/shopshop-keys/jwt-private.pem` is missing. To set a key by hand:

```bash
dotnet user-secrets set "<Key>" "<Value>" --project src/Services/<Svc>/<Svc>.Api
```

Every key a service reads is listed below. Only the two secrets above are missing from `appsettings.Development.json`; any key can be overridden with a user secret.

| Service | Keys |
|---|---|
| Identity | `ConnectionStrings:AuthDb`, `JwtSettings:PrivateKeyPem`, `JwtSettings:KeyId`, `JwtSettings:Issuer`, `JwtSettings:Audience`, `JwtSettings:ExpirationInMinutes` |
| Catalog | `ConnectionStrings:CatalogDb`, `ConnectionStrings:Redis`, `JwtSettings:MetadataAddress`, `JwtSettings:Issuer`, `JwtSettings:Audience`, optional `JwtSettings:RequireHttpsMetadata` (default `true`) |
| Ordering | `ConnectionStrings:OrderDb`, `JwtSettings:MetadataAddress`, `JwtSettings:Issuer`, `JwtSettings:Audience`, optional `JwtSettings:RequireHttpsMetadata`, `CatalogService:BaseUrl`, `RabbitMq:HostName`, `RabbitMq:UserName`, `RabbitMq:Password` (optional `Port`, `VirtualHost`), optional `Payment:MaxRetries`, `Outbox:*` |
| Payment | `ConnectionStrings:PaymentDb`, `RabbitMq:*`, `Stripe:SecretKey` (a test key, `sk_test_...`), optional `Outbox:*` |
| Notification | `RabbitMq:*` |
| Gateway | `Cors:AllowedOrigins`, `ReverseProxy:Clusters:<name>:Destinations` (routes are in `appsettings.json`, the local cluster addresses and origin in `appsettings.Development.json`) |

Notes:

- `RabbitMq:*` uses the same values for Ordering, Payment and Notification, and must match `RABBITMQ_DEFAULT_USER` / `RABBITMQ_DEFAULT_PASS`.
- `JwtSettings:Issuer` and `Audience` in Catalog and Ordering must match Identity's.
- `JwtSettings:MetadataAddress` is Identity's discovery URL, for example `https://localhost:7244/.well-known/openid-configuration`.
- `CatalogService:BaseUrl` must match the URL Catalog is running on.
- `ConnectionStrings:Redis` is for example `localhost:6379`. Catalog caps `connectTimeout` at 1000 ms and `asyncTimeout` at 500 ms in every environment, so requests stay fast when Redis is down.
- `init-dbs.sql` creates the databases when the PostgreSQL container starts with an empty volume. Migrations create the tables.
- Tunables that are not environment-specific live in the service's `appsettings.json`: Ordering's `Payment:MaxRetries`, Catalog's `Cache:*` TTLs, `Outbox:*` (Ordering, Payment), `RabbitMq:Port` / `VirtualHost`, and Identity's `JwtSettings:ExpirationInMinutes`.
- Required values have no default. A service fails at startup with a clear message when one is missing or invalid: JWT `Issuer` and `Audience` (Identity, Catalog, Ordering), `MetadataAddress`, connection strings, `RabbitMq` host and credentials, the Stripe key, and the Gateway's `Cors:AllowedOrigins` and cluster addresses.

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
| Gateway | https://localhost:7080, http://localhost:5080 | `/swagger` (combined) |
| RabbitMQ management | http://localhost:15672 | |
| Redis | localhost:6379 | |
| Grafana (`lgtm`) | http://localhost:3000 (`admin` / `admin`) | |
| OTLP collector (`lgtm`) | localhost:4317 (gRPC), localhost:4318 (HTTP) | |

Swagger is controlled by `OpenApi:Enabled` (default `false`) on the gateway, Identity, Catalog and Ordering. Each `appsettings.Development.json` sets it to `true`. In any other environment, set `OpenApi__Enabled=true` to turn it on. See [gateway](gateway.md#combined-swagger).

The gateway calls Identity, Catalog and Ordering on their HTTPS ports, so run those with the `https` profile. Its local cluster addresses and `Cors:AllowedOrigins` are in `Gateway.Api/appsettings.Development.json` and are not secret. Other environments must supply both, because the base `appsettings.json` has only the routes.

Services export telemetry to `http://localhost:4317` by default. Set the `OTEL_EXPORTER_OTLP_ENDPOINT` environment variable to change it (see [observability](observability.md)).

Run a service with the `https` profile, for example `dotnet run --project src/Services/Identity/Identity.Api --launch-profile https`. Migrations are applied automatically on startup in Development.
