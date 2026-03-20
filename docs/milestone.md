# Milestones: VIIDII (Phase 1 + 2026 Lock-in)

---

## PHASE 1: MVP HARDENING (5 Days)

---

## Milestone: Day 1 — Complete WebRTC State Machine JS Integration

**Due Date:** 2026-02-XX

**Description:** Broadcast DFA state changes from C# to JavaScript. Synchronize peer connection states across all clients in real-time. Eliminate ghost-call conditions where one client thinks a peer is connected when the other has dropped.

**Goal:** Zero ghost calls in production. All clients agree on peer state.

**Completion Criteria:**
- ✅ PeerStateService broadcasts state changes to all session participants
- ✅ JavaScript receives and tracks peer state in window.viidii.peerStates Map
- ✅ ParticipantPanel displays correct state badge for each peer
- ✅ session.js guards all peer.call() with canStartCall() validation
- ✅ Load test: 20 peers × 5 transitions = all states sync within 100ms
- ✅ Zero console errors about invalid transitions

---

## Milestone: Day 2 — SignalR Optimization & Sapa Mode

**Due Date:** 2026-02-XX

**Description:** Eliminate thread-pool starvation on mass joins. Implement "Sapa Mode" (power-saving) for low-battery students. Monitor network quality and auto-adapt video bitrate.

**Goal:** 100-student mass join in <3 seconds. Sapa Mode reduces CPU ≥80%.

**Completion Criteria:**
- ✅ Task.Delay(100) removed from SessionHub
- ✅ MatricNoCachingFilter caches MatricNo at connect time
- ✅ 100-student mass join completes in <3 seconds
- ✅ Sapa Mode button works without page restart
- ✅ Quality monitoring detects degraded network (high RTT/packet loss)
- ✅ Auto-downgrade to SD when packet loss >10%
- ✅ CPU usage with video enabled: ~40-50%. With Sapa: ~10%
- ✅ Load test: 100 joins + 5 random quality changes. No thread hangs.

---

## Milestone: Day 3 — EF Core + PostgreSQL Persistence

**Due Date:** 2026-02-XX

**Description:** Replace in-memory mocks with relational database. Implement repository pattern. Survive server restarts. Enable session history and attendance reporting.

**Goal:** Zero data loss on restart. Full session history available.

**Completion Criteria:**
- ✅ EF Core DbContext created with all entity mappings
- ✅ dotnet ef migrations add InitialCreate succeeds
- ✅ PostgreSQL connection string configured
- ✅ IUserRepository, ISessionRepository interfaces defined
- ✅ EfUserRepository, EfSessionRepository implementations working
- ✅ DatabaseSeeder populates test users on startup (Dev mode)
- ✅ Server restart preserves all session data
- ✅ SessionRecap.razor retrieves past sessions from DB
- ✅ Dashboard.razor lists all sessions (past + active)
- ✅ Load test: 50 concurrent sessions, 500 messages. DB commits <1ms each.

---

## Milestone: Day 4 — Zero-App LAN Data Channel + Catch-Up Protocol

**Due Date:** 2026-02-XX

**Description:** Enable offline file sharing via local LAN signaling. Implement catch-up protocol so late-joining students receive message history.

**Goal:** File sharing works with campus internet down. Late-joiners get full chat history within 5 seconds.

**Completion Criteria:**
- ✅ PeerSignalingHub relays WebRTC SDP and ICE on LAN
- ✅ createPeer() factory detects offline mode and uses local signaling
- ✅ Offline Mode toggle works without restart
- ✅ 10MB PDF file transfer completes with internet disconnected (5 recipients)
- ✅ Graceful fallback to cloud signaling if local fails
- ✅ Late-joining student receives full chat history within 5 seconds
- ✅ Message injection shows "📩 Catch-up: 5 prior messages received"
- ✅ File manifest preserved and delivered to late-joiners

---

## Milestone: Day 5 — Dynamic Cryptographic QR Attendance

**Due Date:** 2026-02-XX

**Description:** Implement rotating HMAC-SHA256 QR codes to verify physical presence. One scan per student per session. Replay attack protection. Attendance recap shows verification status.

**Goal:** Proxy attendance impossible. 100% confidence student is physically present in lecture theatre.

