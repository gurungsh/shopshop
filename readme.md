# ShopShop E-Commerce Microservices

A small e-commerce backend built with **.NET 10 Minimal APIs** as a hands-on way to learn microservice architecture.

Each service owns its own PostgreSQL database and trusts JWTs issued by Identity. Ordering calls Catalog over resilient HTTP and publishes order events to RabbitMQ through a transactional outbox. Payment consumes those events, charges the order through Stripe (test mode) and publishes the result, which Ordering applies to the order. Notification listens for the resulting order updates.

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

Handles what customers have purchased: orders, order items and status. It publishes order events and applies payment results.

```text
POST   /api/orders
POST   /api/orders/search
GET    /api/orders/{id}
POST   /api/orders/{id}/cancel
POST   /api/orders/{id}/retry-payment

POST   /api/admin/orders/search
GET    /api/admin/orders/{id}
PUT    /api/admin/orders/{id}/status
POST   /api/admin/orders/{id}/cancel
```

Order rules:

- Missing or inactive products are skipped and returned as `SkippedProductIds`. If no product is valid, the request fails with 400.
- `paymentMethodId` is required and must be a Stripe payment method id (`pm_...`). Card details never reach our services.
- A new order is `Pending` until Payment answers. Success makes it `Confirmed`. Failure makes it `PaymentFailed`, with `paymentFailureReason` and `paymentRetriesRemaining` on the order.
- A `PaymentFailed` order can be retried with `POST /api/orders/{id}/retry-payment` and a new `paymentMethodId`, up to `Payment:MaxRetries` times (default 3, so 4 attempts in total). If the last retry fails, the order becomes `Failed`.
- Customers can cancel only `Pending`, `Confirmed`, `Processing` or `PaymentFailed` orders; otherwise they get 409. Admins can set any status.
- `order.cancelled` is sent only when an order actually changes to `Cancelled`.
- A payment result for an older attempt, or for an order that is no longer `Pending`, is ignored.

### Payment

Charges orders through Stripe. It has no public API apart from `GET /health` and only reacts to events:

- Consumes `order.placed` and `order.payment-retried` from the `payment.order-events` queue.
- Charges the order total in USD and records one `PaymentTransaction` per order attempt, so a redelivered message never charges twice.
- Publishes `payment.succeeded` or `payment.failed` to the `shopshop.payments` exchange through its own outbox. Ordering consumes these from `ordering.payment-events`.

With a test-mode key, the payment method id decides the outcome:

| `paymentMethodId` | Outcome |
|---|---|
| `pm_card_visa` | Succeeds |
| `pm_card_chargeDeclined` | Fails: "Your card was declined." |
| `pm_card_chargeDeclinedInsufficientFunds` | Fails: "Your card has insufficient funds." |

Stripe's card decline messages are shown to the customer as they are. Anything else (Stripe unavailable, a rejected request) shows a generic "We couldn't process your payment. Please try again." and the real error is only logged.

### Notification

Tells customers what happened to their order. It has no public API apart from `GET /health`.

- Consumes `order.confirmed`, `order.payment-failed` and `order.cancelled` from the `notification.order-events` queue.
- First iteration: each event is only logged (console and `logs/notification-api-*.clef`). Pushing them to a live page comes later.

## Technology stack

| Area | Technology |
|---|---|
| Runtime and APIs | .NET 10, ASP.NET Core Minimal APIs |
| Data | Entity Framework Core 10, Npgsql provider, PostgreSQL 16 |
| Messaging | RabbitMQ 4 with `RabbitMQ.Client` 7 (hand-rolled plumbing, no MassTransit) |
| Payments | Stripe in test mode (`Stripe.net`) |
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
| Payment | `ConnectionStrings:PaymentDb`, `RabbitMq:*` (same values as Ordering), `Stripe:SecretKey` (a test key, `sk_test_...`) |
| Notification | `RabbitMq:*` (same values as Ordering) |

