# Loyalty Platform — Backend API

A production-grade QR code loyalty platform built with **.NET 8 Web API**, **Clean Architecture**, and **SQL Server**. Handles concurrent coupon redemption with ACID guarantees, idempotency, and automated reconciliation of partial failures.

## Tech Stack

- **.NET 8** Web API
- **Clean Architecture** (Domain / Application / Infrastructure / API)
- **Entity Framework Core 8** + SQL Server
- **JWT Authentication** (BCrypt password hashing)
- **Swagger UI** (with Bearer auth support)

## Quick Start

### Prerequisites
- .NET 8 SDK
- SQL Server LocalDB (included with Visual Studio) or SQL Server Express

### Run

```bash
cd LoyaltyPlatform.API
dotnet run
```

Swagger UI opens at **https://localhost:7001** (or check console for exact port).

### Default Admin Credentials
```
Email:    admin@loyalty.com
Password: Admin@123!
```

## API Endpoints

### Auth (no auth required)
| Method | URL | Description |
|---|---|---|
| POST | `/api/auth/register` | Register a new user |
| POST | `/api/auth/login` | Login, returns JWT |

### User (requires JWT)
| Method | URL | Description |
|---|---|---|
| GET | `/api/wallet/balance` | View wallet balance |
| POST | `/api/coupons/redeem` | Redeem a coupon by code |

### Admin (requires Admin JWT)
| Method | URL | Description |
|---|---|---|
| POST | `/api/admin/campaigns` | Create a campaign |
| POST | `/api/admin/campaigns/{id}/coupons?count=10` | Generate N coupons |
| GET | `/api/admin/reconcile/preview` | Preview partial failures (dry-run) |
| POST | `/api/admin/reconcile` | Fix partial failures |

### Test / Debug only (`#if DEBUG`)
| Method | URL | Description |
|---|---|---|
| POST | `/api/test/simulate-partial-failure` | Simulate a partial failure for demo |

## End-to-End Demo Flow

```
1. POST /api/auth/login  →  get admin JWT
2. POST /api/admin/campaigns  →  create campaign (rewardValue: 50)
3. POST /api/admin/campaigns/{id}/coupons?count=5  →  get coupon codes
4. POST /api/auth/register  →  create a user, get user JWT
5. POST /api/coupons/redeem  →  redeem coupon, wallet += 50
6. GET  /api/wallet/balance  →  verify balance = 50
7. POST /api/coupons/redeem (same code)  →  409 Conflict (idempotent)

Partial Failure Demo:
8.  POST /api/test/simulate-partial-failure  →  wallet credited, coupon stays Active
9.  GET  /api/admin/reconcile/preview  →  see the inconsistency
10. POST /api/admin/reconcile  →  fix it
11. GET  /api/admin/reconcile/preview  →  empty (all fixed)
```

## Key Design Decisions

See [DECISION_LOG.md](./DECISION_LOG.md) for detailed rationale.
See [FAILURE_STRATEGY.md](./FAILURE_STRATEGY.md) for failure scenario analysis.

### Concurrency: UPDLOCK + Serializable Isolation
```sql
SELECT * FROM Coupons WITH (UPDLOCK, ROWLOCK) WHERE Code = @code
```
Ensures only one concurrent request can claim a coupon. All others get serialized and see the updated status.

### Idempotency: Compound Key in DB
Every redemption is keyed by `userId:couponCode` with a UNIQUE constraint on Transactions. Duplicate requests return the previously computed result without re-processing.

### Partial Failure Pattern
Transactions start as `Status = PartialFailure`. Only the last operation before COMMIT marks them `Completed`. Any crash between steps leaves a detectable incomplete record for the reconciler.

## Production Checklist

- [ ] Replace `(localdb)\MSSQLLocalDB` with production SQL Server
- [ ] Move JWT key to Azure Key Vault / environment variable
- [ ] Set up HTTPS certificates
- [ ] Configure connection pool size
- [ ] Add health check endpoint (`/health`)
- [ ] Add structured logging (Serilog)
- [ ] Remove `#if DEBUG` test endpoints from production builds
