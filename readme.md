# ShopShop E-Commerce Microservices

A simple e-commerce application built with **.NET 10 Minimal APIs** to practice microservice architecture and enterprise application development patterns.

## Architecture

The application consists of three independently owned services:

```text
                         ┌──────────────┐
                         |    Client    │
                         └──────┬───────┘
                                │
                   ┌────────────┼────────────┐
                   │            │            │
                   ▼            ▼            ▼
              Identity       Catalog      Ordering
                 API           API           API
                   │            │            │
                   ▼            ▼            ▼
              Identity DB   Catalog DB   Ordering DB
                PostgreSQL   PostgreSQL   PostgreSQL
                                             │
                                             │ Events
                                             ▼
                                         RabbitMQ
```

Each service owns its **business logic, data, and database**.

Services communicate through HTTP APIs.

## Services

### Identity Service

**Responsibility:** User identity and access.

Owns:

* User registration
* Authentication
* JWT/refresh tokens
* User profiles
* Roles
* Authorization

```text
POST   /api/auth/register
POST   /api/auth/login
POST   /api/auth/refresh
POST   /api/auth/logout

GET    /api/users/me
PUT    /api/users/me

GET    /api/admin/users
GET    /api/admin/users/{email}
POST   /api/admin/users
PUT    /api/admin/users/{id}
DELETE /api/admin/users/{id}
```

### Catalog Service

**Responsibility:** What can be purchased.

Owns:

* Products
* Services
* Categories
* Pricing
* Product availability

### Ordering Service

**Responsibility:** What customers have purchased.

Owns:

* Orders
* Order items
* Order status
* Order history
* Order cancellation

## Service Communication

### Synchronous

Services use HTTP when an immediate response is required.

```text
Ordering API
     │
     │ HTTP
     ▼
Catalog API
```

Services never access another service's database directly.

## Project Structure

Each service starts with two projects rather than forcing a full Clean Architecture structure.

```text
ECommerce/
├── Identity/
│   ├── Identity.Api
│   └── Identity.Infrastructure
│
├── Catalog/
│   ├── Catalog.Api
│   └── Catalog.Infrastructure
│
├── Ordering/
│   ├── Ordering.Api
│   └── Ordering.Infrastructure
│
├── tests/
│   ├── Identity.Tests
│   ├── Catalog.Tests
│   └── Ordering.Tests
│
└── docker-compose.yml
```

### API Projects

Responsible for:

* Minimal API endpoints
* Request/response DTOs
* Application/service logic
* FluentValidation
* Authentication/authorization
* API configuration

Example:

```text
Identity.Api/
├── Endpoints/
├── DTOs/
├── Services/
├── Validators/
├── Extensions/
└── Program.cs
```

### Infrastructure Projects

Responsible for:

* Entity Framework Core
* PostgreSQL
* DbContext
* EF Core configurations
* Migrations
* Repositories
* External service implementations

Example:

```text
Identity.Infrastructure/
├── Data/
├── Models/
├── Migrations/
```

## Technology Stack

### Application

* .NET 10
* Minimal APIs
* Entity Framework Core
* PostgreSQL
* FluentValidation
* Serilog
* OpenAPI
* Scalar
* JWT Authentication
* ASP.NET Core Authorization
* ProblemDetails
* Health Checks

### Messaging & Communication

* RabbitMQ
* HttpClientFactory
* HTTP resilience
* Asynchronous events

### Testing

* xUnit
* Moq
* AutoFixture
* WebApplicationFactory
* Testcontainers
* PostgreSQL integration tests

### Infrastructure & Observability

* Docker
* Docker Compose
* OpenTelemetry

## Design Principles

The project focuses on:

* Clear microservice boundaries
* Database-per-service
* Separation of concerns
* Dependency injection
* DTOs and API contracts
* Validation
* Centralized error handling
* Structured logging
* Authentication and authorization
* Unit and integration testing
* Resilient service-to-service communication
* Asynchronous messaging
* Health checks
* Distributed tracing
* Containerization

The project starts with **Identity, Catalog, and Ordering** and will evolve incrementally as new requirements are introduced.
