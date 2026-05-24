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

    // Start webcam for lecturer
    async function startWebcam(sessionId) {
        console.log("[sessionInterop JS] [Lecturer] startWebcam requested for session:", sessionId);
        
        let video = document.getElementById("sessionVideo");
        if (!video) {
            console.log("[sessionInterop JS] [Lecturer] #sessionVideo element not found on first check. Waiting 100ms...");
            await new Promise(resolve => setTimeout(resolve, 100));
            video = document.getElementById("sessionVideo");
        }
        if (!video) {
            console.error("[sessionInterop JS] [Lecturer] Critical: Video element #sessionVideo not found");
            throw new Error("Video element #sessionVideo not found");
        }

        try {
            console.log("[sessionInterop JS] [Lecturer] Requesting camera and microphone access...");
            localStream = await navigator.mediaDevices.getUserMedia({
                video: { width: { ideal: 720 }, height: { ideal: 420 } },
                audio: true
            });

            console.log("[sessionInterop JS] [Lecturer] Camera stream successfully obtained. Stream ID:", localStream.id);
            video.srcObject = localStream;
            console.log("[sessionInterop JS] [Lecturer] Attaching stream to video element and playing...");
            await video.play();
            console.log("[sessionInterop JS] [Lecturer] Local video play started");

            // Initialize PeerJS as lecturer (using sessionId as peer ID)
            console.log("[sessionInterop JS] [Lecturer] Initializing PeerJS with ID:", sessionId);
            peer = new Peer(sessionId, {
                config: {
                    iceServers: [
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
                    ]
                }
            });

            peer.on("open", (id) => {
                console.log("[sessionInterop JS] [Lecturer] PeerJS server connection established. Peer ID:", id);
                // Notify Blazor that peer is ready
                if (dotNetRef) {
                    console.log("[sessionInterop JS] [Lecturer] Invoking Blazor callback: OnLecturerPeerReady");
                    dotNetRef.invokeMethodAsync('OnLecturerPeerReady', id);
                }
            });

            peer.on("connection", (conn) => {
                console.log("[sessionInterop JS] [Lecturer] PeerJS connection request received from student:", conn.peer);
                handleStudentConnection(conn);
            });

            peer.on("error", (err) => {
                console.error("[sessionInterop JS] [Lecturer] PeerJS error occurred:", err);
                handlePeerError(err);
            });

            return { success: true, peerId: sessionId };

        } catch (error) {
            console.error("[sessionInterop JS] [Lecturer] Failed to obtain local stream or initialize peer:", error);
            return { success: false, error: error.message };
        }
    }

    function handleStudentConnection(conn) {
        console.log("[sessionInterop JS] [Lecturer] handleStudentConnection called for student peer:", conn.peer);
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
                        audio: true 
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

        let video = document.getElementById("sessionVideo");
        if (!video) {
            console.log("[sessionInterop JS] [Student] #sessionVideo element not found on first check. Checking in loop...");
            for (let i = 0; i < 20 && !video; i++) {
                await new Promise(resolve => setTimeout(resolve, 100));
                video = document.getElementById("sessionVideo");
            }
        }
        if (!video) {
            console.error("[sessionInterop JS] [Student] Critical: Video element #sessionVideo not found after retry loop");
            throw new Error("Video element #sessionVideo not found");
        }

        if (peer && !peer.disconnected) {
            console.log("[sessionInterop JS] [Student] Student peer already exists and is active:", peer.id);
            return { success: true, peerId: peer.id };
        }

        console.log("[sessionInterop JS] [Student] Creating PeerJS instance for student...");
        peer = new Peer({
            config: {
                iceServers: [
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
                ]
            }
        });

        return new Promise((resolve, reject) => {
            peer.on("open", (id) => {
                console.log("[sessionInterop JS] [Student] PeerJS connection established. Student Peer ID:", id);
                
                // Notify Blazor with peer ID so it can send to SignalR
                if (dotNetRef) {
                    console.log("[sessionInterop JS] [Student] Invoking Blazor callback: OnStudentPeerReady");
                    dotNetRef.invokeMethodAsync('OnStudentPeerReady', id);
                }

                // Connect to lecturer to open the data channel.
                if (currentSessionId) {
                    console.log("[sessionInterop JS] [Student] Automatically connecting to lecturer peer ID:", currentSessionId);
                    connectToLecturer(currentSessionId);
                } else {
                    console.warn("[sessionInterop JS] [Student] Warning: currentSessionId not set, cannot connect to lecturer");
                }

                resolve({ success: true, peerId: id });
            });

            peer.on("call", (call) => {
                console.log("[sessionInterop JS] [Student] Incoming media call received from lecturer:", call.peer);
                handleIncomingCall(call, video);
            });

            peer.on("error", (err) => {
                console.error("[sessionInterop JS] [Student] PeerJS error in setupStudentPeer:", err);
                handlePeerError(err);
                reject(err);
            });
        });
    }

    function handleIncomingCall(call, video) {
        console.log("[sessionInterop JS] [Student] handleIncomingCall called. Answering incoming media call...");
        call.answer();

        console.log("[sessionInterop JS] [Student] Setting up 10-second timeout to verify stream reception...");
        let streamTimeout = setTimeout(() => {
            if (!isStreamAttached) {
                console.warn("[sessionInterop JS] [Student] Stream timeout! No stream received after 10s. Closing call to trigger reconnect.");
                call.close();
            }
        }, 10000);

        call.on("stream", (remoteStream) => {
            clearTimeout(streamTimeout);
            console.log("[sessionInterop JS] [Student] Stream event triggered. Remote stream ID:", remoteStream.id);

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
            if (dotNetRef) {
                console.log("[sessionInterop JS] [Student] Invoking Blazor callback: OnPeerError");
                dotNetRef.invokeMethodAsync('OnPeerError', err.type || 'unknown');
            }
        });

        window.currentCall = call;
    }

    // Connect student to lecturer peer
    function connectToLecturer(lecturerPeerId, attempt = 1, maxAttempts = 15) {
        console.log(`[sessionInterop JS] [Student] connectToLecturer: lecturerPeerId=${lecturerPeerId}, attempt ${attempt}/${maxAttempts}`);

        if (!peer || peer.disconnected) {
            console.warn("[sessionInterop JS] [Student] Student peer not initialized or is disconnected");
            return { success: false, error: "Peer not initialized" };
        }

        console.log("[sessionInterop JS] [Student] Establishing data connection to lecturer peer ID:", lecturerPeerId);
        const conn = peer.connect(lecturerPeerId);

        conn.on("open", () => {
            console.log("[sessionInterop JS] [Student] Data connection open to lecturer:", lecturerPeerId);
            console.log("[sessionInterop JS] [Student] Sending 'studentReady' handshake packet");
            conn.send({ type: "studentReady", studentId: peer.id });

            // Notify Blazor of successful connection
            if (dotNetRef) {
                console.log("[sessionInterop JS] [Student] Invoking Blazor callback: OnConnectedToLecturer");
                dotNetRef.invokeMethodAsync('OnConnectedToLecturer', lecturerPeerId);
            }
        });

        conn.on("data", (data) => {
            console.log("[sessionInterop JS] [Student] Received data from lecturer:", data);
            
            // Handle file chunks
            if (data.type === "fileChunk") {
                console.log("[sessionInterop JS] [Student] Received file chunk, processing...");
                handleFileChunk(data);
            }

            // Forward data to Blazor for handling
            if (dotNetRef && data.type === "fileChunk") {
                console.log("[sessionInterop JS] [Student] Forwarding file chunk received callback to Blazor");
                dotNetRef.invokeMethodAsync('OnFileChunkReceived', JSON.stringify(data));
            }
        });

        conn.on("error", (err) => {
            console.error("[sessionInterop JS] [Student] Data connection error with lecturer:", err);
            if (attempt < maxAttempts && err.type === "peer-unavailable") {
                console.warn(`[sessionInterop JS] [Student] Lecturer peer unavailable. Retrying connection (attempt ${attempt + 1}/${maxAttempts}) in 3000ms...`);
                setTimeout(() => connectToLecturer(lecturerPeerId, attempt + 1, maxAttempts), 3000);
            } else {
                console.error("[sessionInterop JS] [Student] Max connection attempts reached or fatal data channel error:", err);
                if (dotNetRef) {
                    console.log("[sessionInterop JS] [Student] Invoking Blazor callback: OnConnectionFailed");
                    dotNetRef.invokeMethodAsync('OnConnectionFailed', err.type || 'unknown');
                }
            }
        });

        return { success: true };
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
        setupTabVisibilityListener: setupTabVisibilityListener
    };
})();
