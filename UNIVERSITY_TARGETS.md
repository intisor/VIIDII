# VIIDII — NIGERIAN UNIVERSITY TARGETS & DIRECT COMPETITOR FINDING

**Date:** October 2026
**Companion to:** `PRODUCTION_HARDENING_HANDOFF.md`
**Status:** Target list compiled from NUC primary source. One direct competitor found.

---

## 1. READ THIS FIRST — There Is a Direct Competitor

**Swiftke (Swiftke Education Technologies Limited)** — Apapa, Lagos.

> *"The LMS built for Nigeria's universities. Course management, live lectures, grading, attendance tracking, and NUC reporting. Works on 3G. Faculty live in 48 hours. Priced in naira."*

**This is your product, already built, already targeting your exact market, already priced in naira.**

Their positioning, quoted from `swiftke.org`:

| Their claim | Your position |
| :--- | :--- |
| "Blackboard is too expensive" | Same argument |
| "Moodle requires IT staff" | Same argument |
| "Most universities use WhatsApp" | Same assumption you had |
| "Works on 3G bandwidth" | Your original bandwidth insight — **already claimed** |
| "Naira pricing, per student" | Ahead of you on localisation |
| "Faculty live in 48 hours" | Faster than any plan in your docs |
| "Attendance tracking + NUC reporting" | Your PRD's core features |

**Additional competitive detail:**
- Registered company: **Swiftke Education Technologies Limited**, 4 Creek Road, Apapa, Lagos 104101
- Targets: Federal, State, and Private Universities, Polytechnics, Colleges of Education
- Onboarding: 1-week go-live, no IT staff required, 2-hour faculty training
- Claims compliance with **NDPA 2023** and NUC reporting frameworks
- Cites an **April 2026 NUC strategic engagement with Anthology Blackboard** as market validation
- No public pricing, no email, no phone — demo request form only

**Consequence: the "no one is doing this" position is dead.** It was already dead — SchoolOS, CertTrack, Treinig, Indelo, CLE Hero — and now there is a *local* competitor with your exact thesis.

**What survives:** you still have a working platform and a year of code they don't have. But your differentiation cannot be "built for Nigerian universities" or "works on 3G." Both are claimed.

---

## 2. Where They Are Weak — The Only Openings

Swiftke's public messaging is polished marketing, so treat this as hypotheses, not verified weaknesses:

| Possible gap | Why it might be real | How to test |
| :--- | :--- | :--- |
| **No live video details** | Site mentions "live lectures" but no SFU, no bandwidth figures, no cohort limits | Ask directly on a demo |
| **No public pricing** | Forces a sales conversation before any price anchoring | Ask what a 500-student cohort costs per year |
| **"Live & recorded lectures" may mean recordings** | If they host pre-recorded video rather than live sessions, they don't compete with your live classroom | Ask: live or on-demand? |
| **Attendance tracking may be Moodle-grade** | LMS-native attendance = "student clicked the course" | Ask how they prove *duration* and *identity* |
| **Imported cohorts** | They migrate data from your student registry — implies they inherit your roster, not verify identity | Ask whether they verify who actually attended |
| **Demo-only funnel** | No phone, no email on the site | Just a signal about maturity |

**Your durable differentiation, if any:** *live* sessions with *verified identity* and *duration-based* evidence — not click-tracking in a course page. That is what your attendance scoring engine actually does, and it is measurably stronger evidence than LMS attendance.

---

## 3. The Official Target List — 34 NUC-Approved Distance Learning Centres

Source: **National Universities Commission** — `nuc.edu.ng/distance-learning-centers/` (primary, retrieved Oct 2026)

This is the authoritative list of institutions with approved open and distance learning programmes. **These are the only universities that can legitimately sell distance programmes** — a genuine, verifiable filter.

