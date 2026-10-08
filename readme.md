# ShopShop E-Commerce Microservices

A small e-commerce backend built with **.NET 10 Minimal APIs** as a hands-on way to learn microservice architecture.

Customers place orders, Payment charges them through Stripe (test mode), and Ordering applies the result. Each service owns its PostgreSQL database, services trust JWTs issued by Identity, and order and payment events travel over RabbitMQ through a transactional outbox.

## Contents

- [Services](#services)
- [Technology stack](#technology-stack)
- [Getting started](#getting-started)
- [Documentation](#documentation)
- [Roadmap](#roadmap)

## Services

| Service | What it does | HTTPS port |
|---|---|---|
| Identity | Users, registration, login, JWT issuing, admin user management | 7244 |
| Catalog | Products and categories | 7151 |
| Ordering | Orders and their status. Publishes order events, applies payment results | 7055 |
| Payment | Charges orders through Stripe. Event-driven, `/health` only | 7075 |
| Notification | Logs order updates for now. Event-driven, `/health` only | 7062 |
| Gateway | YARP reverse proxy in front of Identity, Catalog and Ordering. Combined Swagger in Development | 7080 |

Every API also exposes `GET /health`. The full endpoint list is in the [API reference](docs/api.md).

## Technology stack

| Area | Technology |
|---|---|
| Runtime and APIs | .NET 10, ASP.NET Core Minimal APIs |
| Data | Entity Framework Core 10, Npgsql, PostgreSQL 16 |
| Messaging | RabbitMQ 4 with `RabbitMQ.Client` 7 (hand-rolled plumbing, no MassTransit) |
| Payments | Stripe in test mode (`Stripe.net`) |
| Security | JWT Bearer with RS256 signing keys, ASP.NET Core authorization, `PasswordHasher<T>` |
| Validation | FluentValidation 12 |
| Resilience | `Microsoft.Extensions.Http.Resilience` (standard resilience handler) |
| Logging | Serilog (console, CLEF files, request logging) |
| API docs | Swashbuckle / Swagger UI with a Bearer scheme |
| Testing | xUnit, Moq, EF Core InMemory provider |
| Local infrastructure | Docker Compose (PostgreSQL + RabbitMQ with management UI) |

## Getting started

1. **Start PostgreSQL and RabbitMQ.** Create a `.env` file (see [configuration](docs/configuration.md#docker-environment)), then:

   ```bash
   docker compose up -d
   ```

2. **Set the user secrets** for each service, including a generated signing key for Identity. See [configuration](docs/configuration.md#user-secrets).

3. **Build, test and run.**

   ```bash
   dotnet build ShopShop.slnx
   dotnet test ShopShop.slnx
   ```

   ```bash
   dotnet run --project src/Services/Identity/Identity.Api --launch-profile https
   ```

   Run Catalog, Ordering, Payment and Notification the same way. Then run `src/Gateway/Gateway.Api` and open https://localhost:7080/swagger for one Swagger UI across Identity, Catalog and Ordering. Ports and Swagger URLs are in [configuration](docs/configuration.md#ports).

4. **Run every consumer once.** Payment, Ordering and Notification each create their own queue on first start. Until then events for them cannot be delivered and block the sender. See [messaging](docs/messaging.md#start-every-consumer-at-least-once).

### Try the payment flow

1. Register or log in, then place an order with `POST /api/orders` and `"paymentMethodId": "pm_card_chargeDeclined"`.
2. The response is `Pending`. A few seconds later, `GET /api/orders/{id}` shows `PaymentFailed` with the reason and the retries left.
3. Retry with `POST /api/orders/{id}/retry-payment` and `{ "paymentMethodId": "pm_card_visa" }`. The order becomes `Confirmed`.
4. Check the PaymentIntents in the Stripe dashboard (test mode) and Notification's log.

Other test payment methods are listed in the [API reference](docs/api.md#payment-test-outcomes).

## Documentation

| Page | Contents |
|---|---|
| [Architecture](docs/architecture.md) | Project structure, diagrams, flows and key concepts |
| [API reference](docs/api.md) | Endpoints, order rules, test payment methods |
| [Configuration](docs/configuration.md) | Docker environment, user secrets, signing key, ports |
| [Messaging](docs/messaging.md) | Exchanges, queues and RabbitMQ gotchas |
| [Gateway](docs/gateway.md) | YARP API gateway: routes, combined Swagger, responsibilities |

## Roadmap

Unchecked items are not implemented yet:

- [x] **Payment**: consumes `order.placed` and `order.payment-retried`, charges through Stripe test mode, and publishes the result
- [x] **Ordering handles payment results**: success sets `Confirmed`, failure sets `PaymentFailed` (retryable) or `Failed`
- [x] **Notification.Api (first iteration)**: consumes order events and logs them
- [x] **RS256 JWT signing** with a public key published by Identity
- [x] **API gateway** (YARP) with a combined Swagger UI, CORS, rate limiting and correlation ids
- [ ] **Pagination** for all `POST …/search` endpoints (page number, page size, total count)
- [ ] **Redis caching** for Catalog product and category reads (cache-aside, invalidated on admin writes)
- [ ] Integration tests with Testcontainers (PostgreSQL, RabbitMQ)
- [ ] Identity refresh tokens and logout
- [ ] Containerising all services

The outboxes assume a single instance of each service. Scaling out would need row locking (`FOR UPDATE SKIP LOCKED`).
