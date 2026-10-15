// sessionInterop.js - Media and Peer management for Blazor interop
// This file handles ONLY webcam, screen sharing, and PeerJS connections
// SignalR and all business logic is handled in Blazor C#

window.sessionInterop = (function () {
    // Module state
    let localStream = null;
    let peer = null;
    let isStreamAttached = false;
    let attachedStreamId = null;
    let studentPeers = [];
    let studentConnections = new Map();
    let originalStream = null;
    let dotNetRef = null; // Reference to Blazor component for callbacks
    let currentSessionId = null;
    let isLecturer = false;

    // Initialize session context (called by Blazor)
    function initialize(sessionId, isLecturerRole, dotNetReference) {
        console.log(`[sessionInterop JS] Initializing session. SessionId: ${sessionId}, isLecturer: ${isLecturerRole}`);
        currentSessionId = sessionId;
        isLecturer = isLecturerRole;
        dotNetRef = dotNetReference;

        // Setup beforeunload cleanup
        if (!window.sessionInteropCleanupRegistered) {
            window.addEventListener('beforeunload', () => {
                console.log("[sessionInterop JS] beforeunload event triggered, cleaning up session resources...");
                cleanup();
            });
            window.sessionInteropCleanupRegistered = true;
        }

        return true;
    }

    /**
 * ICE server configuration.
 *
 * Loaded from the server at runtime so switching from public relays to a
 * self-hosted coturn is a configuration change, not a code change and rebuild.
 *
 * Supply via, in order of preference:
 *   1. window.VIIDII_ICE_SERVERS  (injected by the host page)
 *   2. GET /api/config/ice
 *   3. built-in public relays (DEVELOPMENT ONLY - rate limited, will fail under load)
 *
 * Production requires a self-hosted TURN server. See docs/TURN_AND_MEDIA.md.
 */
    let iceServersCache = null;

    // What the student is actually seeing, in words they can report from a phone.
    //
    // A student on mobile data cannot open a developer console, so "it didn't work"
    // used to be the whole of the feedback available. This surfaces the stage the
    // connection reached, which is the difference between a signalling problem and
    // a relay problem: two completely different fixes.
    let mediaState = { stage: "starting", detail: "", at: Date.now() };

    function reportMediaState(stage, detail) {
        mediaState = { stage, detail: detail || "", at: Date.now() };
        console.log("[sessionInterop JS] Media state:", stage, detail);
        if (dotNetRef) {
            // The source is sent so the panel can say which stack reported. The
            // LiveKit path shares this callback, and a stage text without a source
            // cannot be attributed reliably.
            dotNetRef.invokeMethodAsync('OnMediaState', stage, detail || "", "peerjs");
        }
    }

    function getMediaState() {
        return mediaState;
    }

    // Counts the local candidates the browser actually produced. This is the
    // single most useful fact when a connection will not come up:
    //   host only, no "public"  -> no STUN server answered, we never learnt our
    //                              own address, so there is no path off the LAN
    //   relay > 0                -> it went through TURN and can work anywhere
    //   none at all              -> gathering never even started
    async function describeCandidates(pc) {
        if (!pc || !pc.getStats) return "";
        try {
            const report = await pc.getStats();
            const counts = { host: 0, srflx: 0, prflx: 0, relay: 0 };
            let seen = 0;
            report.forEach((entry) => {
                if (entry && entry.type === "local-candidate" && entry.candidateType) {
                    if (counts[entry.candidateType] !== undefined) counts[entry.candidateType]++;
                    seen++;
                }
            });
            if (!seen) return "none found";
            const parts = [];
            if (counts.host) parts.push("host " + counts.host);
            if (counts.srflx) parts.push("public " + counts.srflx);
            if (counts.relay) parts.push("relay " + counts.relay);
            return parts.join(", ");
        } catch (err) {
            return "";
        }
    }

    // Finds the underlying RTCPeerConnection on a PeerJS DataConnection or
    // MediaConnection.
    //
    // PeerJS 1.5.4 does not expose it as "_pc". That was verified against the
    // real library: the connection object carries no "_pc" property at all, and
    // an earlier version of this code read "_pc", found nothing, and reported
    // "PeerJS never created a peer connection" on every single attempt whether
    // the connection was healthy or not. The property is "peerConnection".
    function getPeerConnection(conn) {
        if (!conn) return null;
        if (conn.peerConnection) return conn.peerConnection;
        if (conn._pc) return conn._pc;
        if (conn.pc) return conn.pc;
        const negotiator = conn._negotiator;
        if (negotiator) {
            if (negotiator.peerConnection) return negotiator.peerConnection;
            if (negotiator.pc) return negotiator.pc;
            const inner = negotiator.connection;
            if (inner) {
                if (inner.peerConnection) return inner.peerConnection;
                if (inner._pc) return inner._pc;
            }
        }
        return null;
    }

    // Reads the underlying RTCPeerConnection so we can say which kind of path was
    // negotiated. "relay" means it had to go through a TURN server, which is the
    // thing that breaks under real student traffic on the public relays.
    function describeIce(call) {
        try {
            const pc = getPeerConnection(call);
            if (!pc) return "";

            const stats = [];

            if (pc.iceConnectionState) {
                stats.push("ice=" + pc.iceConnectionState);
            }
            if (pc.iceGatheringState) {
                stats.push("gathering=" + pc.iceGatheringState);
            }
            if (pc.connectionState) {
                stats.push("rtc=" + pc.connectionState);
            }

            return stats.join(" ");
        } catch (err) {
            return "";
        }
    }

    // PeerJS creates the RTCPeerConnection after it hands us the connection, so
    // there is a window where it does not exist yet. Poll for it, generously:
    // the previous window was 2.4s, which on a phone over mobile data expires
    // before the signalling round-trip has even finished, so a slow connection
    // and a dead one were indistinguishable.
    function waitForPeerConnection(conn, attempts = 150, delayMs = 200) {
        return new Promise((resolve) => {
            let tries = 0;
            const tick = () => {
                const pc = getPeerConnection(conn);
                if (pc) return resolve(pc);
                if (tries++ >= attempts) return resolve(null);
                setTimeout(tick, delayMs);
            };
            tick();
        });
    }

    // Reports the ICE states and a watchdog. This is the instrument the video
    // failure never had. Without it a connection that stalls in "checking" is
    // indistinguishable from a slow one: "open", "data" and "error" all stay
    // silent, so the panel just stops on one line and nobody learns anything.
    // States go to the console to keep the panel readable; the panel gets the
    // failure and the stall, which are the two that matter.
    async function watchIce(conn, label, report, stallMs = 20000) {
        const pc = await waitForPeerConnection(conn);
        if (!pc) {
            console.warn(`[sessionInterop JS] ${label} never got an RTCPeerConnection`);
            report("connection stalled", "PeerJS never created a peer connection");
            return;
        }

        const snap = () => console.log(`[sessionInterop JS] ${label} ICE:`, describeIce(conn));
        pc.addEventListener("iceconnectionstatechange", () => {
            snap();
            if (pc.iceConnectionState === "failed") {
                report("connection failed", "ice=failed, no route between the two devices");
            }
        });
        pc.addEventListener("icegatheringstatechange", snap);
        pc.addEventListener("icecandidate", (e) => {
            if (e.candidate) {
                console.log(`[sessionInterop JS] ${label} candidate:`, e.candidate.type, e.candidate.protocol || "?");
            }
        });

        snap();

        setTimeout(async () => {
            const state = pc.iceConnectionState;
            if (state === "connected" || state === "completed") return;
            const candidates = await describeCandidates(pc);
            const detail = `still ${describeIce(conn) || state || "unknown"} after ${Math.round(stallMs / 1000)}s, candidates: ${candidates || "none found"}`;

            // ICE stuck on "new" with gathering already complete means the offer
            // and answer were never exchanged, so no media path was ever even
            // attempted. Adding a TURN server cannot fix that, because TURN is
            // only consulted once two peers have agreed to talk. The signalling
            // server is what is refusing.
            report(state === "new" ? "signalling never completed" : "connection stalled", detail);
        }, stallMs);
    }

    const PUBLIC_FALLBACK_ICE = [
        { urls: "stun:stun.l.google.com:19302" },
        { urls: "stun:freestun.net:3478" },
        {
            urls: ["turn:openrelay.metered.ca:80", "turn:openrelay.metered.ca:443"],
            username: "openrelayproject",
            credential: "openrelayproject"
        },
        {
            urls: "turn:freestun.net:3478",
            username: "free",
            credential: "free"
        }
    ];

    async function getIceServers() {
        if (iceServersCache) return iceServersCache;

        if (window.VIIDII_ICE_SERVERS && Array.isArray(window.VIIDII_ICE_SERVERS)) {
            iceServersCache = window.VIIDII_ICE_SERVERS;
            return iceServersCache;
        }

        try {
            const response = await fetch("/api/config/ice", {
                credentials: "same-origin",
                cache: "no-store"
            });
            if (response.ok) {
                const payload = await response.json();
                if (Array.isArray(payload.iceServers) && payload.iceServers.length > 0) {
                    iceServersCache = payload.iceServers;
                    return iceServersCache;
                }
            }
        } catch (err) {
            console.warn("[sessionInterop JS] Could not load ICE config from server; using public relays.", err);
        }

        console.warn(
            "[sessionInterop JS] Falling back to PUBLIC relay servers. These are rate limited " +
            "and WILL fail with real student traffic. Configure TURN_Servers for production."
        );
        iceServersCache = PUBLIC_FALLBACK_ICE;
        return iceServersCache;
    }

    /**
     * Polls the DOM until an element with the given id appears.
     * @param {string} elementId
     * @param {number} attempts
     * @param {number} delayMs
     * @returns {Promise<HTMLElement|null>}
     */
    async function waitForVideoElement(elementId, attempts, delayMs) {
        for (let i = 0; i < attempts; i++) {
            const el = document.getElementById(elementId);
            if (el) return el;
            console.log(`[sessionInterop JS] Waiting for #${elementId} (attempt ${i + 1}/${attempts})...`);
            await new Promise(resolve => setTimeout(resolve, delayMs));
        }
        return null;
    }

    // Start webcam for lecturer
    async function startWebcam(sessionId) {
        console.log("[sessionInterop JS] [Lecturer] startWebcam requested for session:", sessionId);

        // The <video> element may not exist yet: Blazor renders asynchronously, so the
        // element can land after this call fires. Poll briefly instead of throwing —
        // a thrown error surfaces to the user as a broken camera with no cause.
        const video = await waitForVideoElement("sessionVideo", 5, 200);
        if (!video) {
            console.error("[sessionInterop JS] [Lecturer] #sessionVideo never appeared");
            return {
                success: false,
                error: "Video element unavailable. Reload the page and try again."
            };
        }

        try {
            console.log("[sessionInterop JS] [Lecturer] Requesting camera access (video-only for local preview)...");
            localStream = await navigator.mediaDevices.getUserMedia({
                video: { width: { ideal: 720 }, height: { ideal: 420 } },
                audio: false
            });

            console.log("[sessionInterop JS] [Lecturer] Camera stream successfully obtained. Stream ID:", localStream.id);
            // Strict muting on local preview to prevent any audio feedback / loopback
            video.muted = true;
            video.defaultMuted = true;
            video.volume = 0;
            video.srcObject = localStream;
            console.log("[sessionInterop JS] [Lecturer] Attaching stream to video element and playing...");
            await video.play();
            console.log("[sessionInterop JS] [Lecturer] Local video play started");

            // No PeerJS peer is created here any more. This function now only obtains
            // the local camera stream so the lecturer can see themselves; publishing is
            // LiveKit's job (livekitInterop.js). It used to also register a PeerJS peer
            // named after the session and open a peer-to-peer mesh with every student,
            // which required the free public 0.peerjs.com cloud to carry the signalling
            // and had no relay fallback. That failed on every real phone and the code
            // had no way to point PeerJS anywhere else. Removing it here is what stops
            // a second, competing media stack from stalling behind LiveKit.
            console.log("[sessionInterop JS] [Lecturer] Local preview ready; publishing is handled by LiveKit.");

            if (dotNetRef) {
                dotNetRef.invokeMethodAsync('OnLecturerPeerReady', 'livekit');
            }

            return { success: true };

        } catch (error) {
            console.error("[sessionInterop JS] [Lecturer] Failed to obtain local stream or initialize peer:", error);
            return { success: false, error: error.message };
        }
    }

    function handleStudentConnection(conn) {
        console.log("[sessionInterop JS] [Lecturer] handleStudentConnection called for student peer:", conn.peer);

        // Same reasoning as the student side. The lecturer never places the media
        // call unless this data channel opens, so a stall here means no video at
        // all, for that student, with nothing on screen to say why.
        watchIce(conn, "[Lecturer]", (stage, detail) => reportMediaState(stage, detail));

        conn.on("open", () => {
            console.log("[sessionInterop JS] [Lecturer] Data channel open with student:", conn.peer);
            if (!conn.peer) {
                console.warn("[sessionInterop JS] [Lecturer] Warning: Student peer ID is invalid, skipping call");
                return;
            }
            studentConnections.set(conn.peer, conn);

            console.log("[sessionInterop JS] [Lecturer] Initiating WebRTC call to student:", conn.peer);
            const call = peer.call(conn.peer, localStream);
            call.on("open", () => {
                console.log("[sessionInterop JS] [Lecturer] WebRTC media call to student opened:", conn.peer);
                // Notify Blazor of successful connection
                if (dotNetRef) {
                    console.log("[sessionInterop JS] [Lecturer] Invoking Blazor callback: OnStudentConnected");
                    dotNetRef.invokeMethodAsync('OnStudentConnected', conn.peer);
                }
            });
            call.on("error", (err) => console.error("[sessionInterop JS] [Lecturer] Media call error to student:", conn.peer, err));
        });

        conn.on("data", (data) => {
            console.log("[sessionInterop JS] [Lecturer] Received data message from student:", conn.peer, data);
            if (data.type === "studentReady" && !studentPeers.includes(data.studentId)) {
                console.log(`[sessionInterop JS] [Lecturer] Adding student ID to participant peers: ${data.studentId}`);
                studentPeers.push(data.studentId);
            } else if (data.type === "fileChunk") {
                console.log("[sessionInterop JS] [Lecturer] Forwarding fileChunk to Blazor");
                // Forward file chunk data to Blazor for handling
                if (dotNetRef) {
                    dotNetRef.invokeMethodAsync('OnFileChunkReceived', JSON.stringify(data));
                }
            }
        });

        conn.on("close", () => {
            console.log("[sessionInterop JS] [Lecturer] Data channel closed by student:", conn.peer);
            studentConnections.delete(conn.peer);
            studentPeers = studentPeers.filter(id => id !== conn.peer);
            
            // Notify Blazor of disconnection
            if (dotNetRef) {
                console.log("[sessionInterop JS] [Lecturer] Invoking Blazor callback: OnStudentDisconnected");
                dotNetRef.invokeMethodAsync('OnStudentDisconnected', conn.peer);
            }
        });
    }

    // Start screen sharing for lecturer
    async function startScreenShare() {
        console.log("[sessionInterop JS] [Lecturer] startScreenShare requested");
        
        if (!isLecturer) {
            console.error("[sessionInterop JS] [Lecturer] Error: Only lecturers can share screen");
            throw new Error("Only lecturers can share screen");
        }

        try {
            console.log("[sessionInterop JS] [Lecturer] Requesting display media for screen share...");
            const screenStream = await navigator.mediaDevices.getDisplayMedia({
                audio: false,
                video: true,
            });

            console.log("[sessionInterop JS] [Lecturer] Screen capture stream obtained:", screenStream.id);

            if (!originalStream) {
                console.log("[sessionInterop JS] [Lecturer] Saving original webcam stream to revert later");
                originalStream = localStream;
            }
            localStream = screenStream;

            const video = document.getElementById("sessionVideo");
            if (video) {
                console.log("[sessionInterop JS] [Lecturer] Setting screen stream on #sessionVideo element");
                video.srcObject = screenStream;
            }

            // Notify Blazor of stream change so it can notify students via SignalR
            if (dotNetRef) {
                console.log("[sessionInterop JS] [Lecturer] Invoking Blazor callback: OnStreamTypeChanged ('screenshare')");
                await dotNetRef.invokeMethodAsync('OnStreamTypeChanged', 'screenshare');
            }

            // Restart calls with screen stream
            console.log("[sessionInterop JS] [Lecturer] Restarting student media calls with new screen sharing stream");
            restartCallsWithNewStream(localStream);

            // Handle screen sharing stop
            screenStream.getVideoTracks()[0].addEventListener('ended', async () => {
                console.log("[sessionInterop JS] [Lecturer] Screen sharing ended by browser UI. Reverting to webcam...");
                try {
                    const webcamStream = originalStream || await navigator.mediaDevices.getUserMedia({ 
                        video: true, 
                        audio: false 
                    });
                    await switchToWebcam(webcamStream);
                } catch (err) {
                    console.error("[sessionInterop JS] [Lecturer] Failed to revert to webcam:", err);
                    throw err;
                }
            });

            return { success: true };

        } catch (error) {
            console.error("[sessionInterop JS] [Lecturer] Error in startScreenShare:", error);
            return { success: false, error: error.message };
        }
    }

    async function switchToWebcam(webcamStream) {
        console.log("[sessionInterop JS] [Lecturer] switchToWebcam called");
        localStream = webcamStream;
        const video = document.getElementById("sessionVideo");
        if (video) {
            video.muted = true;
            video.defaultMuted = true;
            video.volume = 0;
            video.srcObject = localStream;
            console.log("[sessionInterop JS] [Lecturer] Switched back to webcam stream.");
        }

        // Notify Blazor of stream change
        if (dotNetRef) {
            console.log("[sessionInterop JS] [Lecturer] Invoking Blazor callback: OnStreamTypeChanged ('webcam')");
            await dotNetRef.invokeMethodAsync('OnStreamTypeChanged', 'webcam');
        }

        console.log("[sessionInterop JS] [Lecturer] Restarting student media calls with reverted webcam stream");
        restartCallsWithNewStream(localStream);
    }

    function restartCallsWithNewStream(stream) {
        console.log("[sessionInterop JS] [Lecturer] restartCallsWithNewStream called for", studentPeers.length, "connected students");
        for (const studentId of studentPeers) {
            const conn = studentConnections.get(studentId);
            if (conn) {
                console.log(`[sessionInterop JS] [Lecturer] Restarting media call to student: ${studentId}`);
                const call = peer.call(studentId, stream);
                call.on("open", () => console.log(`[sessionInterop JS] [Lecturer] Call restarted successfully for: ${studentId}`));
                call.on("error", (err) => console.error(`[sessionInterop JS] [Lecturer] Call restart error for student ${studentId}:`, err));
            }
        }
    }

    // Setup peer connection for student
    async function setupStudentPeer() {
        console.log("[sessionInterop JS] [Student] setupStudentPeer called");

        // Retained only so the call sites in SessionView.razor keep working. There is
        // no PeerJS peer to set up: this used to register with 0.peerjs.com and dial
        // the lecturer's peer directly, which is the path that failed on every real
        // phone. Subscription now belongs to LiveKit, which supplies its own signalling
        // and relays. Returning a distinct peerId stops the lecturer treating this as a
        // PeerJS peer in the participant list.
        return { success: true, peerId: null };
    }

    function handleIncomingCall(call, video) {
        console.log("[sessionInterop JS] [Student] handleIncomingCall called. Answering incoming media call...");
        reportMediaState("call answered", "waiting for media " + describeIce(call));
        call.answer();

        // ICE goes new -> checking -> connected. If it sits on checking it is
        // failing to find a path, which on a real phone means the relay did not
        // come back. Poll it so the failure is visible instead of silent.
        const iceTimer = setInterval(() => {
            const state = describeIce(call);
            if (state) reportMediaState("negotiating", state);
        }, 2000);

        console.log("[sessionInterop JS] [Student] Setting up 10-second timeout to verify stream reception...");
        let streamTimeout = setTimeout(() => {
            if (!isStreamAttached) {
                const state = describeIce(call);
                console.warn("[sessionInterop JS] [Student] Stream timeout! No stream received after 10s. Closing call to trigger reconnect.");
                reportMediaState("no media after 10s", state || "connection never completed");
                clearInterval(iceTimer);
                call.close();
            }
        }, 10000);

        call.on("stream", (remoteStream) => {
            clearTimeout(streamTimeout);
            console.log("[sessionInterop JS] [Student] Stream event triggered. Remote stream ID:", remoteStream.id);
            reportMediaState("stream received", "connected");

            if (remoteStream.id === attachedStreamId) {
                console.log("[sessionInterop JS] [Student] Ignoring duplicate stream with ID:", remoteStream.id);
                return;
            }

            if (isStreamAttached) {
                console.log("[sessionInterop JS] [Student] Stream already attached, ignoring new stream ID:", remoteStream.id);
                return;
            }

            isStreamAttached = true;
            attachedStreamId = remoteStream.id;

            if (video) {
                console.log("[sessionInterop JS] [Student] Attaching remote stream to video element and calling play()...");
                video.srcObject = remoteStream;
                video.play().then(() => {
                    console.log("[sessionInterop JS] [Student] Remote stream playback started successfully");
                }).catch(err => {
                    console.error("[sessionInterop JS] [Student] Remote stream playback failed:", err.message);
                });

                // Notify Blazor that stream is attached
                if (dotNetRef) {
                    console.log("[sessionInterop JS] [Student] Invoking Blazor callback: OnStreamReceived");
                    dotNetRef.invokeMethodAsync('OnStreamReceived');
                }
            }
        });

        call.on("close", () => {
            console.log("[sessionInterop JS] [Student] Call closed by lecturer or timeout.");
            window.currentCall = null;
            isStreamAttached = false;
            attachedStreamId = null;
            if (video) {
                console.log("[sessionInterop JS] [Student] Clearing video element source object");
                video.srcObject = null;
            }

            // Notify Blazor
            if (dotNetRef) {
                console.log("[sessionInterop JS] [Student] Invoking Blazor callback: OnStreamLost");
                dotNetRef.invokeMethodAsync('OnStreamLost');
            }
        });

        call.on("error", (err) => {
            console.error("[sessionInterop JS] [Student] Call stream error:", err);
            reportMediaState("call error", err.type || "unknown");
            clearInterval(iceTimer);
            if (dotNetRef) {
                console.log("[sessionInterop JS] [Student] Invoking Blazor callback: OnPeerError");
                dotNetRef.invokeMethodAsync('OnPeerError', err.type || 'unknown');
            }
        });

        window.currentCall = call;
    }

    // Connect student to lecturer peer
    function connectToLecturer(lecturerPeerId) {
        // Deliberately inert. This opened a direct data channel to the lecturer's
        // PeerJS peer and is the call site that produced
        // "connection stalled ... ice=disconnected" on every real phone. It is kept as
        // a no-op because SessionView.razor still calls it, and a removed export would
        // fail loudly at runtime rather than quietly. Nothing dials anybody from here:
        // LiveKit handles both signalling and relays.
        console.warn("[sessionInterop JS] [Student] connectToLecturer is disabled. LiveKit handles media.");
        return { success: false, error: "Peer-to-peer connections are no longer used." };
    }

    // Handle stream change notification from Blazor (when lecturer switches)
    function handleStreamChange(streamType) {
        console.log(`[sessionInterop JS] handleStreamChange notified by Blazor: ${streamType}`);
        
        if (window.currentCall) {
            console.log("[sessionInterop JS] Resetting stream state flags for new stream type");
            isStreamAttached = false;
            attachedStreamId = null;
            const video = document.getElementById("sessionVideo");
            if (video) {
                console.log("[sessionInterop JS] Clearing current video element source for transition.");
                video.srcObject = null;
            }
        }
    }

    function handlePeerError(err) {
        console.warn("[sessionInterop JS] handlePeerError invoked with type:", err.type);
        if (err.type === "peer-unavailable") {
            console.warn("[sessionInterop JS] Peer was not found / peer-unavailable");
        } else if (err.type === "server-disconnected") {
            console.warn("[sessionInterop JS] Connection to PeerServer lost, attempting reconnect...");
            if (peer) {
                peer.reconnect();
            }
        }

        // Notify Blazor of error
        if (dotNetRef) {
            console.log("[sessionInterop JS] Invoking Blazor callback: OnPeerError");
            dotNetRef.invokeMethodAsync('OnPeerError', err.type || 'unknown');
        }
    }

    // File chunk handling (for students receiving files)
    const fileChunks = new Map(); // Map<messageId, Array<ArrayBuffer>>

    function handleFileChunk(data) {
        const { messageId, fileName, fileSize, chunk, index, total } = data;
        const fileKey = messageId;

        console.log(`[sessionInterop JS] [Student] Received chunk ${index + 1}/${total} for file: ${fileName}`);

        // Initialize chunk array if first chunk
        if (!fileChunks.has(fileKey)) {
            fileChunks.set(fileKey, new Array(total));
            console.log(`[sessionInterop JS] [Student] Initialized chunk array for ${fileName}, expecting ${total} chunks`);
        }

        // Store chunk
        fileChunks.get(fileKey)[index] = chunk;

        const received = fileChunks.get(fileKey).filter(c => c !== undefined).length;
        console.log(`[sessionInterop JS] [Student] Progress: ${received}/${total} chunks received for ${fileName}`);

        // Check if all chunks received
        if (received === total) {
            console.log(`[sessionInterop JS] [Student] All chunks received for ${fileName}, reassembling...`);
            reassembleFile(fileKey, fileName, fileChunks.get(fileKey));
            fileChunks.delete(fileKey);
        }
    }

    function reassembleFile(messageId, fileName, chunks) {
        try {
            // Create blob from chunks
            const blob = new Blob(chunks);
            console.log(`[sessionInterop JS] [Student] File ${fileName} reassembled, size: ${blob.size} bytes`);

            // Create download URL
            const url = URL.createObjectURL(blob);

            // Find download button and update it
            const downloadBtn = document.querySelector(`[data-file-id="${messageId}"]`);
            if (downloadBtn) {
                downloadBtn.href = url;
                downloadBtn.download = fileName;
                downloadBtn.textContent = "Download";
                downloadBtn.disabled = false;
                console.log(`[sessionInterop JS] [Student] Download button updated for ${fileName}`);
            }

            // Auto-download
            const tempLink = document.createElement("a");
            tempLink.href = url;
            tempLink.download = fileName;
            document.body.appendChild(tempLink);
            tempLink.click();
            document.body.removeChild(tempLink);
            console.log(`[sessionInterop JS] [Student] Auto-download triggered for ${fileName}`);

            // Notify Blazor
            if (dotNetRef) {
                console.log("[sessionInterop JS] [Student] Invoking Blazor callback: OnFileDownloadComplete");
                dotNetRef.invokeMethodAsync('OnFileDownloadComplete', messageId, fileName);
            }
        } catch (err) {
            console.error(`[sessionInterop JS] [Student] Error reassembling file ${fileName}:`, err);
            if (dotNetRef) {
                console.log("[sessionInterop JS] [Student] Invoking Blazor callback: OnFileDownloadError");
                dotNetRef.invokeMethodAsync('OnFileDownloadError', messageId, err.message);
            }
        }
    }

    // Send data to all connected students (for file sharing)
    function sendDataToPeers(data) {
        if (!isLecturer) {
            console.error("[sessionInterop JS] [Lecturer] Error: Only lecturer can send data to peers");
            return { success: false, error: "Not lecturer" };
        }

        if (studentPeers.length === 0) {
            console.warn("[sessionInterop JS] [Lecturer] Warning: No students connected to send data to");
            return { success: false, error: "No students connected", sentTo: 0, total: 0 };
        }

        let successCount = 0;
        let failedPeers = [];

        console.log(`[sessionInterop JS] [Lecturer] Sending data packet to ${studentPeers.length} student peers...`);
        for (const studentId of studentPeers) {
            const conn = studentConnections.get(studentId);
            if (conn && conn.open) {
                try {
                    conn.send(data);
                    successCount++;
                } catch (err) {
                    console.error(`[sessionInterop JS] [Lecturer] Failed to send to student ${studentId}:`, err);
                    failedPeers.push(studentId);
                }
            } else {
                console.warn(`[sessionInterop JS] [Lecturer] Connection not open for student ${studentId}`);
                failedPeers.push(studentId);
            }
        }

        return { 
            success: successCount > 0, 
            sentTo: successCount, 
            total: studentPeers.length,
            failed: failedPeers.length,
            failedPeers: failedPeers
        };
    }

    // Send file to all students in chunks
    async function sendFileToStudents(file, messageId) {
        if (!isLecturer) {
            throw new Error("Only lecturer can send files");
        }

        if (studentPeers.length === 0) {
            throw new Error("No students connected");
        }

        const chunkSize = 1024 * 1024; // 1MB chunks
        const totalChunks = Math.ceil(file.size / chunkSize);
        let sentChunks = 0;

        console.log(`[sessionInterop JS] [Lecturer] Starting file transfer. Name: ${file.name}, Size: ${file.size} bytes, Chunks: ${totalChunks}`);

        // Read file in chunks and send to all students
        for (let i = 0; i < totalChunks; i++) {
            const start = i * chunkSize;
            const end = Math.min(start + chunkSize, file.size);
            const chunk = file.slice(start, end);

            // Convert chunk to ArrayBuffer
            const arrayBuffer = await chunk.arrayBuffer();

            const chunkData = {
                type: "fileChunk",
                fileName: file.name,
                fileSize: file.size,
                chunk: arrayBuffer,
                index: i,
                total: totalChunks,
                messageId: messageId
            };

            // Send to all connected students
            const result = sendDataToPeers(chunkData);
            
            if (result.success) {
                sentChunks++;
                const progress = (sentChunks / totalChunks) * 100;
                
                // Notify Blazor of progress
                if (dotNetRef) {
                    dotNetRef.invokeMethodAsync('OnFileUploadProgress', progress, sentChunks, totalChunks);
                }

                console.log(`[sessionInterop JS] [Lecturer] Chunk ${i + 1}/${totalChunks} sent successfully to ${result.sentTo} students`);
            } else {
                console.error(`[sessionInterop JS] [Lecturer] Failed to send chunk ${i + 1}/${totalChunks}`);
            }

            // Small delay between chunks to prevent overwhelming
            await new Promise(resolve => setTimeout(resolve, 10));
        }

        console.log(`[sessionInterop JS] [Lecturer] File transfer complete: ${file.name}`);
        return { success: true, totalChunks: totalChunks, fileName: file.name };
    }

    // Cleanup all session resources
    function cleanup() {
        console.log("[sessionInterop JS] cleanup starting");

        // Stop local stream
        if (localStream) {
            console.log("[sessionInterop JS] Stopping local stream tracks");
            localStream.getTracks().forEach(track => {
                console.log("[sessionInterop JS] Stopping local track:", track.kind);
                track.stop();
            });
            localStream = null;
        }

        // Stop original stream if exists
        if (originalStream) {
            console.log("[sessionInterop JS] Stopping original stream tracks");
            originalStream.getTracks().forEach(track => {
                console.log("[sessionInterop JS] Stopping original track:", track.kind);
                track.stop();
            });
            originalStream = null;
        }

        // Destroy peer connection
        if (peer) {
            console.log("[sessionInterop JS] Destroying PeerJS instance");
            peer.destroy();
            peer = null;
        }

        // Clear video element
        const video = document.getElementById("sessionVideo");
        if (video) {
            console.log("[sessionInterop JS] Clearing #sessionVideo srcObject");
            video.srcObject = null;
        }

        // Clear state
        isStreamAttached = false;
        attachedStreamId = null;
        studentPeers = [];
        studentConnections.clear();
        dotNetRef = null;
        currentSessionId = null;

        console.log("[sessionInterop JS] cleanup completed successfully");
    }

    // Check if webcam is initialized
    function isWebcamInitialized() {
        return localStream !== null && localStream.active;
    }

    // Device detection helpers
    function isMobile() {
        return /Android|iPhone|iPad|iPod|BlackBerry|IEMobile|Opera Mini/i.test(navigator.userAgent);
    }

    async function getBatteryLevel() {
        if ('getBattery' in navigator) {
            try {
                const battery = await navigator.getBattery();
                return {
                    level: Math.round(battery.level * 100),
                    charging: battery.charging
                };
            } catch (err) {
                console.error("[sessionInterop JS] Battery API error:", err);
                return { level: -1, charging: false, error: err.message };
            }
        } else {
            return { level: -1, charging: false, error: "Battery API not supported" };
        }
    }

    async function getNetworkStatus() {
        if ('connection' in navigator) {
            const conn = navigator.connection || navigator.mozConnection || navigator.webkitConnection;
            return {
                effectiveType: conn.effectiveType || 'unknown',
                downlink: conn.downlink || -1,
                rtt: conn.rtt || -1,
                saveData: conn.saveData || false
            };
        } else {
            // Fallback: measure latency with a test fetch
            const start = Date.now();
            try {
                await fetch('https://www.google.com/favicon.ico', { 
                    mode: 'no-cors', 
                    cache: 'no-store' 
                });
                const latency = Date.now() - start;
                return {
                    effectiveType: latency > 2000 ? 'slow' : latency > 500 ? '3g' : '4g',
                    downlink: -1,
                    rtt: latency,
                    saveData: false,
                    measured: true
                };
            } catch (err) {
                return {
                    effectiveType: 'offline',
                    downlink: -1,
                    rtt: -1,
                    saveData: false,
                    error: err.message
                };
            }
        }
    }

    function isTabVisible() {
        return !document.hidden;
    }

    function saveSessionState(state) {
        sessionStorage.setItem('sessionState', JSON.stringify(state ?? {}));
    }

    function restoreSessionState() {
        const raw = sessionStorage.getItem('sessionState');
        return raw ? JSON.parse(raw) : null;
    }

    function clearSessionState() {
        sessionStorage.removeItem('sessionState');
    }

    // Setup tab visibility listener (reports to Blazor)
    function setupTabVisibilityListener() {
        if (window.sessionInteropVisibilityRegistered) {
            return;
        }

        console.log("[sessionInterop JS] Registering visibilitychange event listener");
        document.addEventListener('visibilitychange', () => {
            if (dotNetRef) {
                const isVisible = !document.hidden;
                console.log("[sessionInterop JS] Tab visibility changed, isVisible:", isVisible);
                dotNetRef.invokeMethodAsync('OnTabVisibilityChanged', isVisible);
            }
        });

        window.sessionInteropVisibilityRegistered = true;
    }

    let telemetryWatcher = null;
    let cachedTelemetry = {
        batteryLevel: -1,
        isCharging: false,
        rtt: -1,
        effectiveType: 'unknown'
    };

    function startPassiveTelemetryWatch(dotNetRef, sessionId, matricNo) {
        stopPassiveTelemetryWatch();

        // 1. Passive Battery Event Listener (zero polling, OS push notifications only)
        if ('getBattery' in navigator) {
            navigator.getBattery().then(battery => {
                const onBatteryChange = () => {
                    const level = Math.round(battery.level * 100);
                    const charging = battery.charging;
                    cachedTelemetry.batteryLevel = level;
                    cachedTelemetry.isCharging = charging;

                    // Critical threshold: battery <= 8% and discharging
                    if (level > 0 && level <= 8 && !charging) {
                        console.log(`[PassiveTelemetry] Auto-flagging low battery: ${level}%`);
                        if (dotNetRef && dotNetRef.invokeMethodAsync) {
                            dotNetRef.invokeMethodAsync('OnPassiveTelemetryAlert', 'BatteryLow', level);
                        }
                    }
                };

                onBatteryChange();
                battery.addEventListener('levelchange', onBatteryChange);
                battery.addEventListener('chargingchange', onBatteryChange);

                telemetryWatcher = telemetryWatcher || {};
                telemetryWatcher.battery = battery;
                telemetryWatcher.batteryListener = onBatteryChange;
            }).catch(() => {});
        }

        // 2. Passive Network Event Listener (fires only on network switch / drop)
        const conn = navigator.connection || navigator.mozConnection || navigator.webkitConnection;
        if (conn) {
            const onNetworkChange = () => {
                const rtt = conn.rtt || -1;
                const effectiveType = conn.effectiveType || 'unknown';
                cachedTelemetry.rtt = rtt;
                cachedTelemetry.effectiveType = effectiveType;

                // Critical threshold: RTT >= 1500ms or slow-2g
                if (rtt >= 1500 || effectiveType === 'slow-2g') {
                    console.log(`[PassiveTelemetry] Auto-flagging network distress: ${rtt}ms RTT, ${effectiveType}`);
                    if (dotNetRef && dotNetRef.invokeMethodAsync) {
                        dotNetRef.invokeMethodAsync('OnPassiveTelemetryAlert', 'DataFinished', rtt);
                    }
                }
            };

            onNetworkChange();
            conn.addEventListener('change', onNetworkChange);

            telemetryWatcher = telemetryWatcher || {};
            telemetryWatcher.conn = conn;
            telemetryWatcher.networkListener = onNetworkChange;
        }

        // 3. Emergency Disconnect Beacon (pagehide / unload)
        const onPageHide = () => {
            if (!sessionId || !matricNo) return;
            try {
                const payload = JSON.stringify({
                    sessionId: sessionId,
                    matricNo: matricNo,
                    batteryLevel: cachedTelemetry.batteryLevel,
                    isCharging: cachedTelemetry.isCharging,
                    rtt: cachedTelemetry.rtt,
                    effectiveType: cachedTelemetry.effectiveType
                });
                const blob = new Blob([payload], { type: 'application/json' });
                navigator.sendBeacon('/api/session/telemetry/beacon', blob);
            } catch (e) {}
        };

        window.addEventListener('pagehide', onPageHide);
        telemetryWatcher = telemetryWatcher || {};
        telemetryWatcher.onPageHide = onPageHide;
    }

    function stopPassiveTelemetryWatch() {
        if (!telemetryWatcher) return;
        if (telemetryWatcher.battery && telemetryWatcher.batteryListener) {
            telemetryWatcher.battery.removeEventListener('levelchange', telemetryWatcher.batteryListener);
            telemetryWatcher.battery.removeEventListener('chargingchange', telemetryWatcher.batteryListener);
        }
        if (telemetryWatcher.conn && telemetryWatcher.networkListener) {
            telemetryWatcher.conn.removeEventListener('change', telemetryWatcher.networkListener);
        }
        if (telemetryWatcher.onPageHide) {
            window.removeEventListener('pagehide', telemetryWatcher.onPageHide);
        }
        telemetryWatcher = null;
    }

    // Public API - only media and peer functions
    return {
        initialize: initialize,
        startWebcam: startWebcam,
        startScreenShare: startScreenShare,
        setupStudentPeer: setupStudentPeer,
        connectToLecturer: connectToLecturer,
        handleStreamChange: handleStreamChange,
        sendDataToPeers: sendDataToPeers,
        sendFileToStudents: sendFileToStudents,
        cleanup: cleanup,
        isWebcamInitialized: isWebcamInitialized,
        // Device detection
        isMobile: isMobile,
        getBatteryLevel: getBatteryLevel,
        getNetworkStatus: getNetworkStatus,
        isTabVisible: isTabVisible,
        saveSessionState: saveSessionState,
        restoreSessionState: restoreSessionState,
        clearSessionState: clearSessionState,
        setupTabVisibilityListener: setupTabVisibilityListener,
        startPassiveTelemetryWatch: startPassiveTelemetryWatch,
        stopPassiveTelemetryWatch: stopPassiveTelemetryWatch
    };
})();