| # | University | Location | ODL Centre |
| :--- | :--- | :--- | :--- |
| 1 | University of Ibadan | Ibadan | Distance Learning Centre |
| 2 | Obafemi Awolowo University | Ile-Ife | Centre for Distance Learning |
| 3 | **University of Lagos** | Akoka | Distance Learning Institute |
| 4 | University of Maiduguri | Maiduguri | Centre for Distance Learning |
| 5 | Modibbo Adama University of Technology | Yola | Centre for Distance Learning |
| 6 | **University of Abuja** | Abuja | Centre for Distance Learning & Continuing Education |
| 7 | Ladoke Akintola University of Technology | Ogbomoso | LAUTECH Distance Learning Centre |
| 8 | **Ahmadu Bello University** | Zaria | Distance Learning Centre |
| 9 | Lagos State University | Lagos | Open & Distance Learning and Research Institute |
| 10 | Joseph Ayo Babalola University | University | JABU Center for Distance Learning |
| 11 | **University of Nigeria** | Nsukka | UNN Center for Distance & e-Learning |
| 12 | Federal University of Technology | Minna | Center for Open Distance & e-Learning |
| 13 | Babcock University | Ilishan | Center for Open Distance & e-Learning |
| 14 | University of Ilorin | Ilorin | Center for Open & Distance Learning |
| 15 | Afe Babalola University | Ado-Ekiti | Open & Distance Learning Centre |
| 16 | Olabisi Onabanjo University | Ago-Iwoye | Open & Distance Learning Centre |
| 17 | Ignatius Ajuru University of Education | Port Harcourt | Institute of Distance Education |
| 18 | Usmanu Danfodio University | Sokoto | Centre for Open & Distance Education (CODE) |
| 19 | Enugu State University of Science & Technology | Enugu | Open & Distance Learning Centre, Agbani |
| 20 | University of Benin | Benin | Centre for Distance Learning |
| 21 | University of Calabar | Calabar | Centre for Open Distance & e-Learning |
| 22 | University of Port Harcourt | Port Harcourt | Centre for Open Distance & e-Learning |
| 23 | Igbinedion University | Okada | Centre for Open & Distance Learning |
| 24 | Mountain Top University | — | Open & Distance Learning Centre |
| 25 | Federal University of Technology | Akure | Open & Distance Learning Centre |
| 26 | Covenant University | Ota | Centre for Open Distance & e-Learning |
| 27 | Nasarawa State University | Keffi | Centre for Open Distance & e-Learning |
| 28 | Delta State University | Abraka | Centre for Open Distance & e-Learning |
| 29 | Nile University | Abuja | Centre for Distance Learning |
| 30 | Federal University | Oye-Ekiti | Centre for Distance Learning Institute |
| 31 | Bayero University | Kano | Centre for Distance Learning |
| 32 | Al-Hikmah University | Ilorin | Centre for Distance Learning |
| 33 | Thomas Adewunmi University | Ede | Centre for Distance Learning |
| 34 | Caleb University | Abuja | Centre for Distance Learning |

**Plus NOUN** — the National Open University of Nigeria, separate ODL institution, not on the list above.

---

## 4. Priority Targets — Tier 1 (start here)

Ranked by likely size, distance-learning maturity, and accessibility.

### 1. NOUN — National Open University of Nigeria ⭐ **Highest scale by far**

- **~23,885 students matriculated in one intake** (31st matriculation, Oct 2026); 23,344 in the 2025 intake
- **Nigeria's dedicated open-and-distance institution** — distance learning is the entire business model, not a side programme
- Runs a **blended LMS-based synchronous and asynchronous delivery** model
- Has published a **Virtual Learning Environment Policy**
- 1.63M registered teachers context: MCPD runs through TRCN, but NOUN's own course delivery is separate

**Contact (published):**
- **Address:** University Village, Plot 91, Cadastral Zone, Nnamdi Azikiwe Expressway, **Jabi, Abuja**
- **Central email:** `centralinfo@noun.edu.ng`
- **Call centre:** +234 803 851 4090 · +234 908 290 7092 · +234 905 646 3036 · +234 807 991 7938
- **Student support:** `support.nou.edu.ng`
- **Web:** nou.edu.ng

**Why first:** at this scale, attendance evidence is not a nice-to-have — it's an operational necessity. And NOUN is publicly reachable *today*.

**The question:** *"With 23,000 students in blended delivery, how do you record who actually attended a synchronous session?"*

### 2. University of Lagos — Distance Learning Institute

- Lagos, largest city, most visible brand
- Has a dedicated **Distance Learning Institute** (dli.unilag.edu.ng)
- VC has publicly advocated for expanded distance learning
- Runs `vlearn.unilag.edu.ng` — e-learning portal
- **Social:** @UnilagNigeria (X) · communicationunit@unilag.edu.ng

**Why:** a successful pilot here is a reference for every other Nigerian university.

### 3. Ahmadu Bello University — Distance Learning Centre

- Current director: **Prof. Muhammed Bashir Mu'azu**, appointed as a "digital education innovator"
- Large federal university, large enrolments
- Publicly documented digital education leadership

**Why:** an explicitly digital-forward director is a better first call than a generic admin.

### 4. University of Nigeria, Nsukka — CDeL

- Explicit "working 9-to-5 shouldn't put your life on hold" positioning
- Active online marketing, modern operation
- Enrols working professionals in part-time MBA

**Why:** professional cohort = attendance matters more, and they'll feel the pain fastest.

### 5. University of Abuja — Centre for Distance Learning

- Runs a **dual-mode** institution with virtual learning
- **Facebook:** OfficialUniAbujaCDLCE (11,455 followers, actively posting)

**Why:** the Facebook page is a direct route to the centre's staff. Smallest effort to make contact.

---

## 5. Tier 2 — Second Wave

| University | Why | Contact route |
| :--- | :--- | :--- |
| Obafemi Awolowo University | Large federal, long-running CDL | oaucdl.edu.ng |
| University of Ilorin | Large, active CODL | uilcodl.unilorin.edu.ng |
| Babcock University | Private, digital-mature | cdel.unn.edu.ng pattern differs — verify |
| Covenant University | Private, well-resourced | — |
| Lagos State University | Fast-growing, own ODL institute | — |
| University of Port Harcourt | Large southern catchment | — |
| University of Ibadan | Oldest, largest | — |
| Modibomo Adama Univ (MAUTECH) | Tech focus, tech-friendly | — |
| Federal University of Technology Minna | Tech focus | — |
| Ignatius Ajuru Univ of Education | Teacher training — large cohorts | — |

