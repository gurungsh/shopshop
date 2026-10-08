# Messaging

How the services talk through RabbitMQ. For the diagrams, see the [order flow](architecture.md#order-flow) and [payment flow](architecture.md#payment-flow).

## Contents

- [Topology](#topology)
- [Start every consumer at least once](#start-every-consumer-at-least-once)
- [Inspecting messages](#inspecting-messages)

## Topology

| Exchange | Routing keys | Published by |
|---|---|---|
| `shopshop.orders` | `order.placed`, `order.payment-retried`, `order.confirmed`, `order.payment-failed`, `order.cancelled` | Ordering |
| `shopshop.payments` | `payment.succeeded`, `payment.failed` | Payment |

| Queue | Created by | Receives |
|---|---|---|
| `payment.order-events` | Payment | `order.placed`, `order.payment-retried` |
| `ordering.payment-events` | Ordering | `payment.succeeded`, `payment.failed` |
| `notification.order-events` | Notification | `order.confirmed`, `order.payment-failed`, `order.cancelled` |

- One durable queue per consumer, named `<service>.<topic>`.
- Each queue dead-letters to `<queue>.dead` through the direct exchange `shopshop.dlx`.
- Services never publish directly. They write an outbox row in the same transaction as the change, and a background publisher sends it.
- Delivery is at least once, so consumers must be idempotent.

## Start every consumer at least once

Each consumer creates its own durable queue and bindings on startup. Until a service has run once, events meant for it are **unroutable**.

- With `mandatory: true`, an unroutable publish fails and the row stays in the sender's outbox.
- The publisher keeps messages in order, so that one stuck row **blocks every later event** from that sender.
- After the first start the queues persist, and messages simply wait if a service is down.

## Inspecting messages

To look at messages without consuming them:

1. In the RabbitMQ UI (http://localhost:15672), create a durable queue such as `debug.order-events`.
2. Bind it to `shopshop.orders` with routing key `order.*`.
3. Use **Get messages** on that queue.

Nothing reads a debug queue, so purge or delete it when you are done.
