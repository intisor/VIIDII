# VIIDII Performance Rework Requirements

## 1. Purpose and portfolio role

VIIDII is the portfolio's real-time classroom system: a .NET 10 Blazor Web App where lecturers create live sessions and students join through SignalR for presence, attendance, messaging, engagement prompts, and recap. The rework must demonstrate reliable real-time systems engineering rather than a one-off speed optimization.

The rework is needed because the current runtime mixes connection-local state, attendance truth, database reconstruction, background polling, and hub fan-out. That design can work on one process, but it makes disconnects, restarts, concurrent status changes, and horizontal scale difficult to reason about. No current latency, throughput, connection-capacity, allocation, or error-rate baseline is present in the repository; performance improvements therefore remain hypotheses until measured.

Portfolio evidence must show:

- a measured SignalR baseline and repeatable hub load suite;
- explicit separation of ephemeral presence from durable attendance;
- correct reconnect, disconnect, restart, and multi-instance behavior;
- observable connection and persistence lifecycles;
- a before/after case study whose results are traceable to committed scripts and raw outputs.

## 2. Verified current baseline

The following is verified from the current repository and is not a performance result:

- `VIIDII.csproj` targets `net10.0` and references SignalR Client 9.0, EF Core 10.0, and SQL Server. No OpenTelemetry, BenchmarkDotNet, Redis, Azure SignalR, or load-test package is referenced.
- `Program.cs` registers `SessionService` as a singleton, `MessageService` and repositories as scoped, SQL Server through `AddDbContext`, in-process distributed memory cache, cookie session, SignalR, `ParticipantPingService`, and `SessionCleanupService`.
- `/sessionHub` is constrained to WebSockets. SignalR detailed errors are enabled, keep-alive is 10 seconds, and client timeout is 60 seconds.
- Database migration and seeding run synchronously during application startup.
- `SessionService` owns a process-local `ConcurrentDictionary<string, Session>` and reconstructs active sessions from SQL on cache misses.
- Per-session members such as participant IDs, statuses, events, and connection IDs are mutable collections ignored by EF Core in `ViidiiDbContext`; they are not intrinsically safe merely because the outer session map is concurrent.
- `SessionHub` stores `_lastSeen` and `_pendingEngagementPrompts` in static process-local dictionaries. Lecturer and participant SignalR connection IDs are stored in live `Session` objects.
- `JoinSession`, status changes, disconnects, starts, and ends initiate several persistence operations using unobserved fire-and-forget tasks inside `SessionService`. Failure is logged to `Console`, but callers do not receive durability confirmation.
- `SessionRepository.AddAttendanceLogAsync` reads the latest event, updates its duration, inserts a new event, and saves. No idempotency key or optimistic concurrency token is configured.
- Many status changes calculate all participant statuses and attendance scores and send full snapshots to the lecturer. `StartSession` and `JoinSession` resolve participant display names one at a time.
- `ParticipantPingService` periodically enumerates active sessions and participants, writes static prompt state, sends direct prompts where connection IDs exist, and also sends a group fallback. It also queries active sessions through a database-backed path.
- `OnDisconnectedAsync` marks a student disconnected, publishes full snapshots, then calls `LeaveSession`, which removes the participant from live membership and starts an unobserved persistence operation.
- Durable entities exist for sessions, participants, messages, attendance logs, and files. The README explicitly describes a hybrid goal: memory-backed live presence with restart-safe history.
- The EF model has unique indexes for user matriculation number and public session ID, plus composite keys for session participants. No explicit compound attendance index for `(SessionId, StudentId, Timestamp)` is configured.
- `VIIDII.slnx` references a `VIIDII.Tests` project, but that directory was not present in the inspected source inventory. A Playwright smoke spec exists. `package.json` still has a failing placeholder `npm test` script.
- `.github/workflows/viidii1.yml` is stale relative to the current project: it installs .NET 9, points to `FutaMeetWeb`, and publishes from that old path while the app now targets .NET 10 at repository root.
- No verified benchmark outputs or production telemetry baseline are committed. Existing console messages are not a substitute for structured measurements.

