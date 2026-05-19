# Architecture Diagrams & Visual Guides

## 1. Overall System Architecture

```
┌────────────────────────────────────────────────────────────────────────────────┐
│                          VIIDII BLAZOR APPLICATION                             │
├────────────────────────────────────────────────────────────────────────────────┤
│                                                                                 │
│  ┌─────────────────────────────┐                                              │
│  │   BLAZOR UI LAYER           │                                              │
│  │  ┌────────────────────────┐ │                                              │
│  │  │ CreateSession.razor    │ │  Lecturer creates session                  │
│  │  ├────────────────────────┤ │                                              │
│  │  │ SessionView.razor      │ │  Students join & interact                  │
│  │  ├────────────────────────┤ │                                              │
│  │  │ Admin.razor            │ │  Admin views statistics                    │
│  │  └────────────────────────┘ │                                              │
│  └──────────────┬───────────────┘                                              │
│                 │ Calls services via DI                                        │
│                 ▼                                                               │
│  ┌─────────────────────────────┐         ┌─────────────────────────────┐      │
│  │  RUNTIME SERVICES (IN-MEMORY) SINGLETON                              │      │
│  ├─────────────────────────────┤         │                             │      │
│  │ SessionService              │         │ MessageService              │      │
│  │ ┌───────────────────────────┐│         │ ┌─────────────────────────┐│      │
│  │ │ CreateSession()      ┐    ││         │ │ CreatePost()       ┐    ││      │
│  │ │ JoinSession()        ├──┐ ││         │ │ CreateComment()    ├──┐ ││      │
│  │ │ EndSession()         │  │ ││         │ │ AddReaction()      │  │ ││      │
│  │ │ StartSession()       ┘  │ ││         │ │                    ┘  │ ││      │
│  │ │                          │ ││         │ │                       │ ││      │
│  │ │ _sessions:              │ ││         │ │ _messages:           │ ││      │
│  │ │ ConcurrentDictionary    │ ││         │ │ ConcurrentBag        │ ││      │
│  │ │ <string, Session>       │ ││         │ │ <Message>            │ ││      │
│  │ └───────────────────────────┘│         │ └─────────────────────────┘│      │
│  └──────────────┬────────────────┘         └──────────────┬──────────────┘      │
│                 │                                         │                     │
│                 │ Fire-and-forget async:                 │                     │
│                 │ _ = PersistAsync()                     │                     │
│                 │                                         │                     │
│                 ▼                                         ▼                     │
│  ┌─────────────────────────────────────────────────────────────────────────┐  │
│  │              PERSISTENCE BRIDGE SERVICES (SCOPED)                        │  │
│  ├─────────────────────────────────────────────────────────────────────────┤  │
│  │                                                                          │  │
│  │ SessionPersistenceService           MessagePersistenceService           │  │
│  │ ┌─────────────────────────────┐    ┌──────────────────────────────┐    │  │
│  │ │ CreateAndPersistSession     │    │ CreateAndPersistPost         │    │  │
│  │ │ AddParticipant              │    │ CreateAndPersistComment      │    │  │
│  │ │ EndAndPersistSession        │    │ AddReaction                  │    │  │
│  │ │ StartAndPersistSession      │    │                              │    │  │
│  │ └────────────┬────────────────┘    └──────────────┬───────────────┘    │  │
│  └──────────────┼──────────────────────────────────────┼────────────────────┘  │
│                 │                                       │                      │
│                 │ Uses repositories to access DB       │                      │
│                 ▼                                       ▼                      │
│  ┌────────────────────────────┐    ┌──────────────────────────────┐         │
│  │  REPOSITORY LAYER (SCOPED) │    │  REPOSITORY LAYER (SCOPED)   │         │
│  ├────────────────────────────┤    ├──────────────────────────────┤         │
│  │ SessionRepository          │    │ MessageRepository            │         │
│  │                            │    │                              │         │
│  │ CreateSessionAsync()       │    │ CreateMessageAsync()         │         │
│  │ GetSessionByIdAsync()      │    │ GetSessionMessagesAsync()    │         │
│  │ AddParticipantAsync()      │    │ GetMessageRepliesAsync()     │         │
│  │ UpdateSessionAsync()       │    │ UpdateMessageAsync()         │         │
│  │ GetActiveSessionsAsync()   │    │ DeleteMessageAsync()         │         │
│  │ ... and more               │    │ ... and more                 │         │
│  └────────────┬───────────────┘    └──────────────┬───────────────┘         │
│               │                                    │                        │
│               └────────────┬───────────────────────┘                        │
│                            │                                               │
│                            │ EF Core queries                              │
│                            ▼                                               │
│  ┌──────────────────────────────────────────────────────────────────────┐ │
│  │              EF CORE (ViidiiDbContext)                               │ │
│  ├──────────────────────────────────────────────────────────────────────┤ │
│  │                                                                      │ │
│  │  DbSet<User>                 DbSet<Session>                         │ │
│  │  DbSet<SessionParticipant>   DbSet<Message>                         │ │
│  │  DbSet<AttendanceLog>        DbSet<FileMetadata>                    │ │
│  │                                                                      │ │
│  │  Relationships:                                                      │ │
│  │  - Session FK → User (LecturerId)                                  │ │
│  │  - SessionParticipant FK → Session.Id, User.Id                    │ │
│  │  - Message FK → Session.Id, User.Id (Author), Message.Id (Parent) │ │
│  │                                                                      │ │
│  └────────────┬─────────────────────────────────────────────────────────┘ │
│               │                                                            │
│               │ SQL Commands                                              │
│               ▼                                                            │
│  ┌──────────────────────────────────────────────────────────────────────┐ │
│  │           SQL SERVER LOCALDB (viidii_dev)                            │ │
│  ├──────────────────────────────────────────────────────────────────────┤ │
│  │                                                                      │ │
│  │  [Users] ─────┬─────→ [Sessions]                                   │ │
│  │               │           │                                         │ │
│  │               │           └─────→ [SessionParticipants]            │ │
│  │               │                        │                           │ │
│  │               └────────────────────────┤                           │ │
│  │                                        │                           │ │
│  │               ┌────────────────────────┘                           │ │
│  │               │                                                     │ │
│  │               └─────→ [Messages]                                   │ │
│  │                           │                                         │ │
│  │                           └─────→ [Messages] (self-join for replies) │ │
│  │                                                                      │ │
│  │  Database: C:\Users\*\AppData\Local\Microsoft\...\viidii_dev.mdf   │ │
│  │  Connection: Server=(localdb)\mssqllocaldb;Database=viidii_dev     │ │
│  │                                                                      │ │
│  └──────────────────────────────────────────────────────────────────────┘ │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## 2. Data Flow: Creating a Session

```
TIME FLOW (Top to Bottom)