**Completion Criteria:**
- ✅ AttendanceTokenService generates HMAC tokens (15-second windows)
- ✅ QRCoder NuGet package integrated
- ✅ LecturerSessionView displays QR code without lag
- ✅ QR rotates exactly every 15 seconds
- ✅ Countdown timer shows "QR expires in: 14s"
- ✅ AttendanceScan.razor (/attend?token=X) validates token
- ✅ Student scans valid QR → shows "✅ Checked in"
- ✅ Old QR token → shows "❌ Token expired"
- ✅ Replay attempt (scan same token twice) → rejected
- ✅ SessionRecap.razor shows ✅/❌ for each student
- ✅ AttendanceLogs table tracks all scan attempts (audit trail)
- ✅ Token generation <1ms latency

---

## Phase 1 Summary

**5 Tasks × 1 Day Each = MVP Ready for Production**

| Day | Task | Files Changed | Status |
|-----|------|---------------|--------|
| 1 | DFA JS Integration | 7 files | ⬜ Pending |
| 2 | SignalR + Sapa Mode | 11 files | ⬜ Pending |
| 3 | EF Core + Postgres | 24 files | ⬜ Pending |
| 4 | LAN Mode + Catch-Up | 7 files | ⬜ Pending |
| 5 | QR Attendance | 9 files | ⬜ Pending |

**Total:** 58 file changes (new + modified)  
**Go-Live:** Day 6 (deploy to FUTA)

---

---

## PHASE 2 & 3 (Future)

*(Placeholder milestones for future expansion)*

## Milestone: Phase 2 — Production Hardening

**Due Date:** 2 weeks after Phase 1

**Description:** Security hardening, performance optimization, logging/monitoring, backup strategy.

**Tasks:**
- SSL/TLS certificate management
- Rate limiting on SignalR
- Database backups & recovery
- Centralized logging (Serilog)
- Monitoring dashboard

---

## Milestone: Phase 3 — Feature Expansion

**Due Date:** 4 weeks after Phase 2

**Description:** Multi-room sessions, adaptive bitrate, analytics dashboard, student engagement reports.

**Tasks:**
- Multi-room session support
- Adaptive bitrate refinement
- Lecturer analytics dashboard
- Engagement heatmaps
- Export attendance to CSV

---

---

## 2026 LOCK-IN PROJECT: SYSTEMS ARCHITECT FOUNDATION

*(21 Theory Deliverables + 3 Integration Projects)*

---

## Milestone: Pillar 1 — Computational Physics (Automata & Theory of Computation)

**Due Date:** Weeks 3–4 of lock-in

**Description:** Master Finite Automata, NFA/DFA equivalence, Pumping Lemma, closure properties, DFA minimization. Connect each theory topic to real systems (OS process scheduling, compiler design, microservice patterns, digital hardware).

**FUTA Courses:** CSC307, CSC309

**Course Hours:** ~30 hours theory + 10 hours integration project

### Task: 1.1 DFA/NFA Fundamentals

**Priority:** High

**Complexity:** 3

**Description:** Construct DFAs and NFAs for given languages. Prove equivalence via subset construction. Understand why NFA state explosion matters in real systems (concurrent processes, scheduler complexity).

**Deliverables:**
- 5 DFA construction problems with proofs
- Subset construction proof (NFA → DFA)
- Complexity analysis (state count explosion)
- Systems connection: OS process scheduling cost

### Task: 1.2 NFA → DFA State Explosion

**Priority:** High

**Complexity:** 3

**Description:** Analyze worst-case exponential blowup (2^n states). Connect to OS concurrency limits, thread-pool thread counts, and system design constraints.

**Deliverables:**
- Complexity proof (2^n states worst-case)
- Benchmark: how many OS threads before scheduler breaks?
- Real example: Kubernetes pod scheduling under concurrent requests

### Task: 1.3 Regular Expressions & Lexical Analysis

**Priority:** High

**Complexity:** 2

**Description:** Map regex to finite automata. Implement a basic tokenizer (lexer). Understand why regex engines are fast (DFA + memoization).

**Deliverables:**
- Regex → NFA → DFA pipeline in C#
- Simple lexer (tokenize arithmetic expressions)
- Performance comparison: naive vs. DFA-based

### Task: 1.4 Pumping Lemma Proofs

**Priority:** Medium

**Complexity:** 4

**Description:** Master contradiction-based proofs for non-regularity. Understand where regex (regular languages) fail and parsers must take over.

**Deliverables:**
- 5 Pumping Lemma proofs
- Why JSON cannot be parsed by regex
- Compiler grammar hierarchy (regex < CFG < Turing)

### Task: 1.5 Closure Properties

**Priority:** Medium

**Complexity:** 3

