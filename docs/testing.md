# Testing

```
unit tests ──► Moq + EF Core InMemory              (fast, no Docker)
integration ─► WebApplicationFactory ──► real service
                                          ├─► PostgreSQL 16 container  (migrations applied)
                                          ├─► RabbitMQ 4 container     (Ordering, Payment, Notification)
                                          └─► Redis 7 container        (Catalog)
```

| Service | Integration tests | Containers | Faked |
|---------|-------------------|------------|-------|
| Identity | `Identity.Api.IntegrationTests` | PostgreSQL | nothing (real JWTs, generated RSA key) |
| Catalog | `Catalog.Api.IntegrationTests` | PostgreSQL, Redis | JWT validation |
| Ordering | `Ordering.Api.IntegrationTests` | PostgreSQL, RabbitMQ | JWT validation, Catalog client |
| Payment | `Payment.Api.IntegrationTests` | PostgreSQL, RabbitMQ | Stripe |
| Notification | `Notification.Api.IntegrationTests` | RabbitMQ | notification service (records instead of logging) |
| Gateway | none | none | it has no database or broker; unit tests only |

All integration tests need Docker. Unit tests (`tests/**/*.UnitTests`) do not.

## How it works

- Testcontainers starts throwaway containers on random ports, so nothing clashes with `docker compose`. They are removed when the tests finish.
- One `<Svc>ApiFactory` per project is shared by all its tests (xUnit collection), so the containers start once per run.
- Each app runs in the Development environment, so it applies its own EF migrations on startup.
- JWT validation (Catalog, Ordering) is replaced by `TestAuthHandler`: send `X-Test-Role: Admin` (Ordering also needs `X-Test-User-Id`) to be authenticated; no header means anonymous.
- Tests use unique data and never assume an empty database.
- External systems stay faked: Stripe (`FakePaymentService`: `pm_card_visa` succeeds, anything else is declined) and Catalog for Ordering (`FakeCatalogServiceClient`).

## Messaging tests

```
test ──publish──► exchange ──► real consumer ──► DB / fake service
                                                     │ outbox
test ◄──listen── private queue ◄── exchange ◄────────┘
```

- `RabbitMqTestBus` (one copy in each project that needs it) publishes events like another service would, and records what the service publishes.
- The bus binds its queue **before** the service publishes. Services publish with `mandatory: true`, so an event with no bound queue is returned and retried.
- A consumer declares its queue a moment after startup, and an event published earlier is dropped. Tests re-publish until the effect is visible; this is safe because handlers are idempotent.
- Poison messages are checked in the `<queue>.dead` queue.
- Outbox polling is set to 200 ms in the tests (`Outbox:PollingInterval`).
- Outbox `Payload` is a `jsonb` column and cannot be filtered with `Contains` in SQL; the tests filter in memory.

## Run

```bash
dotnet test tests/Services/Ordering/Ordering.Api.IntegrationTests
```

`dotnet test ShopShop.slnx` runs everything, so Docker must be running.
