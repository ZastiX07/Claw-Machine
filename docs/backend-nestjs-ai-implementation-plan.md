# Claw Machine Backend Plan for AI Implementer (NestJS + TypeScript)

## 1) Purpose
Build a backend that prevents client-side cheating without running full Unity physics on the server.

Primary rule: server is authoritative for economy and final outcome (`win/lose`, reward item, claim status).

## 2) Context from Current Unity Client
Current gameplay logic is in:
- `Assets/Scripts/ClawMachine/ClawController.cs`
- `Assets/Scripts/ClawMachine/MovementController.cs`
- `Assets/Scripts/ClawMachine/InputController.cs`
- `Assets/Scripts/ClawMachine/FingerContactSensor.cs`

Key facts:
- Rail movement and state timing are deterministic and easy to replay from input.
- Finger contacts, collisions, joints, and toy body behavior are physics-dependent and non-deterministic.
- Random outcomes are currently local in client (`Random.value`, `Random.Range`) and must move to server.

## 3) Product Constraints
- Stack: TypeScript, NestJS.
- Telegram users (verify Telegram auth data).
- Mobile latency tolerance: do not require frame-perfect server sync.
- Do not trust any client claim like "I grabbed toy X".

## 4) High-Level Solution (Hybrid Authority)
Do NOT replicate Unity physics on backend.
Use this model:
1. Client sends only user inputs + telemetry.
2. Server replays simplified deterministic kinematics (rail movement + state times).
3. Server computes final attempt result using:
   - validated kinematic position at drop/close phases
   - server-side RNG and reward table
   - anti-cheat checks
4. Client uses local physics only for visuals.
5. Reward grant always comes from server decision.

## 5) Backend Architecture (NestJS Modules)

### 5.1 Modules
- `AuthModule`
  - Telegram `initData` validation
  - User session/JWT issuance
- `UsersModule`
  - User profile and limits
- `WalletModule`
  - Tickets/coins balance, transactional debit/credit
- `AttemptModule`
  - Attempt lifecycle, input ingestion, resolution
- `RewardModule`
  - Reward tables, weighted RNG, claim pipeline
- `AntiCheatModule`
  - Rules engine, anomaly flags, risk scoring
- `AuditModule`
  - Immutable event log
- `ConfigModule`
  - Tunable machine configs/versioning
- `AdminModule` (internal)
  - Reward odds, config rollout, monitoring

### 5.2 Infrastructure
- PostgreSQL (source of truth)
- Redis (rate limiting, short-lived state, queues)
- BullMQ (optional) for async reward delivery retries
- OpenAPI/Swagger for strict contracts

## 6) Domain Model (PostgreSQL)

### 6.1 Core tables
- `users`
  - `id (uuid pk)`
  - `telegram_user_id (bigint unique)`
  - `created_at`, `updated_at`

- `wallets`
  - `user_id (pk/fk users.id)`
  - `tickets int not null`
  - `coins bigint not null`
  - `version int not null` (optimistic lock)

- `attempts`
  - `id (uuid pk)`
  - `user_id (fk)`
  - `status enum('started','inputs_closed','resolved','claimed','cancelled')`
  - `config_version text not null`
  - `seed_hash text not null`
  - `seed_reveal text null`
  - `started_at`, `resolved_at`, `expires_at`
  - `risk_score int default 0`
  - `result enum('win','lose','void') null`
  - `reward_id uuid null`

- `attempt_inputs`
  - `attempt_id (fk)`
  - `seq int`
  - `client_time_ms bigint`
  - `dir_x real`, `dir_y real` (clamped -1..1)
  - `received_at timestamptz`
  - unique `(attempt_id, seq)`

- `rewards`
  - `id (uuid pk)`
  - `code text unique`
  - `rarity text`
  - `weight int`
  - `is_active bool`
  - `stock int null`

- `reward_grants`
  - `id (uuid pk)`
  - `attempt_id (fk unique)`
  - `user_id (fk)`
  - `reward_id (fk)`
  - `status enum('pending','granted','failed')`
  - `idempotency_key text unique`
  - `provider_tx_id text null`
  - `created_at`, `updated_at`

- `audit_events`
  - `id (bigserial pk)`
  - `user_id uuid null`
  - `attempt_id uuid null`
  - `event_type text`
  - `payload jsonb`
  - `created_at timestamptz`

