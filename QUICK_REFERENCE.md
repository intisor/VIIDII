# Quick Reference Guide: Persistence Layer Integration

## At a Glance

| Layer | Service | Scope | Purpose | Status |
|-------|---------|-------|---------|--------|
| **UI** | Blazor Components | Per-Request | User interaction | ✅ Existing |
| **Runtime** | SessionService | Singleton | In-memory sessions | ✅ Updated |
| **Runtime** | MessageService | Singleton | In-memory messages | ✅ Updated |
| **Bridge** | SessionPersistenceService | Scoped | Session → DB bridge | ✅ New |
| **Bridge** | MessagePersistenceService | Scoped | Message → DB bridge | ✅ New |
| **Repository** | SessionRepository | Scoped | Session data access | ✅ New |
| **Repository** | MessageRepository | Scoped | Message data access | ✅ New |
| **ORM** | ViidiiDbContext | Scoped | EF Core DbContext | ✅ New |
| **Storage** | SQL Server LocalDB | - | Persistent storage | ✅ Running |

---

## File Locations

```
VIIDII/
├── Data/
│   ├── ViidiiDbContext.cs                    ← EF Core config
│   ├── DatabaseSeeder.cs                     ← Seed initial data
│   ├── SessionPersistenceService.cs          ← Session business logic
│   └── MessagePersistenceService.cs          ← Message business logic
├── Services/
│   ├── UserService.cs                        ← DB-backed users
│   ├── SessionService.cs                     ← + persistence calls
│   ├── MessageService.cs                     ← + persistence calls
│   ├── SessionRepository.cs                  ← Session data access
│   └── MessageRepository.cs                  ← Message data access
├── Models/
│   └── User.cs                               ← EF entities + enums
├── Migrations/
│   └── 20250518221113_InitialCreate.cs       ← DB schema
├── Program.cs                                ← DI registration
└── PHASE_1-2_INTEGRATION_COMPLETE.md         ← This documentation
```

---

## Code Patterns

### Pattern 1: Fire-and-Forget Persistence

**Used In**: SessionService, MessageService  
**When**: Operation completes immediately, DB write happens in background

```csharp
// In SessionService.CreateSession()
var session = new Session { SessionId = GenerateSessionCode(), ... };
_sessions.TryAdd(session.SessionId, session);

// Return immediately, persist in background
_ = PersistSessionCreationAsync(lecturerId, title, allowedDepartments, allowedLevels);
return session;  // ← Returned before DB write!
```

### Pattern 2: Service Scope Creation

**Used In**: Persistence helper methods  
**Why**: Singleton (SessionService) needs Scoped (SessionPersistenceService)

```csharp
private async Task PersistAsync(...)
{
    using var scope = _serviceProvider.CreateScope();
    var persistenceService = scope.ServiceProvider
        .GetRequiredService<Data.SessionPersistenceService>();
    await persistenceService.CreateAndPersistSessionAsync(...);
}
```

### Pattern 3: Type Conversion (String ↔ Int)

**Used In**: SessionPersistenceService  
**Why**: Runtime uses string sessionId, DB uses int sessionPk

```csharp
public async Task<bool> AddParticipantAsync(string sessionId, string participantMatricNo)
{
    // Convert string sessionId to int sessionPk
    var session = await _sessionRepository.GetSessionByIdAsync(sessionId);
    if (session == null) return false;

    // Use int for participant operations
    await _sessionRepository.AddParticipantAsync(session.Id, participant.Id);
    return true;
}
```

---

## Method Call Flow

### Session Creation Flow

```
Blazor Component
    ↓ calls
SessionService.CreateSession()
    ├─ 1. Validate lecturer
    ├─ 2. Create in-memory session
    ├─ 3. Add to ConcurrentDictionary
    ├─ 4. RETURN session immediately ←
    └─ 5. _ = PersistSessionCreationAsync()  (background)
        └─ SessionPersistenceService.CreateAndPersistSessionAsync()
            └─ SessionRepository.CreateSessionAsync()
                └─ DbContext.Sessions.AddAsync()
                    └─ DbContext.SaveChangesAsync()
                        └─ SQL: INSERT INTO Sessions
```

### Participant Join Flow