T=0ms   ┌─ User clicks "Create Session"
        │
        ▼
        CreateSession.razor component
        (with Blazor @onclick handler)
        │
        │ sessionService.CreateSession(...)
        │
        ▼
        SessionService.CreateSession() method
        │
        ├─ 1. Get lecturers from MockApiService (sync)
        ├─ 2. Check if lecturer exists ✓
        ├─ 3. Create new Session object ✓
        ├─ 4. Add to _sessions ConcurrentDictionary ✓
        │
T=5ms   ├─ 5. RETURN session object ← TO USER ← UI UPDATES IMMEDIATELY
        │
        └─ 6. _ = PersistSessionCreationAsync(...)  (ASYNC, NO AWAIT)
           │
           └─ Background Task Starts (non-blocking)
              │
T=6ms         ▼
              Create scope:
              using var scope = _serviceProvider.CreateScope()
              │
              ├─ Get SessionPersistenceService from scope
              │
              ├─ sessionService.CreateAndPersistSessionAsync()
              │  │
              │  ├─ Get User by MatricNo from UserService (DB query)
              │  ├─ Expand "Any" enum values
              │  │
              │  ├─ Call repository.CreateSessionAsync(session)
              │  │  │
T=50ms        │  │  ▼
              │  │  sessionRepository.CreateSessionAsync()
              │  │  │
              │  │  ├─ context.Sessions.AddAsync(session)
              │  │  │
T=51ms        │  │  ├─ context.SaveChangesAsync()
              │  │  │  │
              │  │  │  ├─ Generate SQL INSERT statement
              │  │  │  │
              │  │  │  └─ Execute SQL:
              │  │  │     INSERT INTO Sessions (
              │  │  │       SessionId, LecturerId, Title, 
              │  │  │       AllowedDepartments, AllowedLevels,
              │  │  │       Status, CreatedAt
              │  │  │     ) VALUES (
              │  │  │       '20250518-ABCDEF', 1, 'CS101 Lecture',
              │  │  │       '[0,1,2]', '[0,1,2]',
              │  │  │       0, '2025-05-18 10:30:45.123'
              │  │  │     )
              │  │  │
T=100ms       │  │  └─ SQL Server returns: Id = 42
              │  │
              │  └─ Return Models.Session
              │
T=101ms       └─ Console.WriteLine("[SessionService] Session persisted...")
              │
              ▼
              (Background task completes, scope disposed)


