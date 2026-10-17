# Mock data

ShopShop ships with mock data for an outdoor and recreation shop: an admin, five customers, a catalog of camping, hiking, climbing, paddling and cycling gear, and a history of orders with their payments. It is for local use and demos only.

## Contents

- [How it is loaded](#how-it-is-loaded)
- [Accounts](#accounts)
- [Catalog](#catalog)
- [Orders and payments](#orders-and-payments)
- [Reset or change the data](#reset-or-change-the-data)

## How it is loaded

The data lives in one SQL file per database, named after the database:

| File | Contents |
|---|---|
| `scripts/seed/AuthDb.sql` | 1 admin, 5 customers |
| `scripts/seed/CatalogDb.sql` | 8 categories, 39 products (1 inactive) |
| `scripts/seed/OrderDb.sql` | 16 orders, 37 order items, plus a check that each order total equals its items |
| `scripts/seed/PaymentDb.sql` | 17 payment attempts for those orders |

- **With `docker compose up`** a one-shot `seed` container runs the four files with `psql` after the `*-migrate` containers finish and before Identity, Catalog, Ordering and Payment start. See [containers](containers.md#startup-order).
- **With `dotnet run`**, start the apps once so that they apply their migrations, then load the data: `docker compose run --rm --no-deps seed`.
- **Every run is safe.** Rows have fixed ids and are inserted with `ON CONFLICT DO NOTHING`, so a rerun adds what is missing and never changes or deletes existing rows, including ones you created through the API.
- It is not `init-dbs.sql`. Postgres runs that file only when the volume is first created, before any tables exist, so it only creates the four databases.

## Accounts

| Email | Password | Role |
|---|---|---|
| `admin@example.com` | `Admin123!` | Admin |
| `emily.chen@example.com` | `Customer123!` | Customer |
| `marcus.johnson@example.com` | `Customer123!` | Customer |
| `sofia.garcia@example.com` | `Customer123!` | Customer |
| `liam.oconnor@example.com` | `Customer123!` | Customer |
| `priya.patel@example.com` | `Customer123!` | Customer |

The password hashes were generated with ASP.NET Core's `PasswordHasher`, the same hasher Identity uses on register, so these accounts log in like any other.

## Catalog

| Category | Products |
|---|---|
| Camping & Tents | 2- and 6-person tents, a down sleeping bag, a sleeping pad, a stove, a lantern |
| Backpacks & Bags | 65 L trekking pack, 28 L daypack, hydration pack, dry bag, and a discontinued canvas rucksack (`IsActive = false`) |
| Footwear | Hiking boot, trail runner, river sandal, merino socks |
| Outdoor Clothing | Rain jacket, down jacket, fleece, hiking pants, base layer, sun hat |
| Climbing | Harness, helmet, rope, carabiner set, chalk bag |
| Water Sports | Touring kayak, inflatable paddleboard, paddle, life vest |
| Cycling | Mountain bike, bike helmet, multi-tool, light set |
| Hiking Accessories | Trekking poles, headlamp, water filter bottle, first aid kit, compass |

Prices run from $24.95 to $1,249.00. SKUs follow `<AREA>-<ITEM>`, for example `CMP-TENT-2P`.

## Orders and payments

Orders were placed between July and October 2026, three or four per customer. Item names and prices are snapshots of the catalog rows.

| Status | Orders | Payments |
|---|---|---|
| Delivered | 6 | One succeeded attempt each, except one order whose first attempt was declined and second succeeded |
| Shipped | 2 | Succeeded |
| Processing | 2 | Succeeded |
| Confirmed | 2 | Succeeded |
| PaymentFailed | 1 | One failed attempt (insufficient funds), all 3 retries left |
| Cancelled | 1 | One failed attempt (card declined), cancelled afterward |
| Refunded | 1 | Succeeded |

- There are no `Pending` orders. A Pending order waits for Payment to answer an `order.placed` message, and a seeded order would never get one.
- Payment provider references (`pi_seed_...`) are fake and don't exist in Stripe.
- No outbox messages are seeded, so loading the data publishes no events and sends no notifications.

## Reset or change the data

- To start over, delete the volume: `docker compose down -v`, then `docker compose up -d --build`. This also deletes any data you created yourself.
- To change the data, edit the SQL files. Keep the fixed id patterns (`0a…` users, `0c…` categories, `0d…` products, `0e…` orders, `0f…` order items, `09…` payments). Because existing rows are never updated, an edit to a row that is already loaded takes effect only after a reset.
- Catalog caches search results in Redis for up to 2 minutes (see [caching](caching.md)). If you load data while Catalog is running, flush the cache (`docker compose exec redis redis-cli FLUSHALL`) or wait for it to expire.