```
Blazor Component
    ↓ calls
SessionService.JoinSession()
    ├─ 1. Validate session exists
    ├─ 2. Validate user exists
    ├─ 3. Check eligibility (dept/level)
    ├─ 4. Add to session.ParticipantIds
    ├─ 5. Update ParticipantStatuses
    ├─ 6. RETURN (session, null) ←
    └─ 7. _ = PersistParticipantJoinAsync()  (background)
        └─ SessionPersistenceService.AddParticipantAsync()
            ├─ 1. Get Session by SessionId (string)
            ├─ 2. Get User by MatricNo
            ├─ 3. Check eligibility again
            └─ 4. SessionRepository.AddParticipantAsync(sessionPk, userId)
                └─ DbContext.SessionParticipants.AddAsync()
                    └─ SQL: INSERT INTO SessionParticipants
```

### Message Creation Flow

```
Blazor Component
    ↓ calls
MessageService.CreatePost()
    ├─ 1. Validate user is lecturer
    ├─ 2. Create in-memory Message
    ├─ 3. Add to ConcurrentBag
    ├─ 4. RETURN message ←
    └─ 5. _ = PersistPostCreationAsync()  (background)
        └─ MessagePersistenceService.CreateAndPersistPostAsync()
            ├─ 1. Get User by MatricNo
            ├─ 2. Get Session by SessionId
            └─ 3. MessageRepository.CreateMessageAsync()
                └─ DbContext.Messages.AddAsync()
                    └─ SQL: INSERT INTO Messages
```

---

## Dependency Injection

### Registration Order (Program.cs)

```csharp
// 1. Register Scoped services FIRST
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<SessionRepository>();
builder.Services.AddScoped<MessageRepository>();
builder.Services.AddScoped<SessionPersistenceService>();
builder.Services.AddScoped<MessagePersistenceService>();

// 2. Register Singleton services AFTER (so they can see Scoped)
builder.Services.AddSingleton<SessionService>();
builder.Services.AddSingleton<MessageService>();
builder.Services.AddSingleton<PasswordHasher<User>>();
builder.Services.AddSingleton<AuthService>();

// 3. Register IServiceProvider explicitly
builder.Services.AddSingleton<IServiceProvider>(sp => sp);
```

### Accessing Scoped Service from Singleton

```csharp
public class SessionService  // Singleton
{
    private readonly IServiceProvider _serviceProvider;  // Injected

    public SessionService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    private async Task PersistAsync(...)
    {
        // Create a new scope for this operation
        using var scope = _serviceProvider.CreateScope();

        // Access the scoped service within the scope
        var persistenceService = scope.ServiceProvider
            .GetRequiredService<SessionPersistenceService>();  // Scoped

        await persistenceService.DoWorkAsync(...);

        // Scope disposed here, DbContext disposed
    }
}
```

---

## Database Schema Quick View

### Users Table
```sql
Users (Id: int PRIMARY KEY IDENTITY)
  ├─ MatricNo: nvarchar UNIQUE
  ├─ Name: nvarchar
  ├─ Email: nvarchar
  ├─ PasswordHash: nvarchar
  ├─ Role: int (0=Lecturer, 1=Student)
  ├─ Department: int?
  ├─ Level: int?
  └─ CreatedAt: datetime2
```

### Sessions Table
```sql
Sessions (Id: int PRIMARY KEY IDENTITY)
  ├─ SessionId: nvarchar UNIQUE  ← Business code (e.g., "20250518-ABCDEF")
  ├─ LecturerId: int FK → Users.Id
  ├─ LecturerMatricNo: nvarchar
  ├─ Title: nvarchar
  ├─ Status: int (0=Active, 1=Started, 2=Ended)
  ├─ AllowedDepartments: nvarchar (JSON)
  ├─ AllowedLevels: nvarchar (JSON)
  ├─ StartTime: datetime2?
  ├─ EndTime: datetime2?
  └─ CreatedAt: datetime2
```

### SessionParticipants Table
```sql
SessionParticipants (PK: SessionId + UserId)
  ├─ SessionId: int FK → Sessions.Id  ← Use int for participants!
  ├─ UserId: int FK → Users.Id
  └─ JoinedAt: datetime2
```

### Messages Table
```sql
Messages (Id: int PRIMARY KEY IDENTITY)
  ├─ SessionId: int FK → Sessions.Id
  ├─ AuthorId: int FK → Users.Id
  ├─ Content: nvarchar
  ├─ ParentId: int? FK → Messages.Id (for replies)
  ├─ Reaction: nvarchar (comma-separated emoji)
  └─ CreatedAt: datetime2
```