`RabbitMq` credentials must match `RABBITMQ_DEFAULT_USER` / `RABBITMQ_DEFAULT_PASS`. `CatalogService:BaseUrl` must match the URL Catalog is running on. `PaymentDb` is created on first startup.

Ordering's `Payment:MaxRetries` (default 3) is not a secret and lives in its `appsettings.json`.

### 3. Build, test and run

```bash
dotnet build ShopShop.slnx
dotnet test ShopShop.slnx

dotnet run --project src/Services/Identity/Identity.Api
dotnet run --project src/Services/Catalog/Catalog.Api
dotnet run --project src/Services/Ordering/Ordering.Api
dotnet run --project src/Services/Payment/Payment.Api
dotnet run --project src/Services/Notification/Notification.Api
```

Migrations are applied automatically on startup in Development.

| Service | URLs (from `launchSettings.json`) | Swagger |
|---|---|---|
| Identity | https://localhost:7244, http://localhost:5178 | `/swagger` |
| Catalog | https://localhost:7151, http://localhost:5016 | `/swagger` |
| Ordering | https://localhost:7055, http://localhost:5077 | `/swagger` |
| Payment | https://localhost:7075, http://localhost:5051 | none (`/health` only) |
| Notification | https://localhost:7062, http://localhost:5062 | none (`/health` only) |
| RabbitMQ management | http://localhost:15672 | |

### 4. Try the payment flow

1. Run all five services, register or log in, and place an order with `POST /api/orders` and `"paymentMethodId": "pm_card_chargeDeclined"`.
2. The response is always `Pending`. A few seconds later, `GET /api/orders/{id}` shows `PaymentFailed` with the reason and the retries left.
3. Retry with `POST /api/orders/{id}/retry-payment` and `{ "paymentMethodId": "pm_card_visa" }`. The order becomes `Confirmed`.
4. Both PaymentIntents appear in the Stripe dashboard (test mode), and Notification logs `Order payment failed` and then `Order confirmed`.

See [docs/architecture.md](docs/architecture.md#payment-flow) for a diagram of the flow.

### 5. Start every consumer at least once

Each consumer creates its own durable queue and bindings on startup. Until a service has run once, events meant for it are unroutable. With `mandatory: true` they fail and stay in the sender's outbox, and because the publisher keeps messages in order, **they block every later event** from that sender. After the first start the queues persist, so messages simply wait if a service is down.

| Queue | Created by | Receives |
|---|---|---|
| `payment.order-events` | Payment | `order.placed`, `order.payment-retried` |
| `ordering.payment-events` | Ordering | `payment.succeeded`, `payment.failed` |
| `notification.order-events` | Notification | `order.confirmed`, `order.payment-failed`, `order.cancelled` |

To inspect messages without consuming them, bind a durable debug queue (for example `debug.order-events` to `shopshop.orders` with routing key `order.*`) and use **Get messages** in the RabbitMQ UI. Nothing reads a debug queue, so purge or delete it when you are done.

## Roadmap

Unchecked items are not implemented yet:

- [x] **Payment**: consumes `order.placed` and `order.payment-retried`, charges through Stripe test mode, and publishes the result
- [x] **Ordering handles payment results**: success sets `Confirmed`, failure sets `PaymentFailed` (retryable) or `Failed` with a reason the customer sees on the order
- [x] **Notification.Api (first iteration)**: consumes `order.confirmed`, `order.payment-failed` and `order.cancelled` (`notification.order-events`) and logs them
- [ ] **Pagination** for all `POST …/search` endpoints (page number, page size, total count)- [ ] 
- [ ] **Redis caching** for Catalog product and category reads (cache-aside, invalidated on admin writes)
- [ ] Integration tests with Testcontainers (PostgreSQL, RabbitMQ)
- [ ] Identity refresh tokens and logout
- [ ] Containerising all services

The outboxes assume a single instance of each service. Scaling out would need row locking (`FOR UPDATE SKIP LOCKED`).
