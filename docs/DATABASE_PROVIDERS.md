# VIIDII — DATABASE PROVIDER GUIDE

**Date:** October 2026
**Status:** App decoupled from SQL Server. SQL Server still supported but no longer required.

---

## 1. Why This Changed

The app previously hardcoded `UseSqlServer(...)` and every entity used:

```csharp
.HasDefaultValueSql("GETUTCDATE()")
```

That meant a fresh clone could not run without SQL Server LocalDB installed. It also meant the **migration files themselves were SQL Server-flavoured** — 216 lines of `nvarchar(max)`, `datetime2`, `GETUTCDATE()` and `IDENTITY` across the migration history, none of which SQLite or PostgreSQL can execute.

The provider is now chosen at runtime from configuration.

---

## 2. Configuration

```json
{
  "Database": {
    "Provider": "Sqlite"      // Sqlite | Postgres | SqlServer
  },
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=viidii.db"
  }
}
```

| Value | Accepts |
| :--- | :--- |
| `Sqlite` | `Sqlite` |
| `Postgres` | `Postgres`, `PostgreSQL`, `Npgsql` |
| `SqlServer` | `SqlServer`, `MsSql` |

**Behaviour:**
- Non-Production with no provider configured → defaults to `Sqlite`
- Production with no provider configured → **throws.** Fail fast rather than silently using a file on a server
- No connection string → SQLite falls back to `Data Source=viidii.db`; anything else throws

---

## 3. The Important Limitation: Migrations Are Provider-Specific

This is not a VIIDII limitation — it is how EF Core works. **A migration file generated for SQL Server cannot run on SQLite or PostgreSQL.** The scaffolded DDL uses that provider's type names and identity syntax.

That is why the old migration history was unusable after switching, and why it was squashed:

```
216 lines of nvarchar(max) / datetime2 / GETUTCDATE() / IDENTITY
```

### Verified before squashing

```sql
SELECT name FROM sys.databases WHERE name LIKE 'viidii%'
-- (0 rows affected)
```

No database existed on LocalDB, so no data was lost. **Do not repeat this squash once there is real data.**

---

## 4. The Arrangement Used

| Environment | Provider | Schema created by |
| :--- | :--- | :--- |
| Local dev | SQLite file | `EnsureCreated()` from the model |
| Tests | SQLite in-memory | `EnsureCreated()` from the model |
| Production | **PostgreSQL** | `Migrate()` from migrations |

**Why this works:**

- Dev and tests never need a migration. `EnsureCreated()` builds the schema straight from the model, so provider-specific DDL never enters the dev loop.
- Production uses migrations, scaffolded **against PostgreSQL**, which is the canonical production provider.
- SQLite and PostgreSQL both accept `CURRENT_TIMESTAMP` for the UTC default, so the model stays portable. Only SQL Server needs `GETUTCDATE()`, and the model now detects the provider:

```csharp
private string UtcNowDefaultSql()
{
    if (Database.IsSqlServer()) return "GETUTCDATE()";
    return "CURRENT_TIMESTAMP";
}
```

---

## 5. Commands

### Scaffolding a migration

Migrations must be scaffolded against **PostgreSQL**, the production provider:

```powershell
$env:VIIDII_DESIGN_PROVIDER = "postgres"
dotnet ef migrations add <Name> --context ViidiiDbContext
```

### Applying in production

```bash
export Database__Provider=Postgres
export ConnectionStrings__DefaultConnection="Host=...;Database=viidii;Username=...;Password=..."
dotnet VIIDII.dll
```

`Program.cs` calls `Database.Migrate()` at startup in all environments, so migrations apply automatically on deploy.

### Local development

```bash
dotnet run
```

No database server required. Creates `viidii.db` in the working directory.

---

## 6. Why PostgreSQL for Production

| | SQLite | PostgreSQL |
| :--- | :--- | :--- |
| Concurrent writers | Poor — single writer | Good |
| 1,000 students joining at once | Lock contention | Fine |
| Network reliability (NEPA) | File on a VPS, single file | Proper server, reconnects |
| Backups | File copy | `pg_dump`, PITR |
| Managed hosting | Not offered | Widely available, cheap |

SQLite is a **development convenience**, not a production choice. File-based locking across a Nigerian network with intermittent power is the wrong tool for live attendance during a lecture.

---

## 7. Local PostgreSQL, If You Want It

```bash
docker run --name viidii-db -e POSTGRES_PASSWORD=postgres -e POSTGRES_DB=viidii_dev -p 5432:5432 -d postgres:17
```

```json
{
  "Database": { "Provider": "Postgres" },
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Database=viidii_dev;Username=postgres;Password=postgres"
  }
}
```

This is the best way to catch provider-specific SQL before deploying.

---

## 8. Switching Providers Later

If you need to move an existing PostgreSQL deployment to another provider, EF migrations will **not** do it. Use:

- `pg_dump` / `pg_restore` if moving between PostgreSQL versions or hosts
- `EnsureCreated()` plus a manual data copy if moving to a different engine entirely

---

## 9. What Was Not Changed

- `Microsoft.EntityFrameworkCore.SqlServer` is still referenced, so existing SQL Server deployments continue to work
- Connection strings are never hardcoded
- No credentials are committed; production values come from environment variables