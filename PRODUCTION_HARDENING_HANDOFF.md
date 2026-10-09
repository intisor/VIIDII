# VIIDII — PRODUCTION HARDENING HANDOFF

**Date:** October 2026
**Location:** `C:\Users\HomePC\Desktop\Coded\VIIDII`
**Status:** Working prototype → production candidate
**Supersedes:** Nothing. The `viidii-platform` strategy documents remain useful as market research only.

---

## 0. Read This First — Why We Are Here

The product was correct from the beginning: **a live virtual classroom with real video and precise, durable attendance measurement.**

For several months the strategy drifted into "cheaper Zoom for Africa," then into selling compliance software to regulators. Both were dead ends:

- **Cheaper Zoom** — nobody switches from Zoom to save data cost. Absent video, students don't attend.
- **Regulator compliance** — TRCN, MDCN, NBA, and ICAN have **already built their own platforms.** TRCN runs `mcpd.trcn.gov.ng` with 1.63M teachers, 150+ providers, and an 85% completion rate. This market is solved.

**The actual market:** post-secondary institutions running large remote cohorts (100–500 students per lecturer), where a physical register is impossible and attendance evidence is genuinely hard to produce.

**Everything already exists and works:** WebRTC video, SignalR presence, EF Core persistence, attendance scoring, migrations, tests.

**The job now is not to rebuild. It is to harden.**

### Do not rebuild in `viidii-platform`

That folder contains nine strategy documents and `Weather.razor`. The handoff doc there says "fresh solution only; do not port the old app." **That instruction was wrong and should be disregarded.** Keep that folder only as an archive of market research.

---

## 1. What Already Works — Do Not Break This

| Area | Location | Status |
| :--- | :--- | :--- |
| WebRTC video + screen share | `wwwroot/js/sessionInterop.js` | Working, PeerJS mesh |
| Camera test / mobile play overlay | `Components/Pages/SessionView.razor` | Working |
| SignalR presence hub | `Hubs/SessionHub.cs` | Working |
| Durable attendance scoring | `Services/SessionService.cs` (~line 458+) | Working, recomputable from DB |
| Persistence | `Data/ViidiiDbContext.cs`, `Migrations/` | Working, SQL Server |
| Repositories | `Services/SessionRepository.cs`, `MessageRepository.cs` | Working |
| Messaging | `Services/MessageService.cs`, `MessageRepository.cs` | Working |
| Attendance scoring tests | `VIIDII.Tests/Services/SessionServiceTests.cs` | Working, valuable |

**The attendance scoring engine is the most valuable asset in this repository.** It walks persisted attendance logs, credits time per participant by status, and produces a percentage that survives restarts. Protect it.

---

## 2. Blocking Issues — Fix In This Order

### Phase 0 — VALIDATE THE MARKET (do this before writing code)

**Five phone calls. This month. Before Phase 1.**

Target: Directors of Distance Learning, Deans of Student Affairs, and heads of e-learning at Nigerian universities, polytechnics, and private universities running large-cohort online programmes.

The question:

> **"You have 200 students in a streamed lecture. How do you prove who actually attended?"**

| Answer | Action |
| :--- | :--- |
| Spreadsheet, Zoom export, or "we really don't" | **Proceed with Phase 1.** |
| "We already have a system" | Stop. Do not build. |
| Nobody cares | Stop. Do not build. |

**Phases 1–4 below assume Phase 0 returned a problem. Do not skip it.**

---

### Phase 1 — AUTHENTICATION (critical blocker)

**The problem:** `Program.cs` imports `Microsoft.AspNetCore.Identity` but **never calls `AddAuthentication`, `AddAuthorization`, `UseAuthentication`, or `UseAuthorization`.** `AuthService` verifies a password hash and writes session state manually. There is **no auth pipeline and no `[Authorize]` attribute anywhere.**

**Current state: every page is reachable without logging in.** This is not weak auth — it is absent auth.

Additionally:
- `Data/DatabaseSeeder.cs` creates test users on every startup
- `TEST_CREDENTIALS.md` sits in the repository root
- `VIIDII.Services` and `VIIDII.Tests` have no role enforcement

**Tasks:**