- `anti_cheat_flags`
  - `id (bigserial pk)`
  - `user_id uuid`
  - `attempt_id uuid`
  - `flag_type text`
  - `severity int`
  - `details jsonb`
  - `created_at timestamptz`

### 6.2 Required indexes
- `attempts(user_id, started_at desc)`
- `attempt_inputs(attempt_id, seq)`
- `audit_events(attempt_id, created_at)`
- `anti_cheat_flags(user_id, created_at desc)`

## 7) API Contract (v1)

### 7.1 Auth
`POST /v1/auth/telegram`
- request:
  - `initData: string`
- response:
  - `accessToken: string`
  - `expiresInSec: number`
  - `user: { id, telegramUserId }`

Validation:
- Verify Telegram hash using bot token.
- Reject old `auth_date` beyond allowed TTL.

### 7.2 Start Attempt
`POST /v1/attempts/start`
- headers:
  - `Authorization: Bearer <token>`
  - `Idempotency-Key: <uuid>`
- request:
  - `machineId: string`
  - `clientBuild: string`
  - `configVersion: string`
- response:
  - `attemptId: string`
  - `attemptToken: string` (signed, short-lived)
  - `serverNowMs: number`
  - `inputWindowMs: number`
  - `economySnapshot: { ticketsLeft: number }`

Rules:
- Debit ticket atomically in same DB transaction.
- Store `seed_hash` (commit phase).

### 7.3 Send Inputs (batch)
`POST /v1/attempts/:attemptId/inputs`
- headers:
  - `Authorization`
  - `X-Attempt-Token`
- request:
  - `packets: Array<{ seq, clientTimeMs, moveX, moveY }>`
- response:
  - `acceptedSeqUpTo: number`
  - `serverNowMs: number`
  - `warnings: string[]`

Rules:
- Clamp move values to [-1, 1].
- Drop duplicates by `(attemptId, seq)`.
- Reject too-large seq jumps.

### 7.4 Resolve Attempt
`POST /v1/attempts/:attemptId/resolve`
- headers:
  - `Authorization`
  - `X-Attempt-Token`
  - `Idempotency-Key`
- request:
  - `clientSummary: {`
  - `  pressTimeMs: number,`
  - `  closeStartMs?: number,`
  - `  contactHints?: Array<{ toyHintId: string, fingers: number }>`
  - `}`
- response:
  - `attemptId: string`
  - `status: "resolved"`
  - `result: "win" | "lose" | "void"`
  - `reward?: { id: string, code: string, rarity: string }`
  - `seedReveal?: string`
  - `riskScore: number`

Rules:
- Server resolves only once.
- Result is final and persisted transactionally.

### 7.5 Claim Reward
`POST /v1/rewards/claim`
- headers:
  - `Authorization`
  - `Idempotency-Key`
- request:
  - `attemptId: string`
- response:
  - `status: "granted" | "already_granted" | "pending" | "failed"`
  - `reward?: { code, rarity }`

## 8) Attempt Resolution Algorithm (No Full Physics)

### 8.1 Deterministic replay
Replay only rail motion and state timings using config mirrored from Unity:
- movement ranges
- max speed
- acceleration/deceleration
- auto move targets
- drop/rise timings

Use input packets ordered by `seq`, with fixed simulation step (`dt=20ms` recommended).

### 8.2 Skill signal (server-side)
Compute an attempt score from replayed trajectory:
- position quality at drop start
- stability (input jitter/abrupt changes)
- timing quality (button press and lock phase consistency)
- optional telemetry consistency checks

Example:
`skillScore = w1*dropAlignment + w2*stability + w3*timing - w4*anomalyPenalty`

### 8.3 Outcome gate
Server decides win/lose:
- Base chance from reward economy profile
- Modulated by `skillScore` within safe bounds
- Modulated by anti-cheat risk penalty

Then pick reward by weighted RNG (server only).

### 8.4 RNG integrity (commit-reveal)
At start:
- generate `seed`
- store `seed_hash = sha256(seed)`

At resolve:
- derive random stream from `seed`
- persist `seed_reveal`
- return `seedReveal` optionally for audit/debug

## 9) Anti-Cheat Rules (MVP)

### 9.1 Input validation
- Clamp vectors to [-1, 1]
- Max packet rate (for example 30 packets/sec)
- Max allowed backdating (`clientTimeMs` skew bounds)
- Strict monotonic `seq`

### 9.2 Physical plausibility (from replay)
- Position never exits rail bounds
- Effective speed/acceleration cannot exceed config limits
- No movement while control is server-locked phase

