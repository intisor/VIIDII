# VIIDII Next-Gen Post-Class Architecture: AI Lecture Brain & Smart Attendance Pardon

> **Status:** Planned (Option B: Self-Hosted / Edge-First)  
> **Date:** October 2026  
> **Target Platform:** VIIDII Classroom Engine (ASP.NET Core 10, Blazor Server, SQLite, Ollama / EmbeddingGemma)

---

## 1. Executive Summary

This architecture plan elevates VIIDII from a live video communication tool into an **active, asynchronous educational repository** and an **empathetic classroom manager**:

1. **Option B: Self-Hosted "Lecture Brain" (EmbeddingGemma + Gemma):**
   * Eliminates the ephemeral nature of live video calls.
   * Transcripts, chat messages, and lecture timestamps are vectorized using Google's open-weights **EmbeddingGemma (300M)** model and indexed directly into an embedded SQLite vector store.
   * Students query the lecture through a lightweight, self-hosted **Gemma (2B / 3)** model running on the server (or via Ollama), enabling zero-streaming-data review, timestamped answers, and instant practice quizzes.
2. **Automated Client Telemetry & "Smart Attendance Pardon":**
   * Students in developing infrastructure often suffer abrupt power cuts or cellular network drops before they can manually click "Battery Low" or "Data Finished".
   * VIIDII automatically tracks passive hardware and network signals (`navigator.getBattery()`, `navigator.connection`, WebRTC round-trip latency).
   * If a student's session drops abruptly, the system inspects their last telemetry beacon (e.g., Battery: 4%, RTT: 2034ms on 3G) and automatically grants an **Excused Grace Attendance Pardon** instead of penalizing them as an unexcused absence.

---

## 2. Feature 1: Self-Hosted Post-Class "Lecture Brain" (Option B)

### 2.1 Model & Runtime Stack

* **Embedding Model:** **Google EmbeddingGemma (300M parameters)**
  * Lightweight, Apache 2.0 open-weights.
  * Native 768-dimensional vector representations.
  * Runs with tiny RAM footprint (<600MB) via Ollama (`ollama run embeddinggemma`) or ONNX Runtime.
* **Inference / Tutor LLM:** **Google Gemma 2 (2B) / Gemma 3 (1B/4B)**
  * Quantized (Q4_K_M) requires only ~1.8GB VRAM or system RAM.
  * Hosted locally on the server alongside the VIIDII daemon via Ollama or `Microsoft.SemanticKernel`.
  * Zero external API fees, total student data privacy, and full offline resilience.
* **Vector Store:** **Embedded SQLite (`sqlite-vec` or in-process Cosine Similarity)**
  * Vector embeddings stored directly alongside `VIIDII.db`.
  * Zero dependency on costly cloud vector databases (Pinecone, Weaviate, Milvus).

### 2.2 Data Ingestion & Indexing Pipeline

```mermaid
flowchart TD
    A[Lecturer Ends Session] --> B[Aggregate Live Transcripts + In-Class Q&A]
    B --> C[Text Chunking Engine: 150-250 words with Start/End Timestamps]
    C --> D[Embedding Service: EmbeddingGemma API / Ollama]
    D --> E[(SQLite Table: SessionLectureChunks & Vectors)]
    F[Student Posts Question] --> G[Embed Student Query with EmbeddingGemma]
    G --> H[Cosine Similarity Search: Retrieve Top 3 Chunks]
    H --> I[Gemma 2B Context Prompt: Grounded strictly on Lecture]
    I --> J[Student Receives Answer with Clickable [HH:MM:SS] Timestamps]
```

#### Database Schema: Lecture Vectors (`Models/LectureKnowledge.cs`)
```csharp
public class LectureChunk
{
    public int Id { get; set; }
    public required string SessionId { get; set; }
    public int ChunkIndex { get; set; }
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public required string Speaker { get; set; } // "Lecturer" or "Student"
    public required string TextContent { get; set; }
    public byte[]? EmbeddingVector { get; set; } // 768 float32 values (3072 bytes)
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
```

### 2.3 Student Post-Class Superpowers

1. **"Ask the Lecture" (Interactive Chat UI):**
   * Accessible directly from the student's post-class attendance receipt card or class history page.
   * Student types: *"What did the lecturer say about pointer arithmetic vs arrays?"*
   * System retrieves relevant transcript portions and Gemma responds:
     > *"At [23:15], the lecturer explained that array indexing `a[i]` is syntactic sugar for `*(a + i)`. He cautioned that modifying pointer boundaries without length checks can lead to segmentation faults."*
2. **Instant Exam Practice / Self-Quizzer:**
   * Students tap **"Generate 5 Review Questions"**.
   * Gemma synthesizes multiple-choice or short-answer questions strictly from what was taught in that session.
   * Provides immediate feedback and links back to the exact lecture segment.
3. **Zero-Bandwidth Revision:**
   * Replaces 1–2 GB video recordings with lightweight text and audio clips for students on tight mobile data plans.

---

## 3. Feature 2: Automated Telemetry & "Smart Attendance Pardon"

### 3.1 Passive Client Telemetry Probing

Rather than relying on a student to manually panic-click issue buttons as their battery shuts down or signal degrades to zero, the web client will passively sample device vitals every 30 seconds (or immediately when a critical threshold is crossed):