1. Add cookie authentication properly:
   ```csharp
   builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
       .AddCookie(options => { options.LoginPath = "/login"; });
   builder.Services.AddAuthorization();
   ```
2. Add `app.UseAuthentication()` and `app.UseAuthorization()` **before** `MapRazorComponents`.
3. Build a real identity: `HttpContextAccessor` → `ClaimsPrincipal` → `GetUserId()` extension.
4. Add `[Authorize]` to every session page. Add role checks for lecturer vs student.
5. Verify identity **inside `SessionHub`** — never trust a matric number passed from the client.
6. Guard the seeder behind `app.Environment.IsDevelopment()`.
7. Delete `TEST_CREDENTIALS.md`. Add seeded credentials to `.gitignore` and a local-only secrets file.
8. Set `options.Cookie.SameSite`, `SecurePolicy = Always` in production.

**Done when:** an unauthenticated request to `/session/{id}` returns a redirect to `/login`, and a student cannot reach lecturer-only actions.

---

### Phase 2 — TURN SERVER (video reliability)

**The problem:** `wwwroot/js/sessionInterop.js` uses **free public relay servers**:
- `turn:openrelay.metered.ca` (shared credentials, metered, rate-limited)
- `turn:freestun.net`

These are rate-limited by design and will throttle or drop you the moment real students connect. This is the most likely reason video "worked for me but not for students."

**Additionally:** many Nigerian mobile networks block UDP and unencrypted TURN. **TURN must be served over TLS on port 443.**

**Tasks:**

1. Deploy **coturn** on a VPS (Nigeria region preferred).
2. Configure TLS listener on port 443 (`lt-cred-mech`, `use-auth-secret`, `static-auth-secret`).
3. Provision credentials via time-limited HMAC secret, not static usernames.
4. Replace the ICE server list in `sessionInterop.js` with your own, injected via configuration — **not hardcoded in JS.**
5. Add `iceTransportPolicy: "all"` so relay candidates are reachable behind strict NAT.
6. Load-test with 30+ concurrent connections.

**Done when:** video connects reliably from a real Nigerian mobile network, on a bad connection, without relay exhaustion.

---

### Phase 3 — ONE-TO-MANY STREAMING (scales past ~6 people)

**The problem:** the current architecture is a **PeerJS mesh** — every student connects to every other student. At 6 students this works. At 40, each student uploads 40 times and the lecturer's uplink collapses. **This is the single biggest technical obstacle to the large-cohort thesis.**

A 200-student cohort is arithmetically impossible as a mesh.

**Tasks:**

1. **Do not hand-roll an SFU.** Adopt one:
   - **LiveKit** (recommended) — Go, self-hostable, client SDKs for JS/.NET, built for exactly this
   - **mediasoup** — lower-level, more control, more work
   - **Janus** — mature, WebRTC gateway
2. Run the SFU on your own VPS (Nigeria region — latency matters more than bandwidth here).
3. Replace the mesh in `sessionInterop.js` with the SFU client. **Keep the Blazor/SignalR layer for presence and messaging** — that part is good.
4. **Keep the one-to-many pattern** — lecturer publishes one stream, students subscribe. This is what makes bandwidth work: 1 upstream instead of 200.
5. Preserve `SessionState` and `ParticipantPanel` — presentation is independent of transport.
6. Add a hard participant cap per session with a clear UI message.

**Design principle to hold onto:** *the meeting is transient, the record is durable.* If the SFU goes down, the attendance evidence survives.

---

### Phase 4 — STABILITY AND OPERATIONS

**Tasks:**

1. **Fix DI lifetimes.** `SessionService`, `PasswordHasher<User>`, and `IServiceProvider` are registered as **singletons** while depending on scoped services. This causes memory leaks and cross-request state bleed.
   ```csharp
   // Currently WRONG
   builder.Services.AddSingleton<SessionService>();
   builder.Services.AddSingleton<IServiceProvider>(sp => sp);

   // Correct
   builder.Services.AddScoped<SessionService>();
   // Remove the IServiceProvider singleton entirely
   ```
   This also removes the runtime service-locator anti-pattern in `SessionService`.

2. **Replace `Console.WriteLine` with structured logging.** `SessionService.cs` logs heavily to console. Use `ILogger<T>` with levels; console output is invisible in production.