## 3. Target outcomes

Targets are to be established in two steps so the project does not invent numbers.

### 3.1 Baseline first

Capture at least three comparable runs on a documented machine/container profile for each workload in section 8. Record commit SHA, SDK/runtime, CPU, memory, SQL Server version/tier, transport, network topology, instance count, dataset seed, test duration, and warm-up. Publish raw and summarized p50/p95/p99 hub-operation latency, reconnect latency, attendance-persistence latency, messages/second, successful connections, disconnects, error rate, process CPU, working set, GC collections, allocation rate, thread-pool queue length, SignalR connection count, SQL command duration, and database waits where available.

### 3.2 Budgets after baseline

The first accepted baseline becomes `B0`. The team must ratify absolute service-level objectives from real expected class sizes and hosting capacity. Until then, use these non-invented relative gates:

- correctness: zero lost or duplicated durable attendance transitions in the deterministic fault suite;
- capacity: complete the agreed target class profile with no uncontrolled connection loss;
- errors: no increase from `B0`, excluding explicitly injected failures;
- latency: p95 and p99 for join, status update, lecturer snapshot/delta, and reconnect must not regress beyond the measured noise envelope;
- efficiency: allocation rate and CPU per delivered hub event must not regress beyond the measured noise envelope;
- improvement acceptance: a claimed optimization must improve its primary metric by more than run-to-run variation while holding correctness and secondary metrics stable.

An absolute budget is valid only when stored in a versioned `performance-budgets.json` with its workload, environment, rationale, baseline artifact, owner, and review date.

## 4. Scope

### In scope

- SignalR hub lifecycle, authorization, grouping, reconnect, and fan-out.
- Ephemeral presence, lecturer/participant connection registration, and engagement prompts.
- Durable attendance transition capture, replay, finalization, and recap calculation.
- Session runtime concurrency and restart reconstruction.
- Multi-instance scale-out design and validation.
- Query shape, indexing, payload size, serialization, logging, and startup behavior where they affect the above paths.
- Repeatable microbenchmarks, hub/load tests, integration tests, telemetry, CI, and deployment assets.

### Non-goals

- Rewriting the Blazor user experience or adopting a different UI framework.
- Replacing SignalR with a custom WebSocket protocol.
- Treating WebRTC/media transport as part of the attendance control-plane benchmark unless it is later shown to share a bottleneck.
- Changing attendance scoring policy without a separately approved product decision.
- Adding Redis, Azure SignalR, or a message broker before a measured single-instance or scale-out requirement justifies it.
- Optimizing mock user lookup while leaving production identity requirements undefined.

## 5. Required architecture rework

### 5.1 Thin hub and explicit application commands

`SessionHub` must validate identity/session membership and delegate to application services. Hub methods must not own attendance calculation, per-user database lookup loops, connection-state mutation, or persistence orchestration. Define ty
Define typed commands/results for join, start, presence transition, issue flag, engagement acknowledgement, message operations, end, and disconnect. Every command must propagate cancellation and use stable problem codes. Retryable commands require correlation/idempotency keys.

### 5.2 Separate state by durability

1. **Connection state:** connection ID, user/session/role, connected time, heartbeat, node, and reconnect generation. It is ephemeral.
2. **Presence projection:** current status and last transition for fast fan-out. It is reconstructable.
3. **Attendance ledger:** append-only ordered transitions with event ID, session/user IDs, source, observed/accepted UTC timestamps, and connection generation. It is the scoring and recap source of truth.

Do not persist SignalR connection IDs as business state. Route lecturers through groups or a connection-registry abstraction rather than a single mutable `LecturerConnectionId`.

### 5.3 Per-session concurrency

Serialize session mutations through a per-session async lock or actor/mailbox; benchmark and document the choice. Broadcast code must read immutable snapshots. Define ordering for concurrent tab changes, heartbeats, issue flags, reconnects, and disconnect callbacks. A stale disconnect for generation N must not mark generation N+1 disconnected.