---

## Common Operations

### Create and Get Session

```csharp
// CREATE
var session = await sessionService.CreateSession(
    lecturerId: "MAT001",
    title: "CS101 Intro",
    allowedDepartments: new List<Department> { Department.CS },
    allowedLevels: new List<Level> { Level.L100 }
);
Console.WriteLine($"Session Code: {session.SessionId}");

// Later, in background:
// DB INSERT: INSERT INTO Sessions (SessionId, LecturerId, ...) VALUES ('20250518-ABCDEF', 1, ...)
```

### Join Session

```csharp
var (session, error) = await sessionService.JoinSession(
    sessionId: "20250518-ABCDEF",
    participantId: "STU001",
    connectionId: "sig-123"
);

if (session != null)
    Console.WriteLine($"Joined with {session.ParticipantIds.Count} others");

// Later, in background:
// DB INSERT: INSERT INTO SessionParticipants (SessionId, UserId) 
//            VALUES (42, 5)  ← where SessionId=42 is the PK for '20250518-ABCDEF'
```

### Post a Message

```csharp
var message = messageService.CreatePost(
    sessionId: "20250518-ABCDEF",
    userId: "MAT001",
    userName: "Dr. Smith",
    content: "Today we'll cover...",
    isLecturer: true
);
Console.WriteLine($"Posted message: {message.id}");

// Later, in background:
// DB INSERT: INSERT INTO Messages (SessionId, AuthorId, Content, ...)
//            VALUES (42, 1, 'Today we''ll cover...', ...)
```

### Add Reaction

```csharp
var success = messageService.AddReaction(
    sessionId: "20250518-ABCDEF",
    messageId: "550e8400-e29b-41d4-a716-446655440000",
    userId: "STU001",
    emoji: "👍"
);

if (success)
    Console.WriteLine("Reaction added");

// Later, in background:
// DB UPDATE: UPDATE Messages SET Reaction = '👍' WHERE Id = 1
```

---

## Error Handling

### What Happens if DB Write Fails?

```
SessionService.CreateSession()
    ↓
_ = PersistSessionCreationAsync()  ← Fire-and-forget, doesn't await
    ↓
try { SessionPersistenceService.CreateAndPersistSessionAsync() }
catch (Exception ex) {
    Console.WriteLine($"[SessionService] Error persisting: {ex.Message}");
    // Error logged, but doesn't affect session
}

Result:
- ✅ Blazor user sees session immediately
- ❌ Session not persisted to DB
- ⚠️ Error visible in console output
```

### How to Check if Persistence Worked

```csharp
// Option 1: Check console logs
// Look for: "[SessionService] Session 'CS101 Intro' persisted for lecturer MAT001"

// Option 2: Query the database directly
using var context = new ViidiiDbContext(optionsBuilder.Options);
var sessions = await context.Sessions.ToListAsync();
Console.WriteLine($"DB Sessions: {sessions.Count}");

// Option 3: Add monitoring (future)
// - Application Insights
// - Serilog with file logging
// - Health check endpoint
```

---

## Testing Scenarios

### Scenario 1: Normal Flow (Happy Path)

```
1. Lecturer creates session "CS101 Lecture"
   ✅ Session created in-memory (immediate)
   ✅ Session returned to UI
   ⏳ Background task starts
   ✅ SessionPersistenceService creates session
   ✅ SessionRepository saves to DB
   ✅ DB INSERT successful

2. Student joins session
   ✅ Student added to in-memory participants
   ✅ UI updates immediately
   ⏳ Background task starts
   ✅ SessionPersistenceService adds participant
   ✅ SessionRepository adds to SessionParticipants table
   ✅ DB INSERT successful

3. Student sees post from lecturer
   ✅ Post in in-memory message bag
   ✅ Rendered in Blazor component
   ⏳ Background task starts
   ✅ MessagePersistenceService creates message
   ✅ MessageRepository saves to DB
   ✅ DB INSERT successful
```

### Scenario 2: DB Write Fails (Resilience)

```
1. User creates session
   ✅ In-memory session created (immediate)
   ✅ UI shows session
   ⏳ Background persistence starts
   ❌ DB connection timeout
   ⚠️ Error logged to console
   ✅ User continues using app (data in memory)

   Note: Data is NOT persisted. If app restarts, session lost.
   Future: Implement retry queue to persist later.
```