3. **Externalize configuration.** Move connection strings out of `appsettings.json` into environment variables / user secrets. Enforce HTTPS with HSTS. Add a production `appsettings.Production.json` with no secrets.

4. **Database.** Move from SQL Server LocalDB to PostgreSQL or SQL Server on a real server. Confirm `ViidiiDbContextModelSnapshot.cs` regenerates cleanly. Add backups.

5. **Expand tests.** Current coverage is thin — two test files. Expand `SessionServiceTests`, especially:
   - attendance scoring edge cases (zero-duration sessions, mid-session joins, grace-period reconnect)
   - **authorization tests** — student cannot reach lecturer actions
   - hub authorization — unauthenticated client cannot join a session hub group

6. **Observability.** Add health checks (`/health`), structured request logging, and error tracking.

---

## 3. Production Readiness Checklist

Do not claim production readiness until all are true:

- [ ] Every page requires authentication
- [ ] Role-based access control enforced server-side, including in the SignalR hub
- [ ] No seeded test users in production
- [ ] No credentials in the repository
- [ ] Own TURN server on TLS/443
- [ ] SFU replaces mesh; 30+ concurrent connections verified
- [ ] DI lifetimes corrected
- [ ] Structured logging, no `Console.WriteLine`
- [ ] Secrets externalised
- [ ] Database on a real server with backups
- [ ] Attendance scoring tests expanded and passing
- [ ] One real lecturer has run one real cohort and opened the recap

---

## 4. Market Reference (from `viidii-platform/docs`)

Useful research, retained as background:

| Document | Content |
| :--- | :--- |
| `MARKET_INTELLIGENCE.md` | Nigeria TVET biometric mandate (Feb 2026), ICAN CPD, ITF levy |
| `ATTENDANCE_MANDATE_MAP.md` | Tier 1 (mandatory, verifiable) vs Tier 2 (list is enough) |
| `COMPETITIVE_LANDSCAPE.md` | SchoolOS at ₦1,000/student/term, CertTrack, Treinig, Notesia, Indelo, CLE Hero |
| `TARGET_INSTITUTIONS_AND_NETWORK_PLAN.md` | 39 TRCN-accredited MCPD providers (not our market — physical attendance) |

**Competitive position:** every feature in this product has a shipped equivalent somewhere — QR verification, certificates, audit trails, offline-first, rostering. SchoolOS, ShuleOS and Indelo serve African schools at low prices.

**Post-secondary large-cohort distance delivery was not found to have a strong incumbent.** That is the opening. It is a hypothesis, not a finding.

---

## 5. Explicit Non-Goals

Do not build these:

- ❌ In-house SFU (use LiveKit/mediasoup/Janus)
- ❌ Compliance/regulator products (TRCN, MDCN, NBA, ICAN have solved this)
- ❌ Payments (not until a paying customer exists)
- ❌ AI features
- ❌ A second client platform in `viidii-platform`

---

## 6. Success Criteria for the Next 30 Days

| # | Criterion |
| :--- | :--- |
| 1 | Five university conversations completed; at least one confirms a real attendance-proofing problem |
| 2 | Auth pipeline implemented; unauthenticated access blocked everywhere including the hub |
| 3 | Own TURN server live on TLS/443 |
| 4 | SFU integrated; 30 concurrent connections verified |
| 5 | One real lecturer ran one real cohort of 50+ students and used the recap |

**If criterion 5 does not happen, nothing else matters.**

---

## 7. Notes for the Next Agent

1. **Do not rebuild.** This repository works. Extend it.
2. **Call universities before writing code.** Five phone calls. Spreadsheets and manual exports are the manual incumbent — confirm that.
3. **Fix auth first.** It is currently absent, not weak.
4. **Buy your own TURN.** Public relays will fail under real load.
5. **Do not hand-roll media infrastructure.** The mesh will not reach a real cohort.
6. **Protect the attendance scoring engine.** It is the differentiator and it has tests.
7. **Do not rebuild trust on fabricated numbers.** The `FEASIBILITY_AND_MARKET_STUDY.md` in `viidii-platform` contains invented feasibility scores (9.2/10, 9.5/10). Never reuse them.