### 5.4 Durable attendance path

A hub acknowledgement that implies attendance credit must not precede durable acceptance. Use either direct transactional append with a unique event ID and optimistic retry, or a transactional outbox acknowledged after commit and followed by asynchronous projection/fan-out. Duplicate event IDs must be harmless, shutdown must drain accepted work, and finalization must be atomic and retry-safe. Unobserved fire-and-forget database writes are prohibited.

Use UTC throughout domain calculations. Existing manual `AddHours(1)` adjustments must be removed; local conversion belongs at presentation boundaries.

### 5.5 Delta fan-out and backpressure

Publish a small versioned delta for a presence transition. Full participant/status/score snapshots are for initial join, explicit resync, or version-gap recovery. Load names in one set-based query or a bounded projection cache, not one query per participant.

Define bounded queues, maximum message size, slow-consumer policy, cancellation, and resync behavior. Durable events may never be silently dropped. Ephemeral UI deltas may coalesce by participant only if the latest version remains available.

### 5.6 Scale-out

Connection registry and presence projection require single-node and distributed implementations. Multi-instance tests must select Azure SignalR Service or a Redis backplane through an ADR comparing cost, affinity, failure behavior, group propagation, observability, and local testing.

Scale-out acceptance must prove cross-node groups, reconnect to another node, recovery from node termination, singleton/partitioned engagement jobs, and no reliance on local memory as cross-node truth.

## 6. Database, caching, and lifecycle requirements

- Measure query plans before indexing. Candidates are attendance `(SessionId, StudentId, Timestamp)`, session status/expiry, and lecturer/status; add only with evidence.
- Encode event idempotency and optimistic concurrency in constraints.
- Use `AsNoTracking` projections for read-only paths; reconstruction must not include messages when unnecessary.
- Move migrations to a deployment/init job before horizontal scale; readiness waits for dependencies.
- Cache only reconstructable data with size/expiry/invalidation. Attendance truth must not live in `IDistributedCache`.
- Cleanup must be set-based/batched and follow retention policy; live-cache expiry must not delete required history.

## 7. BenchmarkDotNet plan

Create a dedicated Release-only `VIIDII.Benchmarks` console project in the solution. Use BenchmarkDotNet, `MemoryDiagnoser`, .NET 10 jobs, deterministic seeds, and parameterized sizes. It must benchmark CPU/allocation questions only:

- attendance scoring over ordered event streams;
- presence reconstruction from durable events;
- full snapshot versus delta construction/serialization;
- per-item versus set-based name enrichment;
- async lock versus actor/mailbox dispatch under contention;
- coalescing repeated presence updates;
- engagement recipient/exclusion set construction.

Each case states its hypothesis, data shape, correctness validation, and decision it informs. EF InMemory timing must not be presented as SQL performance.

## 8. SignalR and load-test plan

Use NBomber with a SignalR-capable client for authoritative lifecycle tests. k6 may cover HTTP setup and WebSocket smoke, but tests must invoke the actual SignalR protocol through Kestrel and SQL Server.

### Data and concurrency

Generate deterministic lecturers, students, sessions, participants, and ledgers. Candidate exploration profiles are one lecturer with 25, 50, 100, and 250 students; these are inputs, not capacity claims. Start with 1, 5, and 10 student connections, then double stages to the ratified target and controlled saturation. Hold stages for multiple keep-alive/engagement cycles; use arrival-rate ramps for join bursts.

### Workloads

1. Cold join burst and group registration.
2. Steady classroom mix of tab transitions, confirmations, issue flags, comments, and reactions.
3. Automatic/manual engagement wave with duplicate-prompt checks.
4. Student reconnect storm with jitter and stale-disconnect assertions.
5. Lecturer reconnect and versioned resync.
6. End-session while transitions are in flight, followed by oracle recap comparison.
7. App restart and reconstruction from SQL.
8. Two-instance node loss behind the selected backplane.
9. Soak for leaks, prompt duplication, pool behavior, and GC stability.

