# API reference

The endpoints each service exposes. When `OpenApi:Enabled` is `true` (always in Development, and in `docker-compose.yml`) every public API also serves Swagger at `/swagger`; see [configuration](configuration.md#ports) for the ports.

Every API also exposes `GET /health`, which checks that the service can reach its dependencies: PostgreSQL (Identity, Catalog, Ordering, Payment), RabbitMQ (Ordering, Payment, Notification) and Redis (Catalog). It returns `{ Status, Service, Checks }` with one entry per dependency, and `503` when any check fails. The gateway has no dependencies to check, so its `/health` only says it is up. Payment and Notification have no other endpoints.

## Contents

- [Identity](#identity)
- [Catalog](#catalog)
- [Ordering](#ordering)
- [Payment test outcomes](#payment-test-outcomes)

## Identity

Users, registration, login, JWT issuing and admin user management.

```text
POST   /api/auth/register
POST   /api/auth/login
POST   /api/auth/refresh
POST   /api/auth/logout

GET    /api/users/me
PUT    /api/users/me

POST   /api/admin/users/search
GET    /api/admin/users/{id}
POST   /api/admin/users
PUT    /api/admin/users/{id}
DELETE /api/admin/users/{id}

GET    /.well-known/jwks.json                (public key, anonymous)
GET    /.well-known/openid-configuration     (discovery document, anonymous)
```

Identity rules:

| Topic | Rule |
|---|---|
| Email | Trimmed and stored in lowercase, so `Bob@X.com` and `bob@x.com` are the same user. The admin `email` search filter is an exact match after the same normalization. |
| Duplicates | Registering, creating a user, or changing an email to one that is already taken gets 409, including when two requests race. |
| Refresh tokens | Register, login and refresh return a JWT and a refresh token. A refresh token is valid for `JwtSettings:RefreshTokenExpirationInDays` (7 by default) and works once: `POST /api/auth/refresh` revokes it and returns a new pair. Using a revoked refresh token again revokes every refresh token of that user. Only a SHA-256 hash of each token is stored. |
| Logout | Revokes every refresh token of the caller, so it signs out all of their sessions. The JWT stays valid until it expires (`JwtSettings:ExpirationInMinutes`), because services check JWTs without calling Identity. |

## Catalog

What can be purchased: products and categories.

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

Catalog rules:

| Topic | Rule |
|---|---|
| Text search | `name` and `description` filters match any part of the text and ignore case. `%` and `_` are matched literally. |
| SKU | Trimmed and stored in uppercase. The `sku` filter is an exact match after the same normalization, so `abc-1` finds `ABC-1`. |
| Unique values | Category names (ignoring case) and SKUs must be unique. A duplicate gets 409, including when two requests race. |
| Categories | Creating a product in a category that does not exist gets 400. |
| Price range | `maxPrice` below `minPrice` gets 400. |
| Updates | A field left out (`null`) is unchanged. An empty `description` clears it. Sending a product's own SKU back is allowed. |

## Ordering

What customers have purchased: orders, order items and status. It publishes order events and applies payment results.

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

| Topic | Rule |
|---|---|
| Products | Missing or inactive products are skipped and returned as `SkippedProductIds`. If none is valid, the request fails with 400. |
| Payment method | `paymentMethodId` is required and must be a Stripe payment method id (`pm_...`). Card details never reach our services. |
| Status after placing | `Pending` until Payment answers. Success gives `Confirmed`. Failure gives `PaymentFailed`, with `paymentFailureReason` and `paymentRetriesRemaining` on the order. |
| Retries | A `PaymentFailed` order can be retried with `POST /api/orders/{id}/retry-payment` and a new `paymentMethodId`, up to `Payment:MaxRetries` times (default 3, so 4 attempts in total). If the last retry fails, the order becomes `Failed`. |
| Cancelling | Customers can cancel only `Pending`, `Confirmed`, `Processing` or `PaymentFailed` orders; otherwise they get 409. Admins can set any status. |
| Cancel event | `order.cancelled` is sent only when an order actually changes to `Cancelled`. |
| Search ranges | A search with an inverted range (`maxTotalAmount` below `minTotalAmount`, `maxItemCount` below `minItemCount`, or an "after" date later than its "before" date) gets 400. |
| Stale results | A payment result for an older attempt, or for an order that is no longer `Pending`, is ignored. |

See the [payment flow](architecture.md#payment-flow) for the diagram.

## Payment test outcomes

With a Stripe test-mode key, the `paymentMethodId` decides the outcome:

| `paymentMethodId` | Outcome |
|---|---|
| `pm_card_visa` | Succeeds |
| `pm_card_chargeDeclined` | Fails: "Your card was declined." |
| `pm_card_chargeDeclinedInsufficientFunds` | Fails: "Your card has insufficient funds." |

Stripe's card decline messages are shown to the customer as they are. Anything else (Stripe unavailable, a rejected request) shows a generic "We couldn't process your payment. Please try again." and the real error is only logged.
