# VIIDII — Production Deployment & Infrastructure Guide

This guide details how to take VIIDII into production, including **Google Sign-In**, **PostgreSQL database**, **LiveKit media server**, and **server hosting**.

---

## 1. Setting Up Google Sign-In (OAuth 2.0)

VIIDII includes native Google OAuth for hosts and instructors.

### Step-by-Step Google Cloud Setup:
1. Go to the [Google Cloud Console](https://console.cloud.google.com/).
2. Create a new project (e.g. `viidii-production`).
3. In the sidebar, navigate to **APIs & Services** > **OAuth consent screen**:
   - User Type: **External**.
   - App Name: `VIIDII`.
   - User Support Email: your email.
   - Developer Contact: your email.
   - Scopes: `openid`, `profile`, `email`.
4. Navigate to **APIs & Services** > **Credentials**:
   - Click **+ Create Credentials** > **OAuth client ID**.
   - Application type: **Web application**.
   - Name: `VIIDII Web Client`.
   - **Authorized JavaScript origins**:
     - `https://your-domain.com` (e.g. `https://viidii.intitech.dev`)
   - **Authorized redirect URIs**:
     - `https://your-domain.com/signin-google` *(Important: ASP.NET Core uses `/signin-google` automatically)*.
5. Click **Create** and copy your **Client ID** and **Client Secret**.

### Providing Credentials to VIIDII in Production:
Provide these via environment variables (recommended) or in `appsettings.Production.json`:

```bash
AUTHENTICATION__GOOGLE__CLIENTID="your-client-id.apps.googleusercontent.com"
AUTHENTICATION__GOOGLE__CLIENTSECRET="GOCSPX-your-secret"
```

---

## 2. Production Database: PostgreSQL

VIIDII uses Entity Framework Core with automatic provider detection. While local development uses SQLite, **production uses PostgreSQL**.

### Recommended Managed PostgreSQL Providers:
1. **Supabase** (Free 500MB tier / $25/mo Pro) — Instant setup, built-in backups, connection pooling.
2. **Neon.tech** (Free 0.5GB tier / Serverless Postgres) — Great for auto-scaling.
3. **DigitalOcean Managed Database** ($15/mo) — Zero maintenance, automatic daily backups.
4. **AWS RDS / Fly Postgres / Render Postgres**.

### Production Database Configuration:
Set the following environment variables on your server:

```bash
DATABASE__PROVIDER="Postgres"
CONNECTIONSTRINGS__DEFAULTCONNECTION="Host=db.your-host.com;Port=5432;Database=viidii;Username=postgres;Password=YOUR_STRONG_PASSWORD;SSL Mode=Require;Trust Server Certificate=true"
```

> [!NOTE]
> When `ASPNETCORE_ENVIRONMENT=Production`, VIIDII automatically executes `dbContext.Database.Migrate()` on startup, applying any pending migrations cleanly.

---

## 3. Video & Audio: LiveKit Server

VIIDII uses LiveKit for ultra-low latency WebRTC video, screen sharing, and audio.

### Option A: LiveKit Cloud (Recommended for Zero Ops)
- Sign up at [cloud.livekit.io](https://cloud.livekit.io/).
- **Free tier** includes **50 GB / month** of bandwidth (enough for hundreds of class hours).
- Copy your WebSocket URL, API Key, and API Secret.
- Environment variables:
  ```bash
  LIVEKIT__URL="wss://your-project.livekit.cloud"
  LIVEKIT__APIKEY="APIxxxxxxxx"
  LIVEKIT__APISECRET="secretxxxxxxxx"
  ```

### Option B: Self-Hosted LiveKit (Hetzner / DigitalOcean $4-6/mo)
Run the official LiveKit Docker container on an Ubuntu VPS with ports 7880 (HTTP), 7881 (TCP WebRTC), and 50000-60000 (UDP WebRTC) open.

---

## 4. Production Hosting Options

### Option 1: Docker / Container Platform (Fly.io, Railway, Render)
Fastest deployment with built-in HTTPS and health checks.

**Dockerfile** for VIIDII:
```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["VIIDII.csproj", "./"]
RUN dotnet restore "VIIDII.csproj"
COPY . .
RUN dotnet publish "VIIDII.csproj" -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080
ENTRYPOINT ["dotnet", "VIIDII.dll"]
```

### Option 2: Linux VPS (Ubuntu 22.04 / 24.04 with Caddy Reverse Proxy)
Most economical and highest control.
1. Install .NET Runtime:
   ```bash
   sudo apt-get install -y dotnet-runtime-9.0
   ```
2. Set up **Systemd Service** (`/etc/systemd/system/viidii.service`):
   ```ini
   [Unit]
   Description=VIIDII Video Platform
   After=network.target

   [Service]
   WorkingDirectory=/var/www/viidii
   ExecStart=/usr/bin/dotnet /var/www/viidii/VIIDII.dll
   Restart=always
   RestartSec=10
   SyslogIdentifier=viidii
   User=www-data
   Environment=ASPNETCORE_ENVIRONMENT=Production
   Environment=ASPNETCORE_URLS=http://127.0.0.1:5000
   Environment=DATABASE__PROVIDER=Postgres
   Environment=CONNECTIONSTRINGS__DEFAULTCONNECTION=Host=...
   Environment=AUTHENTICATION__GOOGLE__CLIENTID=...
   Environment=AUTHENTICATION__GOOGLE__CLIENTSECRET=...

   [Install]
   WantedBy=multi-user.target
   ```
3. Use **Caddy** for automatic free SSL certificates (`/etc/caddy/Caddyfile`):
   ```caddy
   viidii.yourdomain.com {
       reverse_proxy 127.0.0.1:5000 {
           header_up X-Forwarded-Proto https
       }
   }
   ```

---

## 5. Production Environment Variables Summary

| Variable | Example Value | Description |
| :--- | :--- | :--- |
| `ASPNETCORE_ENVIRONMENT` | `Production` | Enables strict security, HTTPS cookies, and migrations. |
| `DATABASE__PROVIDER` | `Postgres` | Tells EF Core to use PostgreSQL. |
| `CONNECTIONSTRINGS__DEFAULTCONNECTION` | `Host=...;Database=...;Username=...;Password=...` | PostgreSQL connection string. |
| `AUTHENTICATION__GOOGLE__CLIENTID` | `12345...apps.googleusercontent.com` | Google OAuth Client ID. |
| `AUTHENTICATION__GOOGLE__CLIENTSECRET` | `GOCSPX-...` | Google OAuth Client Secret. |
| `LIVEKIT__URL` | `wss://viidii.livekit.cloud` | LiveKit WebRTC server endpoint. |
| `LIVEKIT__APIKEY` | `APIxxxx...` | LiveKit API Key for token generation. |
| `LIVEKIT__APISECRET` | `secretxxxx...` | LiveKit Secret. |
