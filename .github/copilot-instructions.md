# GitHub Copilot Instructions: VIIDII (Edu Edition)

## Project Context
VIIDII is a browser-based WebRTC video conferencing tool engineered for low-bandwidth, high-concurrency African campus environments (FUTA). The stack is **Blazor Server (.NET 8) + SignalR + PeerJS + PostgreSQL (EF Core)**.

## Core Architecture Principles
- **Thread Safety First:** All SignalR hub state MUST use `ConcurrentDictionary<TKey, TValue>`. Never use `List<T>` or `Dictionary<T,V>` for shared state.
- **WebRTC as a DFA:** Every WebRTC peer connection follows a strict state machine: `Idle → Signaling → Connecting → Connected → Disconnected`. No state may be skipped.
- **P2P Over Cloud:** Prefer WebRTC Data Channels for file transfers. Only fall back to the server when P2P is genuinely impossible.
- **No Mock Services in Main:** `MockApiService.cs` is a dev scaffold only. All real features must use EF Core repositories backed by PostgreSQL.

## Sprint Reference
The active Phase 1 sprint tasks are defined in `/docs/Phase1-Project-Plan.md`.
The product vision is in `/docs/PRD.md`.
The architecture constraints are in `/docs/Feasibility-Study.md`.

## MCP Sync Prompt (Run this in Copilot Chat)

```
@workspace Read the Phase 1 tasks inside the `/docs/Phase1-Project-Plan.md` file. Use the GitHub MCP server tools to convert all 5 of these tasks into individual GitHub Issues in this repository and assign them to me.
Then, use the `projects` toolset to add all 5 newly created issues directly to my GitHub Project board named "@intisor's 2026 1.0".
Parse the metadata blocks attached to each task in the markdown to automatically update the custom fields on the project board:
* Set the 'Category' field.
* Set the 'FUTA Course' field.
* Set the 'Content Output' field.
* Assign them sequentially to Lock-in Days 1 through 5.
```
