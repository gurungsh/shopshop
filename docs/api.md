# API reference

The endpoints each service exposes. In Development every public API also serves Swagger at `/swagger`; see [configuration](configuration.md#ports) for the ports.

Every API also exposes `GET /health`, which returns `{ Status, Service }`. Payment and Notification have no other endpoints.

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
POST   /api/auth/refresh          (not implemented yet)
POST   /api/auth/logout           (not implemented yet)

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