### Measurements and assertions

Capture p50/p95/p99/max for negotiate/connect, join, status acknowledgement, lecturer delta receipt, reconnect/resync, database append, and end-to-recap. Capture hub messages/second, HTTP RPS, active/successful connections, drops/retries, errors, timeouts, SQL roundtrips, allocation rate, GC, CPU, memory, thread pool, sockets, and database waits/pool state.

Reconcile sent command IDs, accepted event IDs, projected versions, received versions, and persisted rows. Good latency with dropped events is a failed run.

## 9. OpenTelemetry

Instrument ASP.NET Core, EF Core/SqlClient, runtime/process, and custom operations; export via OTLP. Required spans: `session.join`, `session.start`, `presence.transition`, `attendance.append`, `presence.publish`, `session.resync`, `session.end`, `attendance.finalize`, and `session.reconstruct`.

Metrics must include connections by role/transport/node; join/reconnect/disconnect duration; accepted/rejected/duplicate events; queue depth/age; active sessions and participant-count histograms; delta/snapshot count and bytes; prompt outcomes; SQL duration/roundtrips; reconstruction duration; allocation, GC, CPU, memory, thread-pool, and exceptions. Never use session/user IDs as metric labels.

Replace `Console.WriteLine` with structured logs. Do not log cookies, tokens, passwords, message content, or student attendance detail at information level. Hash or access-control identifiers and document retention.

## 10. Security and privacy

- Authenticate hub connections and authorize every method against identity, role, membership, and session. Never trust caller-supplied user/session/peer identifiers alone.
- Disable detailed hub errors outside development and apply payload/file limits plus per-user/connection rate limits.
- Treat attendance as education data: least-privilege SQL, encryption, retention/deletion policy, controlled audit access, and synthetic benchmark fixtures.
- Validate origin/CORS, antiforgery/session behavior, Secure/SameSite cookies, cross-session sends, lecturer impersonation, replay, oversized messages, and reconnect floods.
- Store SQL/backplane credentials in deployment secret stores.

## 11. Tests

- Unit tests: state machine, scoring, UTC boundaries, duplicate/out-of-order events, coalescing, stale generations, and authorization.
- Repeated concurrency tests: simultaneous join/status/disconnect/end with invariant checks.
- SQL Server container tests: constraints, conflicts, rollback, query plans, restart reconstruction, and finalization.
- Kestrel SignalR tests: authenticated groups, reconnect, cancellation, errors, and delta/resync.
- Fault tests: SQL timeout, process death after commit/before publish, backplane outage, cancellation, and shutdown drain.
- Playwright: lecturer/student happy path and reconnect/resync UI.
- Property-based event-stream replay is recommended.

Restore the missing/inconsistent `VIIDII.Tests` project and replace the placeholder npm test command without weakening discovery.

## 12. GitHub Actions and regression strategy

Replace the stale workflow with .NET 10/root-solution jobs for restore/build, unit tests, SQL integration, Playwright smoke, container build, dependency/security scan, and artifacts. Protect deployment environments.

Avoid noisy gates:

- PRs compile benchmarks and run short functional smoke only.
- Full BenchmarkDotNet/NBomber runs are nightly, manual/label-triggered, and pre-release on a controlled runner.
- Retain raw JSON/CSV/Markdown, environment metadata, and SHA.
- Derive noise from repeated baselines; fail only when regression exceeds both practical threshold and measured variation in repeated/consecutive runs.
- One noisy run is advisory. Correctness loss, authorization bypass, or unbounded growth fails immediately.
- Release load gates run against isolated ephemeral staging.

## 13. Deployment, containers, and Aspire

Provide a multi-stage non-root image, externalized configuration, health/readiness, graceful connection drain, migration job, and rollback. LocalDB is development-only.