### Scenario 3: App Restart (Current Behavior)

```
Session 1: Create session "Math 101"
Session 2: User joins, posts messages
Session 3: User closes browser

App restarts...

Before persistence layer:
   ✅ SessionService._sessions cleared (empty)
   ✅ MessageService._messages cleared (empty)
   Result: All in-memory data lost

After persistence layer:
   ✅ SessionService._sessions cleared (empty)
   ✅ MessageService._messages cleared (empty)
   ❌ BUT: Data is in database (viidii_dev)
   Future: Load previous sessions from DB on startup
```

---

## Troubleshooting

### Problem: "The instance of entity type 'User' cannot be tracked by this DbContext because another instance with the same key is already being tracked"

**Cause**: Entity loaded in one scope, used in another  
**Solution**: Use `.AsNoTracking()` or ensure single scope per operation

```csharp
// ❌ Wrong
var scope1 = serviceProvider.CreateScope();
var user = await repo.GetUserAsync();  // Scoped DbContext

var scope2 = serviceProvider.CreateScope();
await persistenceService.DoWorkAsync(user);  // Different DbContext

// ✅ Right
using var scope = serviceProvider.CreateScope();
var user = await scope.ServiceProvider.GetRequiredService<UserService>().GetUserAsync();
await scope.ServiceProvider.GetRequiredService<SessionPersistenceService>().DoWorkAsync(user);
```

### Problem: "No rows affected - session not persisted"

**Cause**: SessionId (string) vs Session.Id (int) mismatch  
**Solution**: Check database query uses correct key

```csharp
// ❌ Wrong (querying by string)
WHERE SessionId = '20250518-ABCDEF'

// ✅ Right (querying by int PK)
WHERE Id = 42

// In code:
var session = await _context.Sessions
    .FirstOrDefaultAsync(s => s.SessionId == "20250518-ABCDEF");  // Looks up by string
var sessionPk = session.Id;  // Get int PK
await AddParticipantAsync(sessionPk, userId);  // Pass int PK
```

### Problem: "DbContext has been disposed"

**Cause**: Scope disposed before async operation completes  
**Solution**: Don't dispose scope before await completes

```csharp
// ❌ Wrong
using var scope = _serviceProvider.CreateScope();
var persistenceService = scope.ServiceProvider.GetRequiredService<...>();
// Scope disposed here! Async operation might still be running
_ = persistenceService.DoWorkAsync();

// ✅ Right (for fire-and-forget)
private async Task PersistAsync(...)
{
    using var scope = _serviceProvider.CreateScope();
    var persistenceService = scope.ServiceProvider.GetRequiredService<...>();
    await persistenceService.DoWorkAsync();  // Complete before scope disposal
}
```

---

## Next Steps

### Phase 3: Verify Persistence (Testing)
- [ ] Manual testing of session creation → DB INSERT
- [ ] Manual testing of participant joins → DB INSERT
- [ ] Manual testing of messages → DB INSERT
- [ ] Check console output for persistence logs
- [ ] Query database to verify data

### Phase 4: Error Handling & Resilience
- [ ] Add retry logic for failed DB writes
- [ ] Implement persistence queue for offline support
- [ ] Add logging to file (Serilog)
- [ ] Create health check endpoint

### Phase 5: QR Attendance Tracking
- [ ] Create AttendanceLog entity
- [ ] Create migration
- [ ] Implement QR scan handler
- [ ] Integrate with persistence layer

### Phase 6: Analytics
- [ ] Query sessions from DB
- [ ] Calculate attendance percentages
- [ ] Generate reports
- [ ] Create dashboard

---

**Quick Links**:
- Full Documentation: `PHASE_1-2_INTEGRATION_COMPLETE.md`
- Database File: `C:\Users\<username>\AppData\Local\Microsoft\Microsoft SQL Server Local DB\Instances\MSSQLLocalDB\viidii_dev.mdf`
- Connection String: `Server=(localdb)\mssqllocaldb;Database=viidii_dev;Trusted_Connection=true;`
- Branch: `feature/phase1-4-complete`

**Version**: 1.0 | **Last Updated**: 2025-05-18 | **Status**: ✅ Ready