```javascript
// Telemetry collector in wwwroot/js/sessionTelemetry.js
export function getDeviceTelemetry() {
    const telemetry = {
        timestamp: new Date().toISOString(),
        batteryLevel: null,
        isCharging: null,
        connectionType: 'unknown',
        rtt: null,
        downlink: null,
        isLowDataMode: false
    };

    // 1. Battery Status API (Chrome / Edge / Android)
    if ('getBattery' in navigator) {
        navigator.getBattery().then(battery => {
            telemetry.batteryLevel = Math.round(battery.level * 100);
            telemetry.isCharging = battery.charging;
        });
    }

    // 2. Network Information API
    if ('connection' in navigator) {
        telemetry.connectionType = navigator.connection.effectiveType; // '4g', '3g', '2g', 'slow-2g'
        telemetry.rtt = navigator.connection.rtt; // Round-trip time in ms
        telemetry.downlink = navigator.connection.downlink; // Bandwidth in Mbps
        telemetry.isLowDataMode = navigator.connection.saveData;
    }

    return telemetry;
}
```

### 3.2 Heartbeat & Emergency Exit Beacon

* **Heartbeat:** Sent over existing SignalR connection every 30 seconds (`SendAsync("HeartbeatTelemetry", telemetry)`).
* **Emergency Unload (`pagehide` / `unload`):** Uses `navigator.sendBeacon("/api/session/telemetry/disconnect", data)` to ensure the absolute last known battery percentage and RTT are saved even if the browser crashes or tab is killed.

### 3.3 Automated Attendance Grace & Pardon Engine

When a student abruptly disconnects before the class ends, the server evaluates their final telemetry snapshot against the **Smart Grace Policy**:

```mermaid
flowchart TD
    A[Student Abruptly Disconnects] --> B[Inspect Last Heartbeat & Disconnect Beacon]
    B --> C{Battery <= 7% and Discharging?}
    C -->|Yes| D[Classify: 'Excused Drop (Battery Exhausted)']
    C -->|No| E{RTT > 1500ms OR Connection == '2g' / 'slow-2g'?}
    E -->|Yes| F[Classify: 'Excused Drop (Network Infrastructure Failure)']
    E -->|No| G{Active Duration >= 60% of Session?}
    G -->|Yes| H[Classify: 'Present (Majority Completed)']
    G -->|No| I[Classify: 'Early Departure (Unverified)']
    
    D --> J[Attendance Pardon Granted in Recap & CSV]
    F --> J
    H --> J
```

#### Attendance Status Classification Table
| Telemetry Indicators | Automated System Decision | Label in Lecturer Recap & CSV |
| :--- | :--- | :--- |
| Battery $\le$ 7%, Discharging | **Automatic Pardon** | `Excused (Battery Depleted: 4%)` |
| RTT $\ge$ 1500ms, Packet Loss $\ge$ 40% | **Automatic Pardon** | `Excused (Network Failure: 2034ms RTT)` |
| `effectiveType` = `slow-2g`, Downlink < 0.1 Mbps | **Automatic Pardon** | `Excused (Data Starvation / 2G Drop)` |
| Normal vitals, sudden close, < 50% session time | Flagged for review | `Left Early (Unverified)` |
| Normal vitals, attended $\ge$ 70% | Full Credit | `Present (Majority Attended)` |

---

## 4. Implementation Roadmap

### Phase 1: Client Auto-Telemetry & Smart Attendance Grace (Quick Win)
1. Add `sessionTelemetry.js` collecting Battery API + Network Info API vitals.
2. Add `TelemetryLog` entity to SQLite linked to `SessionParticipant`.
3. Add heartbeat ingestion to `SessionHub.cs`.
4. Update `SessionFileEndpoints.cs` CSV generator to display detailed Pardon reasons in the export.
5. Update `SessionRecap.razor` to show the "Grace / Excused" badges in the attendance table.

### Phase 2: EmbeddingGemma & Vector Storage Setup
1. Configure local Ollama daemon or ONNX runtime with `embeddinggemma` (300M).
2. Create `LectureVectorService.cs` implementing cosine similarity search over `LectureChunk` vectors in `VIIDII.db`.
3. Hook session conclusion (`EndSession`) to trigger chunking and vector indexing of live captions and chat transcripts.

### Phase 3: Gemma 2B Inference & "Ask Lecture AI" Interface
1. Add local LLM inference client (`OllamaClient` or `SemanticKernel`) with streaming responses.
2. Build the **"Ask Lecture AI"** tab in [`SessionView.razor`](file:///C:/Users/HomePC/Desktop/Coded/VIIDII/Components/Pages/SessionView.razor) for post-class receipt cards and recap screens.
3. Implement 1-click **"Generate 5 Practice Questions"** and **"Summarize Key Takeaways"**.

---

## 5. Architectural Advantages for VIIDII

* **Empathetic AI:** Protects students in African and emerging tech markets from punitive grading when electrical grids or cell towers fail.
* **Data-Efficient:** Students gain 24/7 interactive access to their professors' lectures without paying high mobile bandwidth costs for video streams.
* **100% On-Premise / Edge-Ready:** Zero monthly subscription costs or cloud vendor lock-in; runs self-contained on the host machine.