**Description:** Prove closure under union, intersection, complement, concatenation, Kleene star. Map to microservice composability (can you compose two services and still get a valid service?).

**Deliverables:**
- Closure proofs (5 properties)
- Microservice pattern mapping
- Real example: composing authentication + authorization filters

### Task: 1.6 DFA Minimization

**Priority:** Medium

**Complexity:** 4

**Description:** Implement Hopcroft's algorithm. Understand gate reduction in digital circuits (fewer states = fewer gates, faster hardware).

**Deliverables:**
- Hopcroft's algorithm in C#
- Before/after state count (show minimization benefit)
- Digital hardware connection (gate count)

### Task: 1.7 Integration Project — Mini Compiler Front-End

**Priority:** High

**Complexity:** 5

**Description:** Build a lexer + parser boundary. Demonstrate automata theory in production (tokenize → parse → AST).

**Deliverables:**
- Lexer: Tokenize simple math expressions (+ - * / parentheses)
- Parser: Build Abstract Syntax Tree (AST)
- Interpreter: Evaluate AST
- 500+ lines of production-quality C# code
- Blog post: "I Built a Parser in C# Using Automata Theory"

---

## Milestone: Pillar 2 — Mathematical Physics (Linear Algebra)

**Due Date:** Weeks 5–6 of lock-in

**Description:** Master eigenvalues, eigenvectors, diagonalization, spectral theorem, control theory, PCA. Connect to distributed systems (consensus algorithms), machine learning, and data analysis.

**FUTA Courses:** MTS203

**Course Hours:** ~30 hours theory + 10 hours integration project

### Task: 2.1 Eigenvalues & Eigenvectors

**Priority:** High

**Complexity:** 3

**Description:** Compute eigenvalues/eigenvectors for matrices (up to 4×4). Interpret geometrically (stretching direction).

