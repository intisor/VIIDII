# VIIDII — TURN SERVER SETUP & MEDIA NOTES

**Date:** October 2026
**Status:** Code side complete. Infrastructure not yet provisioned.
**Cost so far: £0 / $0.** Everything in this document is preparation.

---

## 1. What Changed in Code

The ICE server list is no longer hardcoded in JavaScript. It is fetched at runtime.

**Before:** `wwwroot/js/sessionInterop.js` contained literal `openrelay.metered.ca` and `freestun.net` credentials, duplicated in two places.

**Now:**

```
window.VIIDII_ICE_SERVERS   (highest priority, if injected)
      ↓
GET /api/config/ice        (normal path)
      ↓
public relays             (development fallback, with a console warning)
```

**New files:**
- `Services/TurnServerOptions.cs` — config binding + `TurnCredentialGenerator`
- `GET /api/config/ice` endpoint in `Program.cs`
- `VIIDII.Tests/Services/TurnCredentialGeneratorTests.cs` — 7 tests

**Credentials are time-limited.** The endpoint issues ephemeral TURN credentials via the TURN REST API scheme: username = expiry timestamp, credential = `base64(HMAC-SHA1(secret, username))`. This means no long-lived TURN password is ever shipped to a browser.

**`iceTransportPolicy: "all"`** is now set explicitly so relay candidates stay reachable behind symmetric NAT.

---

## 2. Why You Need Your Own TURN Server

The public relays currently hardcoded are **rate-limited shared infrastructure**. They will work for you testing with three phones and fail when 40 real students connect. That failure looks like "random video problems" — the hardest kind to diagnose.

Specific problems with the current setup:

| Problem | Consequence |
|---|---|
| `openrelay.metered.ca` shared credentials | Throttled, then blocked |
| `freestun.net` free tier | Same |
| Plain `turn:` on port 80/3478 | Blocked by many Nigerian mobile networks |
| No TURN over TLS on 443 | Fails on corporate/hotspot networks |

**TURN over TLS on port 443 is the important one.** It looks like ordinary HTTPS traffic and is almost never blocked.

---

## 3. Provisioning coturn — When You Decide To

Estimated cost: **$5–10/month** for a small VPS. Nigeria-region preferred for latency (Lagos, or any West African host).

> This is the first thing that costs money. Everything up to here has been free. Get a real class running on the public relays first — that tests the product. Buy infrastructure when you know you need it.

### 3.1 Install

```bash
sudo apt update && sudo apt install -y coturn
```

### 3.2 Generate a shared secret

```bash
openssl rand -base64 32
```

Save it. It must match `TurnServers:SharedSecret` in the app.

### 3.3 Configure `/etc/turnserver.conf`

```conf
listening-port=3478
tls-listening-port=443
fingerprint

# Shared-secret mode (matches the TURN REST API scheme the app implements)
use-auth-secret
static-auth-secret=PASTE_YOUR_GENERATED_SECRET

realm=turn.example.com

# TLS cert — required for the 443 listener
cert=/etc/letsencrypt/live/turn.example.com/fullchain.pem
pkey=/etc/letsencrypt/live/turn.example.com/privkey.pem

# Relay address: must be the PUBLIC IP of this VPS
external-ip=YOUR_PUBLIC_IP

# Broad UDP range for relay ports
min-port=49160
max-port=49200

# Logging
no-multicast-peers
no-cli
```

**Open the ports:**

```bash
sudo ufw allow 3478/tcp
sudo ufw allow 3478/udp
sudo ufw allow 443/tcp
sudo ufw allow 49160:49200/udp
```

The UDP relay range is often forgotten and is a common cause of "TURN configured but video still fails."

```bash
sudo systemctl enable coturn
sudo systemctl restart coturn
sudo systemctl status coturn
```

### 3.4 Point the app at it

`appsettings.Production.json` — or better, environment variables:

```bash
export TurnServers__SharedSecret="your-secret"
export TurnServers__Servers__0__Url="turn:turn.example.com:443"
export TurnServers__Servers__0__Urls__0="turn:turn.example.com:443"
export TurnServers__Servers__0__Urls__1="turns:turn.example.com:443"
export TurnServers__Servers__0__CredentialTtlHours="12"
```

**Note the double underscore** — that is .NET's config nesting separator for arrays and sections.

### 3.5 Verify

```bash
# Should return JSON with your servers, usingPublicFallback: false
curl -k https://localhost/api/config/ice
```

If `usingPublicFallback` is `true`, config is not binding. If `SharedSecret` is wrong, cotturn rejects the credential and video fails only on restrictive networks — the most confusing possible failure.

---

## 4. The Mesh Problem — Not Solved by TURN

**Fixing TURN does not fix scale.** These are separate problems.

PeerJS builds a **mesh**: every student connects to every other student.

| Students | Connections per student | Lecturer uploads video |
|---|---|---|
| 4 | 3 | 4× |
| 8 | 7 | 8× |
| 20 | 19 | **20×** |
| 40 | 39 | **collapses** |

At 40 students this is arithmetically impossible on any uplink.

**The fix is one-to-many:** the lecturer publishes one stream, students subscribe. That is an SFU — Selective Forwarding Unit.

| Option | Notes |
|---|---|
| **LiveKit** | **Recommended.** Go, self-hostable, JS/.NET SDKs, built for exactly this |
| mediasoup | Lower level, more control, more work |
| Janus | Mature WebRTC gateway |

Run it on the same VPS or a second one.

**What you keep either way:** the Blazor UI, SignalR presence, the attendance scoring engine. Only the media transport changes. This is why the `IMediaTransport`-style abstraction matters — do the swap once.

---

## 5. Order of Work

| # | Step | Cost |
|---|---|---|
| 1 | ~~Real authentication~~ | **Done** |
| 2 | ~~Peer connection race + self-broadcast~~ | **Done** |
| 3 | ~~TURN config externalised~~ | **Done** |
| 4 | **One real class, 20 students, public relays** | **£0** |
| 5 | Own TURN server if video fails under load | ~$5–10/mo |
| 6 | LiveKit if cohort exceeds ~8 students | ~$10–20/mo |

**Step 4 before step 5.** Test the product before buying infrastructure. If 20 students on public relays works, you may not need a server yet — and if it doesn't, you'll know precisely why rather than guessing.

---

## 6. Verification Checklist

Before running a real class:

- [ ] Log in at `/login` — works with a seeded dev account
- [ ] Visiting `/session/{id}` while signed out redirects to `/login`
- [ ] A student visiting `/create-session` is denied
- [ ] `curl /api/config/ice` returns `usingPublicFallback: true` in dev (expected)
- [ ] Two browsers: lecturer starts session, student joins, video connects both ways
- [ ] Kill the student's network mid-session, reconnect, check the recap shows continuity
- [ ] Lecturer opens `/session-recap/{id}` — scores present and plausible

If video fails but presence works, it is TURN or NAT. If both fail, it is SignalR or auth.

---

## 7. Common Failures

| Symptom | Likely cause |
|---|---|
| Video works on Wi-Fi, fails on mobile data | TURN relay blocked or not configured |
| Works locally, fails for others | Relay port range closed |
| Random video failures under load | Public relay rate limit |
| Student never connects to peers | `SendPeerId` self-broadcast (fixed in `ff95a21`) |
| Session starts but students see nothing | `StartSession` race (fixed in `ff95a21`) |
| cotturn logs "Cannot find allocation" | Shared secret mismatch, or wrong `external-ip` |

---

## 8. Security Note

`TurnServers:SharedSecret` and any database credentials must come from **environment variables or user-secrets in production** — never committed to `appsettings.json`.

Do the same for `ConnectionStrings:DefaultConnection`, which currently holds a development string.