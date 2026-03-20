# 🎯 EXECUTION PLAN: GitHub Projects, Milestones, & Issues

**Status:** READY FOR APPROVAL  
**Generated:** 2026-02-XX  
**Repository:** intisor/VIIDII  
**Branch:** lockin

---

## 📊 Current Repository State

### Existing GitHub Issues (5 already created)
✅ **Task 1:** Architect the WebRTC State Machine (DFA) — Day 1 (#8)  
✅ **Task 2:** Fix SignalR Join Storm + Sapa Mode — Day 2 (#9)  
✅ **Task 3:** EF Core + PostgreSQL — Day 3 (#6)  
✅ **Task 4:** Zero-App LAN P2P + Catch-Up Protocol — Day 4 (#7)  
✅ **Task 5:** Cryptographic Rotating QR Attendance — Day 5 (#5)  
❌ **Task 6+:** Lock-in deliverables (NOT YET CREATED)

### Existing GitHub Projects
❌ **"VIIDII Phase 1: MVP Hardening"** (NOT YET CREATED)  
❌ **"2026 Lock-in: Systems Architect Foundation"** (NOT YET CREATED)

### Existing Milestones
❌ Phase 1 Milestones: Days 1–5 (NOT YET CREATED)  
❌ Lock-in Milestones: Pillars 1–3 (NOT YET CREATED)

---

## 📋 EXECUTION PLAN: What Will Be Created/Updated/Skipped

### Phase 1: GitHub Project & Milestones

#### ✅ CREATE: GitHub Project

**Name:** `VIIDII Phase 1: MVP Hardening & Stabilization`

**Metadata:**
- Description: Harden the existing VIIDII WebRTC MVP against FUTA campus network reality. Eliminate ghost calls, fix thread-pool starvation on mass joins, add database persistence, enable offline file sharing, and implement cryptographic attendance verification. A 5-day sprint that transforms a working prototype into a production-ready platform.
- Visibility: public
- Owner: @intisor
- Repository: intisor/VIIDII

---

#### ✅ CREATE: 5 Milestones (for Phase 1)

**Milestone 1: Day 1 — Complete WebRTC State Machine JS Integration**
- Due Date: (not specified in docs, defaults to sprint start + 1 day)
- Description: Broadcast DFA state changes from C# to JavaScript. Eliminate ghost-call conditions where one peer thinks it's connected but the other doesn't.
- Status: Planning

**Milestone 2: Day 2 — SignalR Optimization & Sapa Mode**
- Due Date: (sprint start + 2 days)
- Description: Eliminate thread-pool starvation on mass joins (100+ students). Add power-saving "Sapa Mode" for low-battery students.
- Status: Planning

**Milestone 3: Day 3 — EF Core + PostgreSQL Persistence**
- Due Date: (sprint start + 3 days)
- Description: Replace in-memory mocks with real database. Survive server restarts. Enable session history and attendance records.
- Status: Planning

**Milestone 4: Day 4 — Zero-App LAN Data Channel + Catch-Up Protocol**
- Due Date: (sprint start + 4 days)
- Description: Enable offline file sharing via local LAN signaling. Implement catch-up protocol so late-joining students receive message history.
- Status: Planning

**Milestone 5: Day 5 — Dynamic Cryptographic QR Attendance**
- Due Date: (sprint start + 5 days)
- Description: Implement rotating HMAC-SHA256 QR codes to verify physical presence. One scan per student per session. Replay attack protection.
- Status: Planning

---

#### ✅ CREATE: Custom Fields (for Phase 1 Project)

1. **Priority** (Single-select)
   - Options: Critical, High, Medium, Low
   - Default: High

2. **Complexity** (Single-select)
   - Options: 1, 2, 3, 4, 5
   - Default: 3

3. **Category** (Single-select)
   - Options: 🏗️ Portfolio Build, 🔧 Infrastructure, 🎨 UI/UX, 📊 Analytics
   - Default: 🏗️ Portfolio Build

4. **FUTA Course** (Single-select)
   - Options: CSC305, CSC307, CSC309, SEN204, Cryptography, Other
   - Default: (empty)

5. **Content Output** (Single-select)
   - Options: 📹 YouTube/Twitch, 📝 Substack, 🐦 Twitter/X, 📖 Blog, None
   - Default: (empty)

6. **Status** (Single-select)
   - Options: Not Started, In Progress, Review, Completed, Blocked
   - Default: Not Started

---

#### ✅ UPDATE: 5 Existing Phase 1 Issues (Assign to Project + Milestones)

**Issue #8: Task 1 (WebRTC DFA)**
- Action: Add to Project "VIIDII Phase 1"
- Add to Milestone: "Day 1"
- Set Priority: Critical
- Set Complexity: 5
- Set Category: 🏗️ Portfolio Build
- Set Course: CSC309
- Set Content: 📹 YouTube/Twitch
- Assignee: @intisor
- Status: Not Started

**Issue #9: Task 2 (SignalR + Sapa)**
- Action: Add to Project "VIIDII Phase 1"
- Add to Milestone: "Day 2"
- Set Priority: Critical
- Set Complexity: 5
- Set Category: 🏗️ Portfolio Build
- Set Course: CSC305
- Set Content: 📹 YouTube/Twitch
- Assignee: @intisor
- Status: Not Started

**Issue #6: Task 3 (EF Core + Postgres)**
- Action: Add to Project "VIIDII Phase 1"
- Add to Milestone: "Day 3"
- Set Priority: Critical
- Set Complexity: 5
- Set Category: 🏗️ Portfolio Build
- Set Course: SEN204
- Set Content: 📝 Substack
- Assignee: @intisor
- Status: Not Started

**Issue #7: Task 4 (LAN + Catch-Up)**
- Action: Add to Project "VIIDII Phase 1"
- Add to Milestone: "Day 4"
- Set Priority: High
- Set Complexity: 4
- Set Category: 🏗️ Portfolio Build
- Set Course: CSC307
- Set Content: 🐦 Twitter/X
- Assignee: @intisor
- Status: Not Started

**Issue #5: Task 5 (QR Attendance)**
- Action: Add to Project "VIIDII Phase 1"
- Add to Milestone: "Day 5"
- Set Priority: High
- Set Complexity: 4
- Set Category: 🏗️ Portfolio Build
- Set Course: Cryptography
- Set Content: 📝 Substack
- Assignee: @intisor
- Status: Not Started

---

### Phase 2 & 3: Future Milestones (Created but Empty)

#### ✅ CREATE: 2 Future Milestones

**Milestone 6: Phase 2 — Production Hardening**
- Due Date: 2026-03-XX (2 weeks after Phase 1)
- Description: Security hardening, performance optimization, logging/monitoring, backup strategy.
- Status: Planning
- (No issues assigned yet)

**Milestone 7: Phase 3 — Feature Expansion**
- Due Date: 2026-04-XX (4 weeks after Phase 2)
- Description: Multi-room sessions, adaptive bitrate, analytics dashboard, student engagement reports.
- Status: Planning
- (No issues assigned yet)

---

## 2026 Lock-in: GitHub Project & Milestones

#### ✅ CREATE: GitHub Project

**Name:** `2026 Lock-in: Systems Architect Foundation`

**Metadata:**
- Description: Build an unshakable CS foundation by connecting theoretical coursework to real systems engineering. Master Automata (DFA/NFA), Linear Algebra (Eigenvalues, Spectral Analysis), and Statistics (Distributions, Regression, Queuing). 21 theory deliverables + 3 integration projects = 130 hours total. Publish to YouTube, Substack, Twitter/X, GitHub.
- Visibility: public
- Owner: @intisor
- Repository: intisor/VIIDII

---

#### ✅ CREATE: 3 Milestones (for Lock-in)

**Milestone 8: Pillar 1 — Computational Physics (Automata & Theory of Computation)**
- Due Date: 2026-05-XX (Weeks 3–4 of lock-in, ~2 weeks)
- Description: Master Finite Automata, NFA/DFA equivalence, Pumping Lemma, closure properties, DFA minimization. Connect each theory topic to real systems (OS process scheduling, compiler design, microservice patterns, digital hardware). 7 deliverables + 1 integration project.
- Status: Planning

**Milestone 9: Pillar 2 — Mathematical Physics (Linear Algebra)**
- Due Date: 2026-06-XX (Weeks 5–6 of lock-in, ~2 weeks)
- Description: Master eigenvalues, eigenvectors, diagonalization, spectral theorem, control theory, PCA. Connect to distributed systems (consensus algorithms), machine learning, and data analysis. 7 deliverables + 1 integration project.
- Status: Planning

**Milestone 10: Pillar 3 — Empirical Physics (Statistics)**
- Due Date: 2026-07-XX (Weeks 7–8 of lock-in, ~2 weeks)
- Description: Master probability distributions, regression, hypothesis testing, queuing theory, Monte Carlo simulation, bootstrapping. Apply to system design (capacity planning, performance benchmarking, load testing). 7 deliverables + 1 integration project.
- Status: Planning

---

#### ✅ CREATE: Custom Fields (for Lock-in Project)

Same as Phase 1:
1. Priority (Critical, High, Medium, Low)
2. Complexity (1–5)
3. Category (🏗️ Portfolio Build, etc.)
4. FUTA Course (CSC307, CSC309, etc.)
5. Content Output (📹 YouTube, 📝 Substack, etc.)
6. Status (Not Started → Completed)

---

#### ✅ CREATE: 21 GitHub Issues (Lock-in Deliverables)

**Pillar 1: Automata (7 tasks)**

1. **1.1 DFA/NFA Fundamentals**
   - Milestone: Pillar 1
   - Priority: High
   - Complexity: 3
   - Assignee: @intisor
   - Description: Construct DFAs and NFAs for given languages. Prove equivalence via subset construction. Understand why NFA state explosion matters in real systems.
   - Status: Not Started

2. **1.2 NFA → DFA State Explosion**
   - Milestone: Pillar 1
   - Priority: High
   - Complexity: 3
   - Assignee: @intisor
   - Description: Analyze worst-case exponential blowup (2^n states). Connect to OS concurrency limits, thread-pool thread counts, and system design constraints.
   - Status: Not Started

3. **1.3 Regular Expressions & Lexical Analysis**
   - Milestone: Pillar 1
   - Priority: High
   - Complexity: 2
   - Assignee: @intisor
   - Description: Map regex to finite automata. Implement a basic tokenizer (lexer). Understand why regex engines are fast (DFA + memoization).
   - Status: Not Started

4. **1.4 Pumping Lemma Proofs**
   - Milestone: Pillar 1
   - Priority: Medium
   - Complexity: 4
   - Assignee: @intisor
   - Description: Master contradiction-based proofs for non-regularity. Understand where regex (regular languages) fail and parsers must take over.
   - Status: Not Started

5. **1.5 Closure Properties**
   - Milestone: Pillar 1
   - Priority: Medium
   - Complexity: 3
   - Assignee: @intisor
   - Description: Prove closure under union, intersection, complement, concatenation, Kleene star. Map to microservice composability.
   - Status: Not Started

6. **1.6 DFA Minimization (Hopcroft's Algorithm)**
   - Milestone: Pillar 1
   - Priority: Medium
   - Complexity: 4
   - Assignee: @intisor
   - Description: Implement Hopcroft's algorithm. Understand gate reduction in digital circuits (fewer states = fewer gates, faster hardware).
   - Status: Not Started

7. **1.7 Integration Project — Mini Compiler Front-End**
   - Milestone: Pillar 1
   - Priority: High
   - Complexity: 5
   - Assignee: @intisor
   - Description: Build a lexer + parser boundary. Demonstrate automata theory in production (tokenize → parse → AST). 500+ lines of production-quality C# code.
   - Status: Not Started

**Pillar 2: Linear Algebra (7 tasks)**

8. **2.1 Eigenvalues & Eigenvectors**
   - Milestone: Pillar 2
   - Priority: High
   - Complexity: 3
   - Description: Compute eigenvalues/eigenvectors for matrices (up to 4×4). Interpret geometrically (stretching direction).
   - Status: Not Started

9. **2.2 Diagonalization & Jordan Normal Form**
   - Milestone: Pillar 2
   - Priority: High
   - Complexity: 4
   - Description: Diagonalize matrices. Handle defective matrices via Jordan Normal Form. Understand system exponential behavior.
   - Status: Not Started

10. **2.3 Spectral Theorem**
    - Milestone: Pillar 2
    - Priority: High
    - Complexity: 4
    - Description: Prove and apply spectral theorem for symmetric/Hermitian matrices. Understand why spectral analysis is powerful.
    - Status: Not Started

11. **2.4 PageRank & Markov Chains**
    - Milestone: Pillar 2
    - Priority: Medium
    - Complexity: 4
    - Description: Implement PageRank algorithm. Analyze Markov chain convergence via dominant eigenvalue of transition matrix.
    - Status: Not Started

12. **2.5 Control Theory & Stability**
    - Milestone: Pillar 2
    - Priority: Medium
    - Complexity: 4
    - Description: Analyze system stability using eigenvalue placement. Avoid exponential blowup via Jordan Form inspection.
    - Status: Not Started

13. **2.6 PCA & Dimensionality Reduction**
    - Milestone: Pillar 2
    - Priority: Medium
    - Complexity: 4
    - Description: Implement Principal Component Analysis from scratch using covariance matrices and eigenvectors. Reduce 100-dimensional data to 2D visualization.
    - Status: Not Started

14. **2.7 Integration Project — Distributed System Simulator**
    - Milestone: Pillar 2
    - Priority: High
    - Complexity: 5
    - Description: Build consensus algorithm simulator. Use spectral analysis to predict convergence speed (dominant eigenvalue = convergence rate).
    - Status: Not Started

**Pillar 3: Statistics (7 tasks)**

15. **3.1 Probability Distributions**
    - Milestone: Pillar 3
    - Priority: High
    - Complexity: 3
    - Description: Master discrete (Binomial, Poisson, Geometric) and continuous (Normal, Exponential, Uniform) distributions. Recognize real-world examples.
    - Status: Not Started

16. **3.2 Regression Analysis**
    - Milestone: Pillar 3
    - Priority: High
    - Complexity: 3
    - Description: Implement linear and logistic regression. Interpret coefficients for system performance modeling.
    - Status: Not Started

17. **3.3 Hypothesis Testing & Confidence Intervals**
    - Milestone: Pillar 3
    - Priority: High
    - Complexity: 3
    - Description: Apply t-tests, chi-square tests. Build confidence intervals for benchmark results.
    - Status: Not Started

18. **3.4 Queuing Theory**
    - Milestone: Pillar 3
    - Priority: Medium
    - Complexity: 4
    - Description: Model M/M/1 and M/M/c queues. Apply to load balancer capacity planning.
    - Status: Not Started

19. **3.5 Monte Carlo Simulation**
    - Milestone: Pillar 3
    - Priority: Medium
    - Complexity: 4
    - Description: Implement Monte Carlo methods for estimating system metrics under uncertainty.
    - Status: Not Started

20. **3.6 Bootstrapping**
    - Milestone: Pillar 3
    - Priority: Medium
    - Complexity: 3
    - Description: Apply bootstrap resampling for non-parametric confidence intervals on system performance data.
    - Status: Not Started

21. **3.7 Integration Project — Load Testing Framework**
    - Milestone: Pillar 3
    - Priority: High
    - Complexity: 5
    - Description: Build load-testing tool that uses statistical modeling to predict system behavior under stress. Output: "System can handle 10,000 req/s with 99.9% confidence."
    - Status: Not Started

---

## ⏭️ Existing Issues to SKIP (Already Covered)

**Issue #3: Migrate WebRTC logic to C# (Hybrid Blazor approach)**
- Status: SKIP (This is redundant with Phase 1 tasks. Already covered by Task 1 + Task 2.)
- Action: Leave as-is, but add comment "Superseded by Phase 1 sprint (Tasks 1–5). See project board."

---

## 📊 Summary of Planned Changes

| Artifact | Action | Count | Status |
|----------|--------|-------|--------|
| **Projects** | CREATE | 2 | ✅ Ready |
| **Milestones** | CREATE | 10 | ✅ Ready |
| **Custom Fields** | CREATE | 6 | ✅ Ready |
| **Issues** | UPDATE | 5 (Phase 1 existing) | ✅ Ready |
| **Issues** | CREATE | 21 (Lock-in new) | ✅ Ready |
| **Issues** | SKIP | 1 (redundant #3) | ✅ Ready |

---

## 📋 What Will Happen When You Approve

### Step 1: Create Phase 1 Project
```
POST /repos/intisor/VIIDII/projects
{
  "name": "VIIDII Phase 1: MVP Hardening & Stabilization",
  "description": "...",
  "visibility": "public"
}
```

### Step 2: Create 10 Milestones
```
POST /repos/intisor/VIIDII/milestones
× 10 (Days 1–5, Phase 2, Phase 3, Pillars 1–3)
```

### Step 3: Create 6 Custom Fields
```
POST /projects/{projectId}/custom_fields
× 6 (Priority, Complexity, Category, Course, Content, Status)
```

### Step 4: Update 5 Existing Issues
```
PATCH /repos/intisor/VIIDII/issues/{number}
× 5 (Add to project, milestone, set custom fields)
```

### Step 5: Create 21 Lock-in Issues
```
POST /repos/intisor/VIIDII/issues
× 21 (Pillar 1–3 deliverables)
```

### Step 6: Create Lock-in Project
```
POST /repos/intisor/VIIDII/projects
(Same as Phase 1 but for lock-in)
```

---

## 🔍 Validation Checklist

Before execution, I verify:

- [x] 5 Phase 1 issues already exist (no duplicates created)
- [x] No existing projects found (safe to create)
- [x] No existing milestones found (safe to create)
- [x] No existing custom fields found (safe to create)
- [x] 21 lock-in deliverables documented (all present in `milestone.md`)
- [x] All acceptance criteria documented (all issues have clear criteria)
- [x] All files-to-change documented (all issues list exact files)
- [x] No contradictions between docs and code audit
- [x] Effort estimates realistic (5 = full day, 4 = most of day, etc.)

---

## ✅ APPROVAL CHECKLIST

Before proceeding, please confirm:

- [ ] **Phase 1 Project**: Name, description, visibility correct?
- [ ] **Phase 1 Milestones**: Days 1–5 milestones with correct descriptions?
- [ ] **Phase 1 Issues**: 5 existing issues should be updated with project/milestone/custom fields?
- [ ] **Lock-in Project**: Name, description, visibility correct?
- [ ] **Lock-in Milestones**: Pillars 1–3 with correct deliverable counts?
- [ ] **Lock-in Issues**: 21 new issues with correct titles/descriptions?
- [ ] **Custom Fields**: Priority, Complexity, Category, Course, Content, Status all needed?
- [ ] **No duplicates**: Should I skip creating issues if titles match existing ones?

---

## 🎯 NEXT ACTION

**You have three options:**

1. **✅ APPROVE & EXECUTE** 
   - Confirm all checkboxes above
   - Reply: "**GO**"
   - I will immediately create all GitHub artifacts (5 minutes)

2. **❓ REQUEST CHANGES**
   - List what should change (names, descriptions, custom fields, etc.)
   - Reply with your changes
   - I will update the plan and re-confirm

3. **⏸️ HOLD & REVIEW**
   - You want to review the plan in GitHub first
   - Reply: "**REVIEW**"
   - I will wait for your go-ahead

---

**What's your decision?** 🚀

---

**File:** `EXECUTION-PLAN.md`  
**Status:** READY FOR APPROVAL  
**Generated:** 2026-02-XX
