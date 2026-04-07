# Decision Log — Loyalty Platform Backend

**Author:** Backend Engineering  
**Date:** April 2026  
**Assignment:** QR-Code Loyalty Redemption Platform — Production-Grade Backend

---

## 1. Architecture Choice: Clean Architecture

**Decision:** Split the solution into four layers — Domain, Application, Infrastructure, API.

**Rationale:**  
Clean Architecture enforces a strict dependency rule: outer layers depend on inner layers, never the reverse. This means:
- Business rules (`Domain`) have zero dependencies on SQL Server, HTTP, or JWT
- Use case contracts (`Application`) are interface-based — swapping SQL Server for PostgreSQL requires only Infrastructure changes
- The API layer is a thin shell over application services

**Trade-off:** More boilerplate vs. a simple single-project API. Given the assignment requires ACID transactions, reconciliation logic, and concurrent access handling, the additional structure pays for itself in testability and maintainability.

---

## 2. Concurrency Strategy: Database-Level Row Locking

**Decision:** Use `SELECT ... WITH (UPDLOCK, ROWLOCK)` inside a Serializable transaction rather than application-level locking (e.g., ConcurrentDictionary or SemaphoreSlim).

**Rationale:**  
Application-level locks fail the moment you deploy two API instances (load balancer scenario). Database-level UPDLOCK is the correct primitive because:
- It serializes access to a single row across all concurrent connections
- SQL Server's lock manager handles escalation, timeouts, and deadlock detection
- ROWLOCK hint keeps locking granularity minimal (row, not page or table)

**Serializable isolation** is used specifically because it prevents phantom reads — we need guarantees that the coupon status we read is the latest committed version and cannot be modified between our read and our write by another transaction.

**Trade-off:** Higher lock contention under extreme traffic on the same coupon. Acceptable because a single coupon can only ever be redeemed once — once it's marked Redeemed, all future lock acquisitions will immediately see the terminal state.

**What could still break:**  
If the SQL Server lock wait timeout is too low, concurrent requests may receive a timeout error rather than a clean 409 Conflict. Mitigation: configure `SET LOCK_TIMEOUT` and surface a 503 with `Retry-After` header.

---

## 3. Idempotency Strategy: Server-Side Compound Key

**Decision:** Generate the idempotency key server-side as `"{userId}:{couponCode}"` stored in the Transactions table with a UNIQUE constraint.

**Alternatives considered:**
- **Client-supplied `X-Idempotency-Key` header:** Requires client discipline; mobile apps often don't implement this correctly. Also requires expiry logic.
- **Redis idempotency store:** Adds operational complexity and an availability dependency. Overkill for this scale.

**Why compound key:** A coupon can only be redeemed by one user. The key `userId:couponCode` is globally unique for any valid redemption — no two successful redemptions can share this key. If the same user retries the same coupon, the existing completed transaction is returned as a cached response.

**Trade-off:** If an admin wants to allow a coupon to be re-activated and re-redeemed (e.g., after a refund), the idempotency key would need to be cleared. This is not supported in the current design.

---

## 4. Partial Failure Detection: Write-Ahead Status Pattern

**Decision:** Write the Transaction record with `Status = PartialFailure` at the beginning of the DB transaction, only updating it to `Completed` as the last step before COMMIT.

**Rationale:**  
This is inspired by write-ahead logging (WAL) principles. The idea:
1. Create a record indicating "we are attempting this operation"
2. Perform the actual state changes (credit wallet, mark coupon)
3. Mark the record as "successfully completed"

If the process crashes at any point between steps 1 and 3, the `PartialFailure` record persists and is detectable. The reconciler can then inspect the actual system state and apply the correct fix.

**Critical property:** The PartialFailure status is written inside the same ACID transaction as the wallet credit and coupon update. So after COMMIT, either:
- All three changes are persisted (wallet credited, coupon marked Redeemed, transaction Completed) — happy path
- None are persisted (ROLLBACK) — clean state

**What the reconciler detects:**  
`PartialFailure` transactions where the associated Coupon is still `Active`. This indicates the crash occurred after wallet credit but before coupon status update.

---

## 5. Wallet Balance Constraint

**Decision:** Enforce `Balance >= 0` at the database level via a `CHECK` constraint in addition to application-level validation.

**Rationale:** Defense in depth. If a future direct SQL operation, migration script error, or a bug bypasses the application layer, the database will reject the negative balance write. This prevents silent data corruption.

---

## 6. Coupon Code Generation

**Decision:** Generate codes as URL-safe Base64-encoded GUIDs (22 characters).

**Rationale:**
- 128-bit GUID = 2^128 collision space = effectively collision-free
- URL-safe Base64 encoding makes them safe for QR code payloads and URL paths
- Non-sequential, non-guessable (no enumeration attacks)

**Trade-off:** Codes are not human-memorable. Promotional campaigns may want human-readable codes — these would be admin-specified, not auto-generated.

---

## 7. What Could Still Break in Production

| Scenario | Likelihood | Mitigation |
|---|---|---|
| DB connection pool exhaustion under spike | Medium | Connection pooling config, circuit breaker |
| Reconciler runs concurrently with live traffic | Low | Reconciler uses Serializable transactions; idempotent |
| Lock timeout under extreme concurrent load | Low-Medium | Configure LOCK_TIMEOUT, return 503 + Retry-After |
| JWT key exposure in appsettings.json | High if not configured | Use Azure Key Vault / environment secrets in production |
| LocalDB not suitable for production | Yes | Replace with full SQL Server instance; connection string only change |