Aspire is justified as optional local/integration orchestration for VIIDII, SQL Server, OTLP tooling, and the selected backplane. It must improve reproducibility, not become an unexplained production dependency. Document proxy WebSocket settings, affinity, connection draining, autoscaling signals, and service/backplane limits. Autoscaling must consider active connections, reconnect pressure, persistence queue age, and SQL capacity, not CPU alone.

## 14. Phased backlog

### P0 — correctness and baseline

- Restore tests and current CI.
- Add structured logging/OpenTelemetry and deterministic seed/load scripts.
- Capture `B0`, discover real class profiles, and ratify budgets.
- Remove local-time arithmetic.
- Replace fire-and-forget attendance writes with awaited idempotent durability.
- Define connection generations and stale-disconnect rules.
- Add concurrency tests and query-plan/index evidence.

### P1 — efficient single node

- Thin hub; add command/state services and serialized per-session mutation.
- Add immutable snapshots, deltas, and resync.
- Batch name lookup and reconstruction queries.
- Add BenchmarkDotNet, Kestrel SignalR, restart/fault/soak suites.
- Containerize dependencies and move migrations out of startup.

### P2 — scale-out and portfolio hardening

- Select/implement Azure SignalR or Redis through an ADR.
- Partition/singleton background jobs.
- Run multi-node node-loss/reconnect/release tests.
- Add Aspire if justified.
- Publish dashboards, runbooks, diagrams, budgets, and case study.

## 15. Deliverables

- ADRs for state ownership, durability, concurrency, and scale-out.
- `VIIDII.Benchmarks` and SignalR/NBomber/k6 assets with deterministic seeds.
- Telemetry configuration, dashboards, alerts, and redaction policy.
- Unit, SQL, SignalR, fault, Playwright, and scale-out tests.
- Current CI, container, and optional Aspire assets.
- Raw baseline/post-change results and versioned budgets.
- Runbooks for outage, reconnect storm, drain, migration, rollback, and reconciliation.
- Portfolio case study and architecture diagrams.

## 16. Acceptance criteria

- Every improvement links to reproducible evidence; no unrerun historical number is called current.
- The agreed target class workload passes ratified p50/p95/p99, throughput, error, allocation/resource, and connection budgets.
- Fault/load runs map every accepted attendance command to exactly one durable event and reproduce identical recap after restart.
- Stale disconnects, duplicates, and out-of-order callbacks cannot corrupt presence/attendance.
- Single-node and selected multi-node topologies pass join, engagement, reconnect, lecturer resync, end, node-loss, and soak.
- Security tests prevent unauthenticated, cross-session, and student-to-lecturer privilege escalation.
- One trace follows a hub command through persistence and fan-out; required metrics exist without sensitive payloads.
- CI validates .NET 10 and applies the controlled noise-aware policy.
- Deployment supports readiness, drain, external secrets, migration isolation, and rollback.

## 17. Case-study evidence

Include original architecture/constraints, hypotheses, environment manifest, methodology, before/after p50/p95/p99 and RPS/messages-per-second/errors/allocations/resources, trace or flame-graph bottleneck evidence, query plans for database changes, correctness reconciliation, and tradeoffs. Show one rejected optimization. Distinguish microbenchmark, staging load, and production evidence.

## 18. Risks and tradeoffs

- A backplane routes messages but does not make attendance durable.
- Per-session serialization improves correctness but may create a hot queue.
- Awaited durability adds latency; an outbox adds replay/operations complexity.
- Deltas save network/CPU but require versioning and resync.
- Faster liveness detection increases fan-out, mobile data use, and false inactivity.
- Scale-out may move the bottleneck to SQL/backplane and increase cost.
- Microbenchmarks do not prove network/database capacity; load tests do not replace production telemetry.

## Definition of Done

The rework is done only when the agreed P0/P1/P2 scope is implemented; all acceptance criteria pass on a documented reference environment; attendance survives duplicate, reconnect, restart, and node-loss tests; the scale topology is reproducible; security/privacy findings are resolved; telemetry and runbooks are operational; CI uses noise-aware regression controls; raw before/after artifacts are retained; and the case study reports measured results and tradeoffs without invented metrics.
