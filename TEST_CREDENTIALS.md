# VIIDII Test Credentials

## How to Run the App

```powershell
dotnet run --project VIIDII.csproj
```

Then open your browser to: **http://localhost:5095**

---

## Test User Accounts

All users use the same password: **studpass1**

### 👨‍🏫 Lecturers
- **Matric No:** Lec001 | **Name:** John doe
- **Matric No:** Lec002 | **Name:** Dr. Brown

### 👨‍🎓 Students (Software Engineering - Level 200)
- **Matric No:** 123456 | **Name:** Intisor
- **Matric No:** 654321 | **Name:** Goodluck
- **Matric No:** 789012 | **Name:** Ade

### 👨‍🎓 Students (Other Departments)
- **Matric No:** 383012 | **Name:** Umar (Mining Engineering - Level 200)
- **Matric No:** 100001 | **Name:** Alice (Computer Science - Level 100)
- **Matric No:** 100002 | **Name:** Brian (Mechanical Engineering - Level 100)
- **Matric No:** 100003 | **Name:** Cynthia (Architecture - Level 100)

### 🔧 Admin
- **Matric No:** Admin | **Name:** Admin

---

## Quick Test Flow

1. **Login as Lecturer** (Lec001 / studpass1)
   - Click "Create New Session"
   - Enter session title
   - Select allowed departments and levels
   - Click "Create Session"

2. **Login as Student** (123456 / studpass1)
   - Click "Join Session"
   - Join the active session created by lecturer

3. **Test Video Features**
   - WebRTC video streaming via PeerJS
   - Real-time messaging via SignalR
   - Screen sharing
   - Participant tracking

---

## Features Migrated to Blazor

✅ Authentication & Session Management  
✅ Dashboard (Role-based UI)  
✅ Create Session (Lecturer)  
✅ Join Session (Student)  
✅ Real-time SignalR Communication  
✅ Session Service & User Management  

**Note:** WebRTC/PeerJS remains in JavaScript (wwwroot/js/session.js) as it requires browser APIs that cannot be migrated to C#.