### 9.3 Behavioral anomalies
- Abnormally high win rate over rolling windows
- Multi-account patterns (device fingerprint/IP heuristics)
- Repeated identical high-precision trajectories

Actions:
- Increase `risk_score`
- Soft fail attempt (`result=void`) if severe
- Flag account for review

## 10) Unity Integration Requirements

Client changes required:
1. On claw button press:
   - call `POST /attempts/start`
   - if rejected, do not start gameplay cycle
2. During manual movement:
   - stream inputs in small batches every 100-150ms
3. Before prize finalization:
   - call `POST /attempts/{id}/resolve`
4. Spawn reward visuals only from server result payload
5. Call `POST /rewards/claim` for durable grant

Important:
- Local `Random` must not decide real reward.
- Local physics remains visual-only.

## 11) Implementation Milestones

### Milestone 1: Foundation (2-3 days)
- Bootstrap NestJS project
- PostgreSQL + Prisma (or TypeORM) setup
- Auth via Telegram `initData`
- User + wallet tables and migrations

Exit criteria:
- User can authenticate and receive JWT.
- Wallet read/update works transactionally.

### Milestone 2: Attempt lifecycle (3-4 days)
- `attempts/start`, `attempts/inputs`, `attempts/resolve`
- Idempotency middleware
- Input persistence and validation
- Basic deterministic replay service

Exit criteria:
- One attempt can be started, fed with inputs, and resolved exactly once.

### Milestone 3: Reward pipeline (2-3 days)
- Reward tables and weighted selector
- Claim endpoint with idempotent grant records
- Queue-based retry for external delivery (if needed)

Exit criteria:
- Win result creates one durable grant, no duplicates.

### Milestone 4: Anti-cheat + observability (2-4 days)
- Rule engine and risk score
- `anti_cheat_flags` persistence
- Structured logs + audit events
- Basic admin metrics dashboard endpoints

Exit criteria:
- Suspicious attempts are flagged and traceable end-to-end.

### Milestone 5: Hardening (2-3 days)
- Load tests for attempt endpoints
- Integration tests for double-submit/replay attacks
- Config versioning and rollout safety

Exit criteria:
- Stable behavior under expected concurrency.

## 12) Test Strategy

### 12.1 Unit tests
- Telegram hash verification
- Weighted reward picker
- Replay simulator deterministic outputs
- Risk scoring rules

### 12.2 Integration tests
- Start attempt debits ticket exactly once
- Duplicate input seq ignored
- Resolve is idempotent
- Claim is idempotent

### 12.3 Security tests
- Tampered `attemptToken`
- Old JWT and old `initData`
- Input flood/rate-limit violations
- Replay of old requests with same nonce

## 13) Non-Functional Requirements
- P95 for `attempts/inputs` < 120ms
- P95 for `attempts/resolve` < 250ms
- Clock skew tolerance: +/- 10s
- Full audit retention: at least 90 days

## 14) AI Execution Checklist
Use this checklist for the implementation AI:
1. Create NestJS modules listed in section 5.
2. Implement DB schema from section 6 via migrations.
3. Generate OpenAPI spec exactly matching section 7.
4. Implement replay + resolver service from section 8.
5. Add anti-cheat rules from section 9.
6. Add integration tests from section 12.2 before feature complete.
7. Provide migration and seed scripts for rewards.
8. Document all env vars and runbook commands.

## 15) Suggested Env Vars
- `PORT`
- `DATABASE_URL`
- `REDIS_URL`
- `JWT_SECRET`
- `JWT_TTL_SEC`
- `TELEGRAM_BOT_TOKEN`
- `ATTEMPT_TOKEN_SECRET`
- `ATTEMPT_TTL_SEC`
- `INPUT_RATE_LIMIT_PER_SEC`
- `AUDIT_LOG_ENABLED`

## 16) Prompt Template for Another AI
Use this prompt as-is for implementation:

```text
Implement a production-ready NestJS backend for a claw machine game using the attached specification.
Follow the module boundaries, DB schema, and API contract exactly.
Server must be authoritative for attempt result and rewards.
Do not implement full Unity physics; implement deterministic kinematic replay plus server RNG resolver.
Add tests for idempotency, replay protection, and transactional economy safety.
Return:
1) project tree,
2) migration files,
3) DTOs/controllers/services,
4) test suite,
5) run instructions.
```