**Deliverables:**
- 10 eigenvalue problems (hand + C#)
- Geometric interpretation diagrams
- Power iteration method implementation

### Task: 2.2 Diagonalization & Jordan Normal Form

**Priority:** High

**Complexity:** 4

**Description:** Diagonalize matrices. Handle defective matrices via Jordan Normal Form. Understand system exponential behavior (e^At where A is the system matrix).

**Deliverables:**
- Diagonalization proofs (5 problems)
- Jordan Normal Form for defective matrices
- Exponential blowup analysis

### Task: 2.3 Spectral Theorem

**Priority:** High

**Complexity:** 4

**Description:** Prove and apply spectral theorem for symmetric/Hermitian matrices. Understand why spectral analysis is powerful (real eigenvalues, orthogonal eigenvectors).

**Deliverables:**
- Spectral theorem proof
- Symmetric matrix diagonalization (5 problems)
- Why Hermitian matrices matter (quantum computing preview)

### Task: 2.4 PageRank & Markov Chains

**Priority:** Medium

**Complexity:** 4

**Description:** Implement PageRank algorithm. Analyze Markov chain convergence via dominant eigenvalue of transition matrix.

**Deliverables:**
- PageRank in C# (power iteration)
- Markov chain convergence analysis
- Why Google's search works (spectral analysis)
- Real example: link graph of 100 pages

### Task: 2.5 Control Theory & Stability

**Priority:** Medium

**Complexity:** 4

**Description:** Analyze system stability using eigenvalue placement. Avoid exponential blowup via Jordan Form inspection.

**Deliverables:**
- Stability analysis (5 systems)
- Why poles must be inside unit circle
- Linearization & feedback control

### Task: 2.6 PCA & Dimensionality Reduction

**Priority:** Medium

**Complexity:** 4

**Description:** Implement Principal Component Analysis from scratch using covariance matrices and eigenvectors. Reduce 100-dimensional data to 2D visualization.

**Deliverables:**
- PCA algorithm in C# (no numpy!)
- Covariance matrix computation
- Scree plot (explained variance)
- Real example: MNIST handwriting data (10000 images → 2D visualization)

### Task: 2.7 Integration Project — Distributed System Simulator

**Priority:** High

**Complexity:** 5

**Description:** Build consensus algorithm simulator. Use spectral analysis to predict convergence speed (dominant eigenvalue = convergence rate).

**Deliverables:**
- Distributed system simulator (10+ nodes)
- Consensus algorithm (Raft-lite)
- Spectral convergence prediction
- Benchmark: 1000 consensus rounds, measure variance decay
- Blog post: "I Predicted Distributed System Behavior Using Linear Algebra"

---

## Milestone: Pillar 3 — Empirical Physics (Statistics)

**Due Date:** Weeks 7–8 of lock-in

**Description:** Master probability distributions, regression, hypothesis testing, queuing theory, Monte Carlo simulation, bootstrapping. Apply to system design (capacity planning, performance benchmarking, load testing).

**FUTA Courses:** Statistics (cross-department)

**Course Hours:** ~30 hours theory + 10 hours integration project

### Task: 3.1 Probability Distributions

**Priority:** High

**Complexity:** 3

**Description:** Master discrete (Binomial, Poisson, Geometric) and continuous (Normal, Exponential, Uniform) distributions. Recognize real-world examples.

**Deliverables:**
- Distribution properties (mean, variance, CDF, PDF)
- 10 distribution problems (hand + C#)
- Real example: network packet arrivals (Poisson)

### Task: 3.2 Regression Analysis

**Priority:** High

**Complexity:** 3

**Description:** Implement linear and logistic regression. Interpret coefficients for system performance modeling.

**Deliverables:**
- Linear regression algorithm (least squares)
- Logistic regression (sigmoid)
- R² goodness-of-fit metric
- Real example: predict response latency from CPU load

### Task: 3.3 Hypothesis Testing & Confidence Intervals

**Priority:** High

**Complexity:** 3

**Description:** Apply t-tests, chi-square tests. Build confidence intervals for benchmark results.

**Deliverables:**
- t-test implementation (two-sample)
- Chi-square test for independence
- Confidence interval construction
- Real example: is API response time faster after optimization? (statistically significant?)

### Task: 3.4 Queuing Theory

**Priority:** Medium

**Complexity:** 4

**Description:** Model M/M/1 and M/M/c queues. Apply to load balancer capacity planning.

**Deliverables:**
- M/M/1 queue simulator
- Erlang formula (call capacity)
- Utilization vs. wait time trade-off
- Real example: how many servers needed for 99% SLA?

### Task: 3.5 Monte Carlo Simulation

**Priority:** Medium

**Complexity:** 4

**Description:** Implement Monte Carlo methods for estimating system metrics under uncertainty.

**Deliverables:**
- Monte Carlo integration (estimate π)
- System reliability estimation
- Failure probability under random load
- Real example: estimate P(system crash) with random network delays

### Task: 3.6 Bootstrapping

**Priority:** Medium

**Complexity:** 3

**Description:** Apply bootstrap resampling for non-parametric confidence intervals on system performance data.

**Deliverables:**
- Bootstrap confidence interval algorithm
- Resampling from empirical distribution
- Real example: confidence interval on median latency (no normal assumption)

### Task: 3.7 Integration Project — Load Testing Framework

**Priority:** High

**Complexity:** 5

**Description:** Build load-testing tool that uses statistical modeling to predict system behavior under stress. Output: "System can handle 10,000 req/s with 99.9% confidence."

**Deliverables:**
- Load generator (concurrent requests to API)
- Latency histogram + percentiles (p50, p95, p99)
- Statistical regression: predict latency at 50k req/s
- Monte Carlo failure prediction: what's P(timeout) at scale?
- Report generator (markdown + charts)
- Real example: load test VIIDII API with 100 concurrent sessions
- Blog post: "I Built a Statistical Load Tester in C#"

---

## 2026 Lock-in Summary

| Pillar | Duration | Deliverables | Integration Project | Status |
|--------|----------|---------------|---------------------|--------|
| 1: Automata | Weeks 3–4 | 7 theory | Mini Compiler | ⬜ Pending |
| 2: Linear Algebra | Weeks 5–6 | 7 theory | Distributed Simulator | ⬜ Pending |
| 3: Statistics | Weeks 7–8 | 7 theory | Load Testing Framework | ⬜ Pending |

**Total:** 21 theory deliverables + 3 integration projects  
**Total Hours:** ~100 hours theory + 30 hours projects = 130 hours total  
**Output:** YouTube, Substack, Twitter/X, GitHub (3 repos with full source code)

---

## Success Criteria (End of 2026 Lock-in)

- ✅ 21 theory deliverables authored + published
- ✅ 3 integration projects complete with production-quality code
- ✅ 10+ blog posts / YouTube videos published
- ✅ 50+ Twitter/X posts (build in public)
- ✅ 3 GitHub repos with documentation + tests
- ✅ Systems architect foundation: understand how theory maps to real problems
- ✅ VIIDII implementation enhanced with insights from each pillar

---

**Last Updated:** 2026-02-XX  
**Status:** Ready for GitHub Issues generation