---

## 6. Also Worth Contacting

**National Universities Commission (NUC)**
- Publishes e-learning guidelines for Nigerian universities (April 2023, revised)
- Maintains the approved distance learning centre list
- Held a strategic engagement with Anthology Blackboard (April 2026)
- Contact: `nuc.edu.ng` · Victoria Island, Abuja

**Why:** NUC sets the direction. Guidelines mandating engagement reporting would create demand for every institution at once — and NUC is the one body that could make your market mandatory rather than optional.

**Ask NUC:** *"What engagement evidence are institutions required to produce for quality assurance? And are you seeing demand for tools that verify participation rather than just course completion?"*

That single answer tells you whether a regulatory market exists — the thing I could not find anywhere else.

---

## 7. The Cold Email / Letter

### To a Distance Learning Centre

**Subject:** Question about synchronous session attendance at [University Name]

> Dear [Director],
>
> I'm researching how Nigerian universities handle one specific problem and your centre is the right place to ask.
>
> With cohorts this size, when a lecturer runs a **synchronous** online session, how do you record **who actually attended** — and for how long? I'm particularly interested in whether it's reliable enough to stand up to a dean asking for evidence, or whether it's closer to "the student opened the course page."
>
> I'm not selling anything. I'm trying to work out whether this is a real problem or a solved one, and where the honest answer is "it's handled."
>
> Happy to send a 4-minute demo if it's useful. Equally happy to just have the answer.
>
> [Name]
> [Phone]

**Why this works:** it asks a question they can answer in 30 seconds, concedes the possibility that it's solved, and offers to leave if it is. That reads as research, not sales — and research gets replies.

### Follow-up to NOUN specifically

> Dear NOUN Distance Learning Team,
>
> NOUN matriculated over 23,000 students in this year's intake. At that scale, I'm curious how synchronous session attendance is recorded — is it real-time presence with timing, or completion of asynchronously delivered material?
>
> Building a tool that produces **audit-grade** attendance evidence for large cohorts on low-bandwidth networks. Not asking for a pilot — I'd just like to understand the current practice.
>
> [Name]

---

## 8. Revised Strategy Given Swiftke

| Before | Now |
| :--- | :--- |
| "Nobody is doing this" | **Swiftke is doing this, in Lagos, right now** |
| "Our bandwidth insight is unique" | **Swiftke advertises "works on 3G"** |
| "We're first" | **We are not first, locally or globally** |
| "Sell to universities" | **Still correct — but you are entering a contested market** |

### What this means for you

1. **Your hard problem is unchanged.** Five conversations, ask how attendance is proved. Still worth doing.
2. **Add a sixth question: "Have you looked at Swiftke?"** What they say tells you whether there's room.
3. **Your differentiator must be evidence quality.** LMS attendance = "opened the page." Your scoring engine = verified identity, duration, per-status credit, durable across restarts. That is measurably stronger and regulators care about the difference.
4. **Consider being a specialist where Swiftke is general.** They sell an LMS. You could sell **live-session evidence**. A narrower pitch lands harder.
5. **NOUN is your best shot** — public contact, enormous scale, distance learning is the core business rather than a side programme.

### And the honest risk

You are a solo developer with a year-old prototype entering a market with a funded local competitor with faster onboarding and a cleaner pitch. **Swiftke's "live in 48 hours" beats any timeline in your documents.**

The realistic paths:
- **Beat them on evidence quality** with live, verified, duration-based attendance — requires finishing auth, TURN, and the SFU work first
- **Partner rather than compete** — they need a media layer; you have one
- **Find a niche they don't serve** — very large cohorts, or specialised evidence requirements

---

## 9. Source Notes

- **NUC distance learning centre list: PRIMARY.** Retrieved directly from `nuc.edu.ng/distance-learning-centers/`, October 2026. 34 entries captured verbatim.
- **NUC e-learning guidelines (April 2023, revised):** `nuc.edu.ng/wp-content/uploads/2024/06/Guidelines-for-e-Learning-in-Nigerian-Universities-April-2023-Revised-FINAL.pdf`
- **NOUN matriculation figures:** multiple independent outlets, Oct 2025 and Oct 2026, consistent at ~23,000–24,000 per intake.
- **NOUN contact details:** published on noun.edu.ng and foc.nou.edu.ng.
- **Swiftke:** all content from `swiftke.org` (retrieved Oct 2026). **Marketing claims — unverified.** No public pricing, email, or phone. Named director and live student counts on their site were not independently confirmed.
- **NUC/Blackboard engagement (April 2026):** cited by Swiftke, **not independently verified.** Verify before repeating to anyone.
- Contact details change. **Verify before use.**
- Absence of contact routes for Tier 2 universities means "not retrieved," **not** "does not have one."