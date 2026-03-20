# VIIDII Documentation: Source of Truth

**Status:** Consolidated  
**Last Updated:** 2026-02-XX  
**Decision:** Two files are now the authoritative project plan

---

## Consolidated Project Files

### 1. **`docs/CURRENT-STATE.md`** 
**Purpose:** Audit of what's actually implemented vs. what's broken.

**When to read:**
- Understanding the MVP baseline
- Identifying bugs and gaps
- Understanding why Phase 1 tasks exist

**Key sections:**
- ✅ What's implemented (DFA model, SignalR hub, engagement tracking, UI)
- ❌ What's missing (JS DFA integration, Sapa Mode, database, LAN mode, QR attendance)
- Known bugs & warnings (ghost calls, thread-pool starvation, data loss on restart)
- Dependency map showing why tasks must run in order

---

### 2. **`docs/PROJECT-ROADMAP.md`** 
**Purpose:** Detailed actionable plan for Phase 1 (5-day sprint) and 2026 lock-in (21 deliverables).

**When to read:**
- Planning Phase 1 work
- Creating GitHub Issues
- Tracking sprint progress
- Understanding acceptance criteria

**Key sections:**
- Phase 1: 5 tasks (Days 1–5) with full technical details
- Each task: "What's broken", "What we build", "Files to change", "Acceptance criteria"
- Phase 2/3 roadmap (post-Phase 1)
- 2026 lock-in: 21 theory + integration deliverables

---

## Archived / Deprecated Files

The following files are **SUPERSEDED** by the two files above. They may contain outdated info:

- ❌ `docs/PRD.md` → Superseded by `CURRENT-STATE.md` + `PROJECT-ROADMAP.md`
  - Old PRD listed Phase 1 tasks in scattered fashion
  - New roadmap is more detailed + organized by day
  
- ❌ `docs/Phase1-Project-Plan.md` → Superseded by `PROJECT-ROADMAP.md` (Day 1–5 section)
  - Old plan didn't account for Task 1 (DFA) being partially implemented
  - New roadmap reflects actual code state
  
- ❌ `docs/2026 lockin project/PLAN.md` → Superseded by `PROJECT-ROADMAP.md` (Pillar 1–3 section)
  - Consolidated into single document to avoid fragmentation
  
- ❌ `docs/2026 lockin project/MILESTONES.md` → Superseded by `PROJECT-ROADMAP.md` (Pillar 1–3 tables)
  - Milestone status now tracked in roadmap

- ❌ `docs/Feasibility-Study.md` → Reference only (still valid for architectural background)

---

## How to Use This Repo Moving Forward

### For Development Work:
1. **Understand current state:** Read `CURRENT-STATE.md` once to know what exists.
2. **Plan Phase 1:** Read `PROJECT-ROADMAP.md` → Phase 1 Tasks section.
3. **Create issues:** Use `PROJECT-ROADMAP.md` as the source for GitHub Issues.
4. **Track progress:** Update issue statuses as you complete each day.

### For New Contributors:
1. Read `CURRENT-STATE.md` (5 min) to understand what's built.
2. Read `PROJECT-ROADMAP.md` (10 min) to understand the plan.
3. Grab a Day 1–5 task from the roadmap.
4. Follow the "Files to change" list.
5. Verify against "Acceptance criteria".

### For Stakeholders / Lecturers:
- Read `PROJECT-ROADMAP.md` → "Phase 1 Summary" table (quick status overview).
- Read "Success Metrics" → what VIIDII will be able to do after Phase 1.

---

## GitHub Issues: Mapping

When we generate GitHub Issues from `PROJECT-ROADMAP.md`, they'll be organized as:

```
Project: "VIIDII Phase 1: MVP Hardening"

Issues:
  ├─ Day 1: Complete WebRTC State Machine JS Integration
  │   ├─ Priority: Critical
  │   ├─ Effort: 5
  │   ├─ Category: 🏗️ Portfolio Build
  │   ├─ FUTA Course: CSC309
  │   └─ Content Output: 📹 YouTube/Twitch
  ├─ Day 2: SignalR Optimization + Sapa Mode
  ├─ Day 3: EF Core + PostgreSQL
  ├─ Day 4: Zero-App LAN Data Channel + Catch-Up Protocol
  └─ Day 5: Dynamic Cryptographic QR Attendance

Project: "2026 Lock-in: Systems Architect Foundation"

Milestones:
  ├─ Milestone 1: Automata & Theory of Computation
  │   ├─ 1.1 DFA/NFA Fundamentals
  │   ├─ 1.2 NFA → DFA State Explosion
  │   ... (7 per milestone)
  ├─ Milestone 2: Linear Algebra
  │   ├─ 2.1 Eigenvalues & Eigenvectors
  │   ... (7 per milestone)
  └─ Milestone 3: Statistics
      ├─ 3.1 Probability Distributions
      ... (7 per milestone)
```

---

## Quick Reference: File Modifications per Phase 1 Task

### Day 1: DFA JS Integration
- NEW: `Services/PeerStateService.cs`
- MODIFY: 6 files (SessionService, SessionHub, session.js, SessionJsInterop, ParticipantPanel, Program.cs)

### Day 2: SignalR Optimization + Sapa Mode
- NEW: `Hubs/MatricNoCachingFilter.cs`, `Services/QualityService.cs`
- MODIFY: 10 files (SessionHub, session.js, SessionJsInterop, VideoStage, ParticipantPanel, IssueButtons, Program.cs, etc.)

### Day 3: EF Core + PostgreSQL
- NEW: 16 files (Repositories, DbContext, Models, Migrations, Seeder)
- MODIFY: 8 files (Hub, Service, Components, Program.cs, appsettings)

### Day 4: LAN Mode + Catch-Up
- NEW: `Hubs/PeerSignalingHub.cs`
- MODIFY: 6 files (session.js, SessionJsInterop, ControlsBar, MessagingPanel, SessionHub, Program.cs)

### Day 5: QR Attendance
- NEW: `Services/AttendanceTokenService.cs`, `Components/Pages/AttendanceScan.razor`, migration
- MODIFY: 5 files (LecturerSessionView, SessionRecap, Program.cs, appsettings)

---

## Next Step

**All documentation is consolidated and ready.** 

Next action: Use `PROJECT-ROADMAP.md` to generate 5 GitHub Issues (one per day) + 1 GitHub Project for Phase 1, and 1 GitHub Project + 3 Milestones for the 2026 lock-in.

Ready to proceed with GitHub automation?

---

**Approval:** ✅ Documentation consolidated. Ready to create GitHub Issues.
