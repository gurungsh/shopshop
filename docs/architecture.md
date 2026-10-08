# ShopShop Architecture

How ShopShop is put together and where to find things in the code. For setup see the [readme](../readme.md), and for details see the [API reference](api.md), [configuration](configuration.md) and [messaging](messaging.md).

## Contents

- [Project structure](#project-structure)
- [Architecture overview](#architecture-overview)
- [Database diagram](#database-diagram)
- [Authentication flow](#authentication-flow)
- [Order flow](#order-flow)
- [Payment flow](#payment-flow)
- [Key concepts](#key-concepts)

## Project structure

```text
ShopShop/
├── src/
│   ├── BuildingBlocks/
│   │   ├── BuildingBlocks.Core         # Result<T>, ResultErrorType, Roles
│   │   ├── BuildingBlocks.Web          # EndpointResults.ToHttpResult
│   │   ├── BuildingBlocks.Contracts    # integration events + routing keys (Orders/, Payments/)
│   │   └── BuildingBlocks.Messaging    # RabbitMQ connection, publisher, consumer base, topology
│   └── Services/
│       ├── Identity/
│       │   ├── Identity.Api
│       │   └── Identity.Infrastructure
│       ├── Catalog/
│       │   ├── Catalog.Api
│       │   └── Catalog.Infrastructure
│       ├── Ordering/
│       │   ├── Ordering.Api
│       │   └── Ordering.Infrastructure
│       ├── Payment/
│       │   ├── Payment.Api
│       │   └── Payment.Infrastructure
│       └── Notification/
│           └── Notification.Api
├── tests/
│   └── Services/
│       ├── Identity/Identity.Api.UnitTests
│       ├── Catalog/Catalog.Api.UnitTests
│       ├── Ordering/Ordering.Api.UnitTests
│       ├── Payment/Payment.Api.UnitTests
│       └── Notification/Notification.Api.UnitTests
├── docs/
├── docker-compose.yml
├── init-dbs.sql
└── ShopShop.slnx
```

Each service has two projects:

| Project | Holds |
|---|---|
| `<Svc>.Api` | Endpoints, DTOs, business services, validators, filters, options, extensions and `Program.cs` |
| `<Svc>.Infrastructure` | The DbContext, entities, Fluent configurations, migrations and constants |

```text
Ordering.Api/                     Ordering.Infrastructure/
├── DTOs/                         ├── Configurations/
├── Endpoints/                    ├── Constants/
├── Extensions/                   ├── Data/
├── Filters/                      ├── Migrations/
├── Messaging/                    └── Models/
├── Options/
├── Services/
├── Validators/
└── Program.cs
```

Differences from that shape:

- **Identity.Api** adds `Security/` (`IJwtKeyProvider`, `JwtKeyProvider`) for the signing key and the public JWKS.
- **Payment.Api** has no `Endpoints/`. It has `Messaging/` (`OutboxPublisher`, `OrderEventsConsumer`) instead.
- **Notification.Api** has no Infrastructure project, because it stores nothing yet. It has `Messaging/` (`OrderEventsConsumer`), `Services/` (`NotificationService`) and `Program.cs`.

## Architecture overview

Two small diagrams: how requests flow, and how events flow.

### Requests (HTTP)

```mermaid
flowchart TB
    client([Client])
    identity["Identity.Api<br/>AuthDb"]
    catalog["Catalog.Api<br/>CatalogDb"]
    ordering["Ordering.Api<br/>OrderDb"]

    client -- "login" --> identity
    client -- "browse products" --> catalog
    client -- "place and track orders" --> ordering
    ordering -- "HTTP + resilience:<br/>names, prices, active state" --> catalog
```

- Clients send a Bearer JWT to Catalog and Ordering. The services check it themselves (see [Authentication flow](#authentication-flow)).
- HTTP is used when the caller needs an answer now. Ordering asks Catalog before it creates an order.
- Each service owns its database. No service reads another one's.

### Events (RabbitMQ)

```mermaid
flowchart LR
    ordering["Ordering.Api<br/>OrderDb + outbox"]
    ordersX{{"RabbitMQ<br/>shopshop.orders"}}
    payment["Payment.Api<br/>PaymentDb + outbox"]
    stripe(["Stripe<br/>test mode"])
    paymentsX{{"RabbitMQ<br/>shopshop.payments"}}
    notification["Notification.Api<br/>logs events for now"]

    ordering -- "all order.* events" --> ordersX
    ordersX -- "order.placed<br/>order.payment-retried" --> payment
    payment -- "charge" --> stripe
    payment -- "payment.succeeded<br/>payment.failed" --> paymentsX
    paymentsX --> ordering
    ordersX -- "order.confirmed<br/>order.payment-failed<br/>order.cancelled" --> notification
```

- Events are used when other services only need to react. Ordering and Payment never call each other.
- Every event leaves a service through its outbox, so it is sent only if the change that caused it was saved.

## Database diagram

Each service owns its own database. Lines are real foreign keys *inside* one database.

```mermaid
erDiagram
    %% ---------- AuthDb (Identity) ----------
    Users {
        uuid Id PK
        varchar Email UK "max 256, normalised lower-case"
        text PasswordHash "ASP.NET PasswordHasher"
        varchar Role "Admin | Customer"
        timestamptz CreatedAtUtc "default now()"
        timestamptz UpdatedAtUtc "default now()"
    }

    %% ---------- CatalogDb (Catalog) ----------
    Categories {
        uuid Id PK
        varchar Name UK "max 100"
        varchar Description "max 500"
        boolean IsActive "default true"
        timestamptz CreatedAtUtc "default now()"
        timestamptz UpdatedAtUtc "default now()"
    }
    Products {
        uuid Id PK
        uuid CategoryId FK "indexed, on delete restrict"
        varchar Name "max 200"
        varchar Description "max 2000"
        varchar Sku UK "max 50"
        numeric Price "18,2"
        boolean IsActive "default true"
        timestamptz CreatedAtUtc "default now()"
        timestamptz UpdatedAtUtc "default now()"
    }
    Categories ||--o{ Products : contains

    %% ---------- OrderDb (Ordering) ----------
    Orders {
        uuid Id PK
        uuid UserId "logical ref to AuthDb.Users"
        numeric TotalAmount "18,2"
        varchar Status "enum stored as string"
        varchar ShippingAddress "max 500"
        int PaymentAttempts "1 for the first attempt"
        varchar PaymentFailureReason "nullable, max 500, shown to the customer"
        timestamptz CreatedAtUtc
        timestamptz UpdatedAtUtc
    }
    OrderItems {
        uuid Id PK
        uuid OrderId FK "on delete cascade"
        uuid ProductId "logical ref to CatalogDb.Products"
        varchar ProductName "snapshot, max 200"
        numeric UnitPrice "snapshot, 18,2"
        int Quantity
    }
    OutboxMessages {
        uuid Id PK "used as RabbitMQ MessageId"
        varchar Type "routing key, max 200"
        jsonb Payload "serialised event"
        timestamptz OccurredAtUtc
        timestamptz ProcessedAtUtc "nullable, indexed"
        int Attempts
        varchar LastError "nullable, max 2000"
    }
    Orders ||--|{ OrderItems : has

    %% ---------- PaymentDb (Payment) ----------
    PaymentTransactions {
        uuid Id PK "sent as PaymentId on payment events"
        uuid OrderId "logical ref to OrderDb.Orders, unique with Attempt"
        int Attempt
        numeric Amount "18,2, USD"
        varchar Status "Succeeded | Failed"
        varchar FailureReason "nullable, max 500, shown to the customer"
        varchar ProviderReference "nullable, Stripe PaymentIntent id"
        timestamptz CreatedAtUtc
        timestamptz UpdatedAtUtc
    }
```

| Database | Tables | Owner |
|---|---|---|
| `AuthDb` | `Users` | Identity |
| `CatalogDb` | `Categories`, `Products` | Catalog |
| `OrderDb` | `Orders`, `OrderItems`, `OutboxMessages` | Ordering |
| `PaymentDb` | `PaymentTransactions`, `OutboxMessages` | Payment |

- `Orders.UserId`, `OrderItems.ProductId` and `PaymentTransactions.OrderId` point into **other databases**, so they are plain ids with no foreign key. The owning service keeps them consistent.
- `OrderItems.ProductName` and `UnitPrice` are **snapshots**: later catalog changes don't rewrite past orders. `TotalPrice` is computed in code and not stored.
- `Orders.Status` values: `Pending`, `Confirmed`, `Processing`, `Shipped`, `Delivered`, `Cancelled`, `Refunded`, `Failed`, `PaymentFailed`.
- `PaymentTransactions` has a unique index on (`OrderId`, `Attempt`), so each attempt is charged at most once.
- `ProcessedAtUtc` is indexed on both `OutboxMessages` tables, because the publishers read pending rows (`ProcessedAtUtc IS NULL`).

## Authentication flow

Tokens are signed with an RSA key pair (RS256). Only Identity holds the private key, and everyone else verifies with the public key.

```mermaid
sequenceDiagram
    participant U as Client
    participant I as Identity.Api
    participant S as Catalog / Ordering

    Note over I: Holds the private key (user secret)
    U->>I: POST /api/auth/login
    I->>I: Sign JWT with private key (header has kid)
    I-->>U: JWT

    U->>S: Request + Bearer JWT
    opt First token check, or the key is not cached yet
        S->>I: GET /.well-known/openid-configuration
        I-->>S: issuer + jwks_uri
        S->>I: GET /.well-known/jwks.json
        I-->>S: Public key (kty, kid, n, e)
    end
    S->>S: Verify signature, issuer, audience, expiry
    S-->>U: Response (401 if invalid, 403 if the role is wrong)
```

- **Private key:** in Identity's user secrets. `IJwtKeyProvider` loads it once and exposes the signing key and the public JWKS.
- **Public key:** Catalog and Ordering only configure `MetadataAddress`. JwtBearer fetches the key on the first token check and caches it.
- **Identity down:** other services still start. Token checks return 401 until Identity is reachable again, then recover on their own.
- **`kid`:** every token names its signing key, which leaves room for key rotation.

## Order flow

How an order is saved and its event published.

```mermaid
sequenceDiagram
    autonumber
    actor C as Customer
    participant O as Ordering.Api
    participant Cat as Catalog.Api
    participant DB as OrderDb
    participant P as OutboxPublisher
    participant R as RabbitMQ

    C->>O: POST /api/orders
    O->>Cat: POST /api/products/search
    Cat-->>O: names, prices, active state
    O->>DB: one transaction: insert Order + OrderItems + OutboxMessage
    O-->>C: 201 Created
    loop every 2s (Outbox:PollingInterval)
        P->>DB: read unprocessed rows (oldest first)
        P->>R: publish to shopshop.orders / order.placed (mandatory, confirms)
        alt confirmed
            R-->>P: ack
            P->>DB: set ProcessedAtUtc
        else failed (broker down, unroutable, auth)
            P->>DB: Attempts++, LastError, retry next poll
        end
    end
```

- The order and its event are saved in **one transaction**, so an event is never lost and never sent for an order that wasn't saved.
- Delivery is **at least once**, so consumers must be idempotent by `MessageId` (the outbox row id).
- Prices always come from Catalog, never from the client.

## Payment flow

What happens after `order.placed` is published. Payment never calls Ordering, and Ordering never calls Payment.

```mermaid
sequenceDiagram
    autonumber
    actor C as Customer
    participant O as Ordering
    participant OX as shopshop.orders
    participant P as Payment
    participant S as Stripe
    participant PX as shopshop.payments
    participant N as Notification

    Note over O: order saved as Pending (see Order flow)
    O->>OX: order.placed (attempt 1)
    OX->>P: via queue payment.order-events
    Note over P: skip if this order and attempt was already charged
    P->>S: create and confirm a PaymentIntent in USD
    S-->>P: succeeded, or card declined
    P->>PX: payment.succeeded or payment.failed
    PX->>O: via queue ordering.payment-events
    Note over O: ignore if the attempt is stale or the order is no longer Pending
    alt payment succeeded
        O->>O: Pending to Confirmed
        O->>OX: order.confirmed
        OX-->>N: order.confirmed
    else payment failed
        O->>O: Pending to PaymentFailed (Failed if no retries are left)
        O->>OX: order.payment-failed
        OX-->>N: order.payment-failed
    end
```

### Retrying a failed payment

```mermaid
sequenceDiagram
    actor C as Customer
    participant O as Ordering
    participant OX as shopshop.orders
    participant P as Payment

    C->>O: POST /api/orders/{id}/retry-payment (new paymentMethodId)
    O->>O: PaymentFailed to Pending, attempt + 1
    O->>OX: order.payment-retried (next attempt)
    OX->>P: via queue payment.order-events
    Note over P: charges the new attempt the same way as the first
```

### Order status while waiting for payment

```mermaid
stateDiagram-v2
    [*] --> Pending: order placed (attempt 1)
    Pending --> Confirmed: payment.succeeded
    Pending --> PaymentFailed: payment.failed, retries left
    Pending --> Failed: payment.failed, no retries left
    PaymentFailed --> Pending: customer retries with a new card
    Pending --> Cancelled: cancelled
    PaymentFailed --> Cancelled: cancelled
    Confirmed --> Cancelled: cancelled
    Failed --> [*]
```

- **Retries:** `Payment:MaxRetries` in Ordering's config (default 3) sets how many retries follow the first attempt. The attempt number travels on every event.
- **Failure reasons:** Stripe's card decline messages are shown to the customer as they are. Provider errors get a generic message, and the details are only logged.
- **Stale results:** a result for an older attempt, a redelivered result, or a result for an order cancelled while paying changes nothing. Refunds are not implemented.
- **Notification:** reacts to Ordering's `order.confirmed`, `order.payment-failed` and `order.cancelled` rather than Payment's events, so it only announces what Ordering actually did. It must have started once so its queue exists (see [messaging](messaging.md#start-every-consumer-at-least-once)).

## Key concepts

Where each idea lives in the code.

### Data and structure

| Concept | How it's done here | Where to look |
|---|---|---|
| Database per service | Each service has its own PostgreSQL database and DbContext. | `*.Infrastructure/Data/` |
| Pragmatic layering | Two projects per service instead of full Clean Architecture. No repositories, MediatR or mappers. | `src/Services/<Svc>/` |
| EF Core mapping | Fluent `IEntityTypeConfiguration<T>` classes, explicit table names, `numeric(18,2)` money, enums as strings. Migrations apply on startup in Development. | `*.Infrastructure/Configurations/` |
| Data snapshots | Order items copy the product name and price at order time. | `OrderItem.ProductName` / `UnitPrice` |

### API and code style

| Concept | How it's done here | Where to look |
|---|---|---|
| Minimal APIs | Endpoints are grouped with `MapGroup` and named with `.WithName`. Admin routes live in separate `Admin*Endpoints` classes. | `*.Api/Endpoints/` |
| Search as `POST` | List endpoints are `POST …/search` with a `<Entity>Query` body. A bare `POST` is always a create. | `ProductEndpoints`, `OrderEndpoints` |
| Result pattern | Business failures return `Result<T>` with a `ResultErrorType`. `EndpointResults.ToHttpResult` maps them to status codes. | `BuildingBlocks.Core`, `BuildingBlocks.Web` |
| Validation | FluentValidation validators run through a generic `ValidationFilter<T>`. | `*.Api/Validators/`, `*.Api/Filters/` |
| Error handling | `UseExceptionHandler` logs unexpected exceptions and returns a generic 500. | each `Program.cs` |
| Logging | Serilog message templates to the console and CLEF files, plus request logging. | each `Program.cs` |
| Fail-fast configuration | Options and connection strings are checked at startup. Secrets live in user secrets. | `*.Api/Options/` |

### Security

| Concept | How it's done here | Where to look |
|---|---|---|
| Authentication | RS256 JWTs signed by Identity and verified with its public key. See [Authentication flow](#authentication-flow). | `Identity.Api/Security/`, `JwksEndpoints.cs` |
| Authorization | `Admin` and `Customer` roles come from one shared `Roles` class. | `BuildingBlocks.Core/Roles.cs` |
| Card data | Clients send only a Stripe payment method id (`pm_...`). Card numbers never reach our services. | `PaymentMethodIdRules`, `StripePaymentService` |

### Service communication

| Concept | How it's done here | Where to look |
|---|---|---|
| Resilient HTTP | Ordering calls Catalog through a typed `HttpClient` with `AddStandardResilienceHandler()`. Transport failures become 503. | `CatalogServiceClient.cs` |
| Transactional outbox | Events are saved as `OutboxMessage` rows in the same `SaveChangesAsync` as the change, then published by a background service. | `OrderingService.AddOutboxMessage`, `OutboxPublisher.cs` |
| Reliable publishing | Publisher confirms and `mandatory: true`. Failed rows keep `Attempts` and `LastError` and retry on the next poll. | `RabbitMqPublisher.cs` |
| Topology | Two topic exchanges, one durable queue per consumer, dead-lettering to `<queue>.dead`. See [messaging](messaging.md). | `MessagingTopology.cs`, `RoutingKeys.cs` |
| Choreography | No central coordinator. Each service reacts to events and publishes its own. | `*.Api/Messaging/*Consumer.cs` |
| Idempotent consumers | One `PaymentTransaction` per order and attempt, a Stripe idempotency key per attempt, and Ordering ignores stale results. | `PaymentProcessingService`, `OrderingService.IsCurrentPendingAttempt` |
| Event contracts | Versionable `sealed record`s in a shared library, serialised as camelCase JSON. | `BuildingBlocks.Contracts/` |

### Testing

| Concept | How it's done here | Where to look |
|---|---|---|
| Unit tests | xUnit, Moq and the EF Core InMemory provider. Background work is a public method (`PublishPendingAsync`) so it needs no timers. | `tests/Services/` |
