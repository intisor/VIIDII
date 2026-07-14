# VIIDII

VIIDII is a Blazor-based live classroom platform for lecturers and students. It combines Blazor interactive server components, SignalR, EF Core, and SQL Server LocalDB to support real-time teaching sessions, participant monitoring, messaging, attendance tracking, and post-session recap.

## Current Architecture

- **Application model**: Blazor Web App on `.NET 10`
- **UI**: Razor components under `Components/`
- **Real-time coordination**: SignalR via `Hubs/SessionHub.cs`
- **Persistence**: EF Core with `ViidiiDbContext`
- **Database**: SQL Server LocalDB
- **Session runtime**: in-memory live state for transient presence and active session coordination
- **Durable history**: sessions, participants, messages, attendance logs, and recap data persisted in the database

## Core Features

- Lecturer-created live sessions
- Student join and participation workflow
- Real-time participant status monitoring
- Attendance scoring and session recap
- Lecturer posts, comments, and reactions
- Session cleanup and background participant activity checks

## Phase 6 Status

Phase 6 durability work is now implemented for the main recap and history paths.

Completed in this phase:
- Durable participant join and leave persistence
- Attendance status transition logging
- Reconstruction-free recap scoring from persisted attendance logs
- Persisted attendance segment durations for closed status intervals
- Final attendance segment closure when a session ends
- DB-backed message identifiers and persistence-aware message flow
- Session recap loading through the database-aware path

Still intentionally kept in memory:
- Active SignalR connection presence
- Runtime-only live coordination state for in-progress sessions

## Project Structure

- `Components/` - Blazor UI components and pages
- `Data/` - EF Core context, seeding, and persistence bridge services
- `Hubs/` - SignalR hub endpoints for live session behavior
- `Models/` - domain entities and enums
- `Services/` - application services and repositories
- `wwwroot/` - static assets

## Prerequisites

- .NET 10 SDK
- Visual Studio 2026 or later
- SQL Server LocalDB

## Getting Started

1. Restore packages:
   - `dotnet restore`
2. Build the project:
   - `dotnet build`
3. Run the app:
   - `dotnet run`

The app applies migrations and seeds required data at startup.

## Development Notes

- The root solution is `VIIDII.slnx`.
- The main project is `VIIDII.csproj`.
- The application uses startup migration/seeding in `Program.cs`.
- Recap and historical attendance analytics should use persisted data paths, not transient runtime-only state.

## Technology Summary

- Blazor Web App
- ASP.NET Core SignalR
- Entity Framework Core
- SQL Server LocalDB
- Bootstrap

## Repository Goal

VIIDII is being evolved from a runtime-centric classroom app into a hybrid architecture where live presence remains memory-backed but recap, analytics, messaging history, and attendance history are durable and restart-safe.
