# Failure Strategy — Loyalty Platform Backend

## Overview

This document details how the system behaves under various failure scenarios and what guarantees are provided.

---

## Failure Scenario 1: Network Timeout During Redemption

**Scenario:** Client sends `POST /api/coupons/redeem`. The server successfully processes the request (wallet credited, coupon marked Redeemed, transaction marked Completed), but the response is lost due to a network timeout. The client retries.

**System Behavior:**
- On retry, the server checks the Transactions table for the idempotency key `userId:couponCode`
- A `Completed` transaction is found → returns the cached success response (200 OK)
- Wallet is NOT double-credited
- Coupon status remains Redeemed

**Guarantee:** Exactly-once wallet credit per coupon per user.

---

## Failure Scenario 2: Server Crash Mid-Transaction (Partial Failure)

**Scenario:** The server processes redemption as follows:
1. ✅ PartialFailure transaction record written
2. ✅ Wallet credited
3. ❌ **Server crashes** — coupon status never updated to Redeemed

**System Behavior:**
- SQL Server automatically rolls back any incomplete transaction (crash recovery)
- However, since all steps were committed before the crash, the PartialFailure record persists with wallet credited and coupon still Active
- The reconciler detects `PartialFailure` + `Coupon.Status == Active`

**Wait — is this possible?**  
Only if the crash happens AFTER `SaveChangesAsync()` commits but BEFORE the final `transaction.Status = Completed` update in the same transaction. But since all three SaveChangesAsync calls are within the same `BeginTransactionAsync`, they're atomic — either all commit or all rollback.

**What PartialFailure actually catches:**  
The simulation endpoint (`POST /api/test/simulate-partial-failure`) intentionally commits wallet credit + PartialFailure transaction WITHOUT marking the coupon. This represents a code-level bug (e.g., an exception thrown between wallet update and coupon update in the service layer) where the transaction was committed in two separate SaveChanges calls.

**Reconciler Fix:**
- Marks `Coupon.Status = Redeemed` (coupon not usable again)
- Marks `Transaction.Status = Completed`
- Does NOT re-credit wallet (already has the money)
- Runs inside a Serializable transaction — safe against concurrent reconcile runs

---

## Failure Scenario 3: Concurrent Redemption of Same Coupon

**Scenario:** Two users (or the same user from two devices) simultaneously send `POST /api/coupons/redeem` with the same coupon code at the exact same millisecond.

**System Behavior:**
- Both requests enter the Serializable transaction and attempt `SELECT ... WITH (UPDLOCK, ROWLOCK)`
- SQL Server grants the lock to one request; the other waits
- First request: validates `Status == Active` → proceeds → commits → Coupon is now `Redeemed`
- Second request: acquires lock → reads `Status == Redeemed` → returns 409 Conflict
- Wallet is credited exactly once

**Guarantee:** At most one redemption per coupon, even under concurrent load.

---

## Failure Scenario 4: Database Goes Down During Redemption

**Scenario:** SQL Server becomes unavailable mid-request.

**System Behavior:**
- EF Core connection retry policy (configured with `EnableRetryOnFailure(3)`) retries up to 3 times with exponential backoff
- If DB remains unavailable, the request fails with a 500 error
- No partial state is written (connection failure before commit = no data change)
- Client should surface a "Server temporarily unavailable" message and retry later

---

## Failure Scenario 5: Reconcile During Live Traffic

**Scenario:** Admin manually triggers `POST /api/admin/reconcile` while users are actively redeeming coupons.

**System Behavior:**
- Reconciler processes each issue in its own Serializable transaction
- If a coupon is being actively redeemed concurrently:
  - Either the redemption commits first → reconciler sees `Status = Completed` → skips it
  - Or the reconciler fixes it first → the real redemption's idempotency check returns the cached result
- Reconciler is idempotent — calling it twice has no negative effect

**Potential Issue:** Reconciler could fix a "PartialFailure" that was actually part of an in-flight real redemption that hasn't committed yet. Mitigation: Add a minimum age threshold (e.g., only reconcile transactions older than 30 seconds).

---

## Failure Scenario 6: JWT Token Compromise

**Scenario:** An attacker obtains a valid JWT.

**System Behavior:**
- JWTs expire after 8 hours (`ClockSkew = Zero` is set — no grace period)
- No token revocation mechanism in current design (stateless JWTs)
- Mitigation: Implement a token blacklist in Redis, or use shorter expiry (15 min) + refresh tokens

---

## Summary Table

| Failure | Detected By | Auto-Healed | Requires Manual Action |
|---|---|---|---|
| Network timeout + retry | Idempotency key check | Yes | No |
| Duplicate concurrent request | UPDLOCK + idempotency | Yes | No |
| Partial failure (bug/crash) | PartialFailure status | Via reconciler | Trigger reconcile |
| DB unavailable | EF retry policy | After DB recovery | No |
| JWT compromise | Token expiry | Partially | Rotate JWT key |
