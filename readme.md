# ShopShop E-Commerce Microservices

A small e-commerce backend built with **.NET 10 Minimal APIs** as a hands-on way to learn microservice architecture.

Each service owns its own PostgreSQL database and trusts JWTs issued by Identity. Ordering calls Catalog over resilient HTTP and publishes order events to RabbitMQ through a transactional outbox.

For the concepts used and where to find them in the code, see [docs/architecture.md](docs/architecture.md).

## Contents

- [Services](#services)
- [Technology stack](#technology-stack)
- [Getting started](#getting-started)
- [Roadmap](#roadmap)

## Services

Every API also exposes `GET /health`, which returns `{ Status, Service }`.

### Identity

Handles users, registration, login, JWT issuing and admin user management.

```text
POST   /api/auth/register
POST   /api/auth/login
POST   /api/auth/refresh          (not implemented yet)
POST   /api/auth/logout           (not implemented yet)

GET    /api/users/me
PUT    /api/users/me

POST   /api/admin/users/search
GET    /api/admin/users/{id}
POST   /api/admin/users
PUT    /api/admin/users/{id}
DELETE /api/admin/users/{id}
```

### Catalog

Handles what can be purchased: products and categories.

```text
POST   /api/products/search
GET    /api/products/{id}

POST   /api/categories/search
GET    /api/categories/{id}

POST   /api/admin/products
PUT    /api/admin/products/{id}
DELETE /api/admin/products/{id}

POST   /api/admin/categories
PUT    /api/admin/categories/{id}
DELETE /api/admin/categories/{id}
```

### Ordering

Handles what customers have purchased: orders, order items and status. It also publishes order events.

```text
POST   /api/orders
POST   /api/orders/search
GET    /api/orders/{id}
POST   /api/orders/{id}/cancel

POST   /api/admin/orders/search
GET    /api/admin/orders/{id}
PUT    /api/admin/orders/{id}/status
POST   /api/admin/orders/{id}/cancel
```

Order rules:

- Missing or inactive products are skipped and returned as `SkippedProductIds`. If no product is valid, the request fails with 400.
- Customers can cancel only `Pending`, `Confirmed` or `Processing` orders; otherwise they get 409. Admins can set any status.
- `order.cancelled` is sent only when an order actually changes to `Cancelled`.

## Technology stack

| Area | Technology |
|---|---|
| Runtime and APIs | .NET 10, ASP.NET Core Minimal APIs |
| Data | Entity Framework Core 10, Npgsql provider, PostgreSQL 16 |
| Messaging | RabbitMQ 4 with `RabbitMQ.Client` 7 (hand-rolled plumbing, no MassTransit) |
| Security | JWT Bearer (`System.IdentityModel.Tokens.Jwt`), ASP.NET Core authorization policies, ASP.NET `PasswordHasher<T>` |
| Validation | FluentValidation 12 |
| Resilience | `Microsoft.Extensions.Http.Resilience` (standard resilience handler) |
| Logging | Serilog (console, CLEF files, request logging, environment enrichers) |
| API docs | Swashbuckle / Swagger UI with a Bearer scheme |
| Testing | xUnit, Moq, EF Core InMemory provider |
| Local infrastructure | Docker Compose (PostgreSQL + RabbitMQ with management UI) |

## Getting started

### 1. Start PostgreSQL and RabbitMQ

Create a `.env` file next to `docker-compose.yml` with:

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

These credentials are applied only the first time a container starts with an empty volume.

### 2. Configure user secrets

Connection strings and secrets are empty in `appsettings.json` and come from user secrets:

```bash
dotnet user-secrets set "<Key>" "<Value>" --project src/Services/<Svc>/<Svc>.Api
```

| Service | Keys |
|---|---|
| Identity | `ConnectionStrings:AuthDb`, `JwtSettings:Secret` (32+ chars), `JwtSettings:Issuer`, `JwtSettings:Audience`, `JwtSettings:ExpirationInMinutes` |
| Catalog | `ConnectionStrings:CatalogDb`, `JwtSettings:*` (same values as Identity) |
| Ordering | `ConnectionStrings:OrderDb`, `JwtSettings:*` (same values as Identity), `CatalogService:BaseUrl`, `RabbitMq:HostName`, `RabbitMq:UserName`, `RabbitMq:Password` (optional `Port`, `VirtualHost`) |

`RabbitMq` credentials must match `RABBITMQ_DEFAULT_USER` / `RABBITMQ_DEFAULT_PASS`. `CatalogService:BaseUrl` must match the URL Catalog is running on.

### 3. Build, test and run

```bash
dotnet build ShopShop.slnx
dotnet test ShopShop.slnx

dotnet run --project src/Services/Identity/Identity.Api
dotnet run --project src/Services/Catalog/Catalog.Api
dotnet run --project src/Services/Ordering/Ordering.Api
```

Migrations are applied automatically on startup in Development.

| Service | URLs (from `launchSettings.json`) | Swagger |
|---|---|---|
| Identity | https://localhost:7244, http://localhost:5178 | `/swagger` |
| Catalog | https://localhost:7151, http://localhost:5016 | `/swagger` |
| Ordering | https://localhost:7055, http://localhost:5077 | `/swagger` |
| RabbitMQ management | http://localhost:15672 | |

### 4. Watch an order event without a consumer

Until a consumer exists, nothing is bound to `shopshop.orders`. With `mandatory: true`, publishes fail as unroutable and stay in the outbox. To see events:

1. In the RabbitMQ UI, create a durable queue `debug.order-events`.
2. Bind it from exchange `shopshop.orders` with routing key `order.*`. Create the exchange first (type `topic`, durable) if it doesn't exist yet.
3. Place an order. Ordering logs `Outbox message published`, and the message appears under **Queues → debug.order-events → Get messages**.

Delete the debug queue once real consumers exist.

## Roadmap

Nothing below is implemented yet:

- [ ] **Payment.Worker**: simulated payment service consuming `order.placed` (`payment.order-events`) and publishing a success or failure result
- [ ] **Ordering handles payment results**: success sets the order to `Confirmed`. Failure sets it to `Failed` with a reason the customer sees on the order, and sends nothing to Notification
- [ ] **Notification.Api**: live SignalR page showing successful payments and order status updates (`notification.order-events`)
- [ ] **Redis caching** for Catalog product and category reads (cache-aside, invalidated on admin writes)
- [ ] Seq and correlation ids across HTTP and messages
- [ ] Integration tests with Testcontainers (PostgreSQL, RabbitMQ)
- [ ] OpenTelemetry tracing and metrics
- [ ] Readiness health checks (database, broker)
- [ ] Identity refresh tokens and logout
- [ ] Containerising all services
- [ ] Pagination for all `POST …/search` endpoints (page number, page size, total count)

The outbox currently assumes a single Ordering instance. Scaling out would need row locking (`FOR UPDATE SKIP LOCKED`).