SUMMARY:
========
T=0ms    User action
T=5ms    UI receives response (in-memory session)
T=100ms  Database updated (user doesn't wait)
```

---

## 3. Singleton ↔ Scoped Dependency Injection

```
DI Container
├─ Singleton Services (last forever)
│  ├─ SessionService ────────────────┐
│  ├─ MessageService                 │ These CANNOT directly
│  └─ AuthService                    │ inject Scoped services
│                                    │ (they live longer!)
├─ Scoped Services (per request/scope)
│  ├─ SessionPersistenceService      │
│  ├─ MessagePersistenceService      │ These CAN be injected
│  ├─ SessionRepository              │ into Scoped consumers
│  ├─ MessageRepository              │ 
│  ├─ UserService                    │
│  ├─ DbContext                      │
│  └─ (created/destroyed per scope)  │
│                                    │
└─ Transient Services (per instantiation)
   └─ (unused in this app)


SOLUTION: IServiceProvider
==========================

    SessionService (Singleton)
           │
           │ constructor:
           ▼
    private readonly IServiceProvider _serviceProvider;
           │
           │ when needing Scoped service:
           ▼
    private async Task PersistAsync()
    {
        // 1. Create a NEW scope
        using var scope = _serviceProvider.CreateScope();

        // 2. Get scoped service from NEW scope
        var persistenceService = scope.ServiceProvider
            .GetRequiredService<SessionPersistenceService>();

        // 3. Use it (it has its own DbContext, etc.)
        await persistenceService.DoWorkAsync(...);

        // 4. Scope disposed here (Scoped services cleaned up)
    }


VISUALIZED:
===========

    SINGLETON LIFETIME
    ┌─────────────────────────────────────────────────────┐
    │ SessionService created at app startup               │
    │ │                                                    │
    │ ├─ Persists for entire application lifetime        │
    │ │                                                    │
    │ ├─ At T=10ms: CreateSession()                       │
    │ │  └─ Creates scope-A                              │
    │ │     ├─ SessionPersistenceService-A created       │
    │ │     ├─ DbContext-A created                       │
    │ │     └─ DB work...                                │
    │ │     └─ Scope-A disposed                          │
    │ │                                                    │
    │ ├─ At T=100ms: JoinSession()                        │
    │ │  └─ Creates scope-B                              │
    │ │     ├─ SessionPersistenceService-B created       │
    │ │     ├─ DbContext-B created (new!)               │
    │ │     └─ DB work...                                │
    │ │     └─ Scope-B disposed                          │
    │ │                                                    │
    │ └─ SessionService still alive, waiting for next call
    │
    └─────────────────────────────────────────────────────┘
         App Restart → SessionService destroyed
```

---

## 4. Session State Transitions

```
                ┌──────────────┐
                │   CREATED    │
                │  (in-memory) │
                └──────┬───────┘
                       │
                       │ CreateSession()
                       │ 1. _sessions.Add()
                       │ 2. Fire-and-forget: SaveToDB
                       │
                       ▼
                ┌──────────────┐
                │    ACTIVE    │ ← Can accept joins
                │  in-memory + │   Visible to students
                │      DB      │   Messages not yet counted
                └──────┬───────┘
                       │
                       │ StartSession()
                       │ 1. Set Status = Started
                       │ 2. Record StartTime
                       │ 3. Fire-and-forget: SaveToDB
                       │
                       ▼
                ┌──────────────┐
                │   STARTED    │ ← Attendance scoring active
                │  in-memory + │   Participants locked
                │      DB      │   Messages being counted
                └──────┬───────┘
                       │
                       │ EndSession()
                       │ 1. Set Status = Ended
                       │ 2. Record EndTime
                       │ 3. Fire-and-forget: SaveToDB
                       │
                       ▼
                ┌──────────────┐
                │    ENDED     │ ← Read-only
                │  in-memory + │   Can view history
                │      DB      │   Attendance locked
                └──────────────┘


IN MEMORY vs DATABASE:
======================

Session Created:
├─ T=0ms   In-memory: ✓ (ConcurrentDictionary)
├─ T=5ms   User sees: ✓ (response returned)
└─ T=100ms Database:  ✓ (INSERT completed)
           Status:   ACTIVE

Session Joined by Student:
├─ T=200ms In-memory: ✓ (ParticipantIds.Add())
├─ T=205ms User sees: ✓ (participant count updates)
└─ T=250ms Database:  ✓ (INSERT into SessionParticipants)

Session Started:
├─ T=500ms In-memory: ✓ (Status = Started, StartTime set)
├─ T=505ms User sees: ✓ (UI updates, scoring begins)
└─ T=550ms Database:  ✓ (UPDATE Sessions)

Session Ended:
├─ T=3000ms In-memory: ✓ (Status = Ended, EndTime set)
├─ T=3005ms User sees: ✓ (UI locked, results shown)
└─ T=3050ms Database:  ✓ (UPDATE Sessions)

NOTE: If app crashes between "In-memory ✓" and "Database ✓":
- In-memory data lost (app restart clears it)
- But database has consistent snapshot of previous state
- User can re-join from DB next time
```

---

## 5. Type Conversions

```
RUNTIME LAYER                PERSISTENCE LAYER         DATABASE
(String IDs)                 (Convert)                 (Int FKs)

SessionId: "20250518-ABCDEF"
    ↓
CreateSession() called
    ↓
Session {
  SessionId: "20250518-ABCDEF",
  LecturerMatricNo: "MAT001",
  ...
}
    ↓
DB INSERT

            ← Session.Id = 42 (auto-generated PK)

            ← SessionRepository receives:
              Session {
                Id: 42,
                SessionId: "20250518-ABCDEF",
                LecturerId: 1,
                ...
              }

            ↓
            context.Sessions.Add(Session)
            ↓
            SQL:
            INSERT INTO Sessions (
              SessionId,          ← '20250518-ABCDEF'
              LecturerId,         ← 1
              Title,
              ...
            )

                                   ↓
                                   [Sessions] table
                                   Id | SessionId          | LecturerId
                                   42 | 20250518-ABCDEF    | 1

                                   Now SessionId = 42 is the
                                   foreign key for participants!


PARTICIPANT JOIN:
==================

JoinSession("20250518-ABCDEF", "STU001")
    ↓
SessionPersistenceService.AddParticipantAsync(
    sessionId: "20250518-ABCDEF",  ← String!
    participantMatricNo: "STU001"
)
    ↓
// Get Session by string ID to find int PK
var session = await _sessionRepository.GetSessionByIdAsync(
    "20250518-ABCDEF"
);
    ↓
// Now we have session.Id = 42
    ↓
await _sessionRepository.AddParticipantAsync(
    sessionPk: 42,  ← Int!
    userId: 5
)
    ↓
context.SessionParticipants.AddAsync(
    new SessionParticipant {
        SessionId: 42,   ← FK to Sessions.Id
        UserId: 5        ← FK to Users.Id
    }
)
    ↓
SQL:
INSERT INTO SessionParticipants (SessionId, UserId)
VALUES (42, 5)
    ↓
[SessionParticipants] table
SessionId | UserId
42        | 5
(links back to Sessions row with Id=42)


KEY INSIGHT:
============
- Session.SessionId = "20250518-ABCDEF"  ← Business code, shown to users
- Session.Id = 42                        ← Primary key, used for FKs
- SessionParticipant.SessionId = 42      ← FK to Sessions.Id, NOT SessionId string!
```

---

## 6. Request Lifecycle

```
REQUEST LIFECYCLE IN BLAZOR
===========================

Blazor Server (Interactive)
├─ User action (click, input change, etc.)
├─ DI Container resolves dependencies
├─ Component method executes (e.g., OnClickCreateSession)
└─ Component calls services

    sessionService.CreateSession(...)
    ├─ Type: Singleton
    ├─ Resolved: Once at app startup, reused for all requests
    ├─ In-memory work happens synchronously
    └─ Fire-and-forget async:
       _ = PersistSessionCreationAsync(...)
           ├─ Type: Async method
           ├─ Returns immediately without awaiting
           ├─ Runs in background ThreadPool
           └─ Creates own scope:
              using var scope = _serviceProvider.CreateScope()
              ├─ Scoped services instantiated
              ├─ DbContext created for this scope
              ├─ Work happens asynchronously
              └─ Scope disposed when complete
                 └─ DbContext disposed
                    └─ Connection returned to pool

    Component receives result
    └─ UI updates via Blazor state management
    └─ SignalR sends update to browser
    └─ Browser renders new state


CONCURRENCY:
============

Request 1 (T=0):                     Request 2 (T=10ms):
    User A creates session              User B joins session
    ▼                                   ▼
    SessionService.CreateSession()      SessionService.JoinSession()
    ├─ Modify _sessions (thread-safe)   ├─ Modify _sessions (thread-safe)
    │  (ConcurrentDictionary)           │  (ConcurrentDictionary)
    └─ _ = PersistAsync() #1            └─ _ = PersistAsync() #2

                ↓ (Background)                       ↓ (Background)

    Scope-A created                     Scope-B created
    DbContext-A                         DbContext-B
    (separate DB connections)           (separate DB connections)
    │                                   │
    ├─ INSERT Session A                 ├─ Query to get Session A
    └─ Commit                           ├─ INSERT SessionParticipant A,B
                                        └─ Commit

    Result: Both complete without blocking each other
            Both have separate database transactions
            No locking, no waiting
```

---

## 7. Error Handling Flow

```
HAPPY PATH:
===========
CreateSession()
    ▼ (fast)
Return session to user  ✓
    ▼
_ = PersistAsync()
    ▼
try {
    SessionPersistenceService.CreateAndPersistSessionAsync()
        ▼
    SessionRepository.CreateSessionAsync()
        ▼
    DbContext.SaveChangesAsync()
        ▼
    SQL: INSERT ✓
} catch (Exception ex) {
    Console.WriteLine("[SessionService] Error persisting: {ex.Message}");
    // No rethrow, user doesn't see error
}


ERROR SCENARIOS:
================

Scenario 1: User doesn't exist
    SessionPersistenceService.CreateAndPersistSessionAsync()
        ▼
    await _userService.GetUserByMatricNoAsync(lecturerId)
        ▼
    Returns null
        ▼
    if (user == null || user.Role != Role.Lecturer)
        return null;  ✓ Handles gracefully


Scenario 2: Database connection fails
    SessionRepository.CreateSessionAsync()
        ▼
    await _context.Sessions.AddAsync(session)
    await _context.SaveChangesAsync()
        ▼
    DbUpdateException: Connection timeout
        ▼
    try-catch in PersistAsync():
    catch (Exception ex) {
        Console.WriteLine("[SessionService] Error persisting: {ex.Message}");
        // Error logged, but doesn't propagate
        // Session already in in-memory ConcurrentDictionary
        // User continues working with in-memory data
        // Data NOT persisted to DB (eventual loss if app restarts)
    }


Scenario 3: Invalid state
    SessionRepository.RemoveParticipantAsync(sessionPk, userId)
        ▼
    var participant = await _context.SessionParticipants
        .FirstOrDefaultAsync(sp => sp.SessionId == sessionPk && sp.UserId == userId);
        ▼
    if (participant == null)
        return false;  ✓ Handles gracefully


CURRENT BEHAVIOR:
=================
- App doesn't crash if DB fails
- Errors logged to console
- User experience unaffected (uses in-memory data)
- Data loss risk if app crashes before DB write completes

FUTURE IMPROVEMENTS:
====================
- Implement retry queue
- Add persistence verification
- Use Polly for resilience policies
- Add health checks
- Implement Circuit Breaker pattern
```

---

## 8. Database Connection String Reference

```
LOCAL SQL SERVER EXPRESS (LOCALDB):
===================================

Configuration: appsettings.json
{
  "ConnectionStrings": {
    "DefaultConnection": 
    "Server=(localdb)\\mssqllocaldb;Database=viidii_dev;Trusted_Connection=true;"
  }
}

Breakdown:
├─ Server=(localdb)\mssqllocaldb
│  │ Connection to LocalDB instance
│  │ No username/password needed (Windows auth)
│  └─ Instance name: MSSQLLocalDB (default)
│
├─ Database=viidii_dev
│  └─ Database name created during migration
│
└─ Trusted_Connection=true
   └─ Use Windows authentication (current user)


File Locations:
C:\Users\<USERNAME>\AppData\Local\Microsoft\
  └─ Microsoft SQL Server Local DB\Instances\
     └─ MSSQLLocalDB\
        └─ viidii_dev.mdf       (data file)
        └─ viidii_dev_log.ldf   (log file)


Access Methods:

1. Via Entity Framework (Code):
   optionsBuilder.UseSqlServer(connectionString);

2. Via SQL Server Management Studio (SSMS):
   Server: (localdb)\mssqllocaldb
   Database: viidii_dev

3. Via Command Line (sqlcmd):
   sqlcmd -S (localdb)\mssqllocaldb -d viidii_dev

4. Via Visual Studio:
   View → SQL Server Object Explorer
   → (localdb)\mssqllocaldb → viidii_dev


Useful Commands:
================
# Start LocalDB instance
sqllocaldb start mssqllocaldb

# Stop LocalDB instance  
sqllocaldb stop mssqllocaldb

# Delete and recreate
sqllocaldb delete mssqllocaldb
sqllocaldb create mssqllocaldb

# List instances
sqllocaldb info

# Get instance info
sqllocaldb info mssqllocaldb
```

---

**Visual Guide Version**: 1.0  
**Last Updated**: 2025-05-18  
**Status**: ✅ Complete
