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
        console.log(`Initializing sessionInterop: ${sessionId}, isLecturer: ${isLecturerRole}`);
        currentSessionId = sessionId;
        isLecturer = isLecturerRole;
        dotNetRef = dotNetReference;

        // Setup beforeunload cleanup
        if (!window.sessionInteropCleanupRegistered) {
            window.addEventListener('beforeunload', () => {
                cleanup();
            });
            window.sessionInteropCleanupRegistered = true;
        }

        return true;
    }

    // Start webcam for lecturer
    async function startWebcam(sessionId) {
        console.log("Starting webcam for lecturer:", sessionId);
        
        const video = document.getElementById("sessionVideo");
        if (!video) {
            console.error("Video element #sessionVideo not found - waiting for DOM...");
            // Wait a bit and try again
            await new Promise(resolve => setTimeout(resolve, 200));
            const videoRetry = document.getElementById("sessionVideo");
            if (!videoRetry) {
                console.error("Video element still not found after retry");
                return { success: false, error: "Video element #sessionVideo not found. Make sure the session view is rendered." };
            }
            // Use the retry element
            return startWebcamWithElement(sessionId, videoRetry);
        }

        return startWebcamWithElement(sessionId, video);
    }

    async function startWebcamWithElement(sessionId, video) {
        try {
            localStream = await navigator.mediaDevices.getUserMedia({
                video: { width: { ideal: 720 }, height: { ideal: 420 } },
                audio: true
            });

            console.log("Successfully obtained local stream:", localStream);
            video.srcObject = localStream;
            await video.play();

            // Initialize PeerJS as lecturer (using sessionId as peer ID)
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
                console.log("Lecturer peer open:", id);
                // Notify Blazor that peer is ready
                if (dotNetRef) {
                    dotNetRef.invokeMethodAsync('OnLecturerPeerReady', id);
                }
            });

            peer.on("connection", (conn) => {
                console.log("Student connection received:", conn.peer);
                handleStudentConnection(conn);
            });

            peer.on("error", (err) => {
                console.error("Peer error:", err);
                handlePeerError(err);
            });

            return { success: true, peerId: sessionId };

        } catch (error) {
            console.error("Failed to get local stream:", error);
            return { success: false, error: error.message };
        }
    }

    function handleStudentConnection(conn) {
        conn.on("open", () => {
            console.log("Student connected, peer ID:", conn.peer);
            if (!conn.peer) {
                console.warn("Invalid student peer ID, skipping call");
                return;
            }
            studentConnections.set(conn.peer, conn);

            const call = peer.call(conn.peer, localStream);
            call.on("open", () => {
                console.log("Call to student opened:", conn.peer);
                // Notify Blazor of successful connection
                if (dotNetRef) {
                    dotNetRef.invokeMethodAsync('OnStudentConnected', conn.peer);
                }
            });
            call.on("error", (err) => console.error("Call error:", err));
        });

        conn.on("data", (data) => {
            console.log("Received student data:", data);
            if (data.type === "studentReady" && !studentPeers.includes(data.studentId)) {
                console.log(`Adding student peer ID: ${data.studentId}`);
                studentPeers.push(data.studentId);
            } else if (data.type === "fileChunk") {
                // Forward file chunk data to Blazor for handling
                if (dotNetRef) {
                    dotNetRef.invokeMethodAsync('OnFileChunkReceived', JSON.stringify(data));
                }
            }
        });

        conn.on("close", () => {
            console.log("Student connection closed:", conn.peer);
            studentConnections.delete(conn.peer);
            studentPeers = studentPeers.filter(id => id !== conn.peer);
            
            // Notify Blazor of disconnection
            if (dotNetRef) {
                dotNetRef.invokeMethodAsync('OnStudentDisconnected', conn.peer);
            }
        });
    }

    // Start screen sharing for lecturer
    async function startScreenShare() {
        console.log("Starting screen share");
        
        if (!isLecturer) {
            throw new Error("Only lecturers can share screen");
        }

        try {
            const screenStream = await navigator.mediaDevices.getDisplayMedia({
                audio: false,
                video: true,
            });

            console.log("Screen captured:", screenStream);

            if (!originalStream) {
                originalStream = localStream;
            }
            localStream = screenStream;

            const video = document.getElementById("sessionVideo");
            if (video) {
                video.srcObject = screenStream;
            }

            // Notify Blazor of stream change so it can notify students via SignalR
            if (dotNetRef) {
                await dotNetRef.invokeMethodAsync('OnStreamTypeChanged', 'screenshare');
            }

            // Restart calls with screen stream
            restartCallsWithNewStream(localStream);

            // Handle screen sharing stop
            screenStream.getVideoTracks()[0].addEventListener('ended', async () => {
                console.log("Screen sharing stopped.");
                try {
                    const webcamStream = originalStream || await navigator.mediaDevices.getUserMedia({ 
                        video: true, 
                        audio: true 
                    });
                    await switchToWebcam(webcamStream);
                } catch (err) {
                    console.error("Failed to revert to webcam:", err);
                    throw err;
                }
            });

            return { success: true };

        } catch (error) {
            console.error("Error sharing screen:", error);
            return { success: false, error: error.message };
        }
    }

    async function switchToWebcam(webcamStream) {
        localStream = webcamStream;
        const video = document.getElementById("sessionVideo");
        if (video) {
            video.srcObject = localStream;
            console.log("Switched back to webcam stream.");
        }

        // Notify Blazor of stream change
        if (dotNetRef) {
            await dotNetRef.invokeMethodAsync('OnStreamTypeChanged', 'webcam');
        }

        restartCallsWithNewStream(localStream);
    }

    function restartCallsWithNewStream(stream) {
        for (const studentId of studentPeers) {
            const conn = studentConnections.get(studentId);
            if (conn) {
                console.log(`Restarting call for student: ${studentId}`);
                const call = peer.call(studentId, stream);
                call.on("open", () => console.log(`Call restarted for ${studentId}`));
                call.on("error", (err) => console.error(`Call error for ${studentId}:`, err));
            }
        }
    }

    // Setup peer connection for student
    async function setupStudentPeer() {
        console.log("Setting up student peer");
        
        // Debug: Check all video elements
        const allVideos = document.querySelectorAll('video');
        console.log(`Found ${allVideos.length} video element(s) on page:`);
        allVideos.forEach((v, i) => {
            console.log(`  Video ${i}: id="${v.id}", class="${v.className}"`);
        });

        const video = document.getElementById("sessionVideo");
        if (!video) {
            console.error("Video element #sessionVideo not found - waiting for DOM...");
            // Wait and retry
            await new Promise(resolve => setTimeout(resolve, 200));
            const videoRetry = document.getElementById("sessionVideo");
            if (!videoRetry) {
                console.error("Video element still not found after retry");
                
                // Debug: List all elements with IDs
                const allIds = document.querySelectorAll('[id]');
                console.log(`All elements with IDs on page (${allIds.length}):`);
                allIds.forEach(el => console.log(`  - ${el.tagName}#${el.id}`));
                
                return { success: false, error: "Video element #sessionVideo not found. Make sure the session view is rendered." };
            }
            // Use retry element
            return setupStudentPeerWithElement(videoRetry);
        }

        return setupStudentPeerWithElement(video);
    }

    async function setupStudentPeerWithElement(video) {
        if (peer && !peer.disconnected) {
            console.log("Student peer already exists:", peer.id);
            return { success: true, peerId: peer.id };
        }

        console.log("Creating student peer for video element:", video.id);
        console.log("Video element details:", {
            id: video.id,
            tagName: video.tagName,
            className: video.className,
            parentId: video.parentElement?.id
        });

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
                console.log("Student peer open:", id);
                
                // Notify Blazor with peer ID so it can send to SignalR
                if (dotNetRef) {
                    dotNetRef.invokeMethodAsync('OnStudentPeerReady', id);
                }

                resolve({ success: true, peerId: id });
            });

            peer.on("call", (call) => {
                console.log("Received lecturer call:", call.peer);
                handleIncomingCall(call, video);
            });

            peer.on("error", (err) => {
                console.error("Peer error:", err);
                handlePeerError(err);
                reject(err);
            });
        });
    }

    function handleIncomingCall(call, video) {
        call.answer();

        let streamTimeout = setTimeout(() => {
            if (!isStreamAttached) {
                console.warn("No stream received after 10s, retrying...");
                call.close();
            }
        }, 10000);

        call.on("stream", (remoteStream) => {
            clearTimeout(streamTimeout);
            console.log("Received lecturer stream:", remoteStream);

            if (remoteStream.id === attachedStreamId) {
                console.log("Ignoring duplicate stream, ID:", remoteStream.id);
                return;
            }

            if (isStreamAttached) {
                console.log("Stream already attached, ignoring new stream:", remoteStream.id);
                return;
            }

            isStreamAttached = true;
            attachedStreamId = remoteStream.id;

            if (video) {
                console.log("Attaching remote stream to video element.");
                video.srcObject = remoteStream;
                
                // Unmute video for students to hear lecturer
                video.muted = false;
                video.volume = 1.0;
                
                video.play().catch(err => {
                    console.error("Playback failed:", err.message);
                    // If autoplay fails, show play button for user interaction
                    if (dotNetRef) {
                        dotNetRef.invokeMethodAsync('OnStreamReceived');
                    }
                });

                // Notify Blazor that stream is attached
                if (dotNetRef) {
                    dotNetRef.invokeMethodAsync('OnStreamReceived');
                }
            }
        });

        call.on("close", () => {
            console.log("Call closed.");
            window.currentCall = null;
            isStreamAttached = false;
            attachedStreamId = null;
            if (video) {
                video.srcObject = null;
            }

            // Notify Blazor
            if (dotNetRef) {
                dotNetRef.invokeMethodAsync('OnStreamLost');
            }
        });

        call.on("error", (err) => {
            console.error("Call error:", err);
            if (dotNetRef) {
                dotNetRef.invokeMethodAsync('OnPeerError', err.type || 'unknown');
            }
        });

        window.currentCall = call;
    }

    // Call a specific student (used by lecturer)
    function callStudent(studentPeerId) {
        if (!isLecturer) {
            console.error("Only lecturer can call students");
            return { success: false, error: "Not lecturer" };
        }

        if (!peer || peer.disconnected) {
            console.error("Lecturer peer not initialized");
            return { success: false, error: "Peer not initialized" };
        }

        if (!localStream) {
            console.error("No local stream available");
            return { success: false, error: "No local stream" };
        }

        console.log(`Calling student: ${studentPeerId}`);

        try {
            const call = peer.call(studentPeerId, localStream);

            if (!call) {
                console.error("Failed to create call");
                return { success: false, error: "Failed to create call" };
            }

            call.on("stream", (remoteStream) => {
                console.log(`Call established with student ${studentPeerId}`);
                // Students don't send stream back, so this won't trigger
            });

            call.on("close", () => {
                console.log(`Call to student ${studentPeerId} closed`);
                if (dotNetRef) {
                    dotNetRef.invokeMethodAsync('OnStudentDisconnected', studentPeerId);
                }
            });

            call.on("error", (err) => {
                console.error(`Call error with student ${studentPeerId}:`, err);
            });

            console.log(`Call initiated to student ${studentPeerId}`);
            return { success: true, peerId: studentPeerId };

        } catch (err) {
            console.error(`Exception calling student ${studentPeerId}:`, err);
            return { success: false, error: err.message };
        }
    }

    // Connect student to lecturer peer
    function connectToLecturer(lecturerPeerId, attempt = 1, maxAttempts = 15) {
        console.log(`Connecting to lecturer: ${lecturerPeerId}, attempt ${attempt}/${maxAttempts}`);

        if (!peer || peer.disconnected) {
            console.warn("Student peer not initialized or disconnected");
            return { success: false, error: "Peer not initialized" };
        }

        const conn = peer.connect(lecturerPeerId);

        conn.on("open", () => {
            console.log("Connected to lecturer:", lecturerPeerId);
            conn.send({ type: "studentReady", studentId: peer.id });

            // Notify Blazor of successful connection
            if (dotNetRef) {
                dotNetRef.invokeMethodAsync('OnConnectedToLecturer', lecturerPeerId);
            }
        });

            conn.on("data", (data) => {
                console.log("Received data from lecturer:", data.type || data);
                
                // Handle different message types
                if (data.type === "fileMetadata") {
                    handleFileMetadata(data);
                } else if (data.type === "fileChunk") {
                    handleFileChunk(data);
                } else if (data.type === "fileComplete") {
                    handleFileComplete(data);
                }
            });

        conn.on("error", (err) => {
            console.error("Connection error:", err);
            if (attempt < maxAttempts && err.type === "peer-unavailable") {
                console.warn(`Retrying connection (attempt ${attempt + 1}/${maxAttempts})...`);
                setTimeout(() => connectToLecturer(lecturerPeerId, attempt + 1, maxAttempts), 3000);
            } else {
                console.error("Max connection attempts reached or fatal error:", err);
                if (dotNetRef) {
                    dotNetRef.invokeMethodAsync('OnConnectionFailed', err.type || 'unknown');
                }
            }
        });

        return { success: true };
    }

    // Handle stream change notification from Blazor (when lecturer switches)
    function handleStreamChange(streamType) {
        console.log(`Handling stream change: ${streamType}`);
        
        if (window.currentCall) {
            isStreamAttached = false;
            attachedStreamId = null;
            const video = document.getElementById("sessionVideo");
            if (video) {
                console.log("Clearing video for new stream.");
                video.srcObject = null;
            }
        }
    }

    function handlePeerError(err) {
        if (err.type === "peer-unavailable") {
            console.warn("Peer not available");
        } else if (err.type === "server-disconnected") {
            console.warn("PeerServer disconnected, reconnecting...");
            if (peer) {
                peer.reconnect();
            }
        }

        // Notify Blazor of error
        if (dotNetRef) {
            dotNetRef.invokeMethodAsync('OnPeerError', err.type || 'unknown');
        }
    }

    // File transfer handling for students
    const fileTransfers = new Map(); // Map<messageId, { metadata, chunks, receivedCount }>

    function handleFileMetadata(data) {
        const { messageId, fileSize, totalChunks, chunkSize } = data;
        console.log(`Received file metadata: messageId=${messageId}, size=${fileSize}, chunks=${totalChunks}`);

        fileTransfers.set(messageId, {
            metadata: { fileSize, totalChunks, chunkSize },
            chunks: new Array(totalChunks),
            receivedCount: 0
        });

        // Update UI to show receiving file
        const fileMessage = document.querySelector(`[data-file-id="${messageId}"]`);
        if (fileMessage) {
            fileMessage.textContent = "Receiving...";
            fileMessage.disabled = true;
        }
    }

    function handleFileChunk(data) {
        const { messageId, index, totalChunks, data: chunkData } = data;
        
        if (!fileTransfers.has(messageId)) {
            console.warn(`Received chunk for unknown file: ${messageId}`);
            return;
        }

        const transfer = fileTransfers.get(messageId);
        
        // Store chunk
        transfer.chunks[index] = chunkData;
        transfer.receivedCount++;

        const progress = (transfer.receivedCount / transfer.metadata.totalChunks) * 100;
        console.log(`Chunk ${index + 1}/${transfer.metadata.totalChunks} received (${progress.toFixed(1)}%)`);

        // Update progress in UI
        const fileMessage = document.querySelector(`[data-file-id="${messageId}"]`);
        if (fileMessage) {
            fileMessage.textContent = `${progress.toFixed(0)}%`;
        }

        // Check if all chunks received (auto-complete)
        if (transfer.receivedCount === transfer.metadata.totalChunks) {
            console.log(`All chunks received, reassembling...`);
            reassembleFile(messageId, transfer);
        }
    }

    function handleFileComplete(data) {
        const { messageId, totalChunks } = data;
        console.log(`File transfer complete signal received for ${messageId}`);

        if (!fileTransfers.has(messageId)) {
            console.warn(`Completion signal for unknown file: ${messageId}`);
            return;
        }

        const transfer = fileTransfers.get(messageId);
        
        // Check if all chunks received
        const receivedChunks = transfer.chunks.filter(c => c !== undefined).length;
        
        if (receivedChunks === totalChunks) {
            console.log(`All ${totalChunks} chunks received, reassembling file...`);
            reassembleFile(messageId, transfer);
        } else {
            console.warn(`Missing chunks: ${receivedChunks}/${totalChunks}`);
            const fileMessage = document.querySelector(`[data-file-id="${messageId}"]`);
            if (fileMessage) {
                fileMessage.textContent = "Failed";
                fileMessage.classList.add("text-red-500");
            }
        }
    }

    function reassembleFile(messageId, transfer) {
        try {
            // Create blob from chunks
            const blob = new Blob(transfer.chunks);
            console.log(`File reassembled: ${blob.size} bytes`);

            // Create download URL
            const url = URL.createObjectURL(blob);

            // Get filename from message content
            const messageElement = document.querySelector(`[data-file-id="${messageId}"]`);
            let fileName = 'download';
            
            if (messageElement) {
                const contentElement = messageElement.closest('.bg-secondary\\/30, .bg-secondary\\/40, [class*="bg-secondary"]');
                if (contentElement) {
                    const textContent = contentElement.textContent || '';
                    fileName = textContent.replace('File:', '').replace('Receiving...', '').trim() || 'download';
                }
            }

            // Update download button
            if (messageElement) {
                messageElement.onclick = (e) => {
                    e.preventDefault();
                    const tempLink = document.createElement("a");
                    tempLink.href = url;
                    tempLink.download = fileName;
                    document.body.appendChild(tempLink);
                    tempLink.click();
                    document.body.removeChild(tempLink);
                };
                messageElement.textContent = "Download";
                messageElement.disabled = false;
                messageElement.classList.remove("text-red-500");
            }

            // Clean up transfer data
            fileTransfers.delete(messageId);

            console.log(`File ready for download: ${fileName}`);

            // Notify Blazor
            if (dotNetRef) {
                dotNetRef.invokeMethodAsync('OnFileDownloadComplete', messageId, fileName);
            }

        } catch (error) {
            console.error("Error reassembling file:", error);
            const fileMessage = document.querySelector(`[data-file-id="${messageId}"]`);
            if (fileMessage) {
                fileMessage.textContent = "Error";
                fileMessage.classList.add("text-red-500");
            }
            
            if (dotNetRef) {
                dotNetRef.invokeMethodAsync('OnFileDownloadError', messageId, error.message);
            }
        }
    }

    // Send data to all connected students (for file sharing)
    function sendDataToPeers(data) {
        if (!isLecturer) {
            console.error("Only lecturer can send data to peers");
            return { success: false, error: "Not lecturer" };
        }

        if (studentPeers.length === 0) {
            console.warn("No students connected");
            return { success: false, error: "No students connected", sentTo: 0, total: 0 };
        }

        let successCount = 0;
        let failedPeers = [];

        for (const studentId of studentPeers) {
            const conn = studentConnections.get(studentId);
            if (conn && conn.open) {
                try {
                    conn.send(data);
                    successCount++;
                } catch (err) {
                    console.error(`Failed to send to ${studentId}:`, err);
                    failedPeers.push(studentId);
                }
            } else {
                console.warn(`Connection not open for ${studentId}`);
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
    async function sendFileToStudents(dotNetStreamRef, messageId) {
        if (!isLecturer) {
            throw new Error("Only lecturer can send files");
        }

        if (studentConnections.size === 0) {
            console.warn("No students connected via data channel");
            return { success: false, error: "No students connected" };
        }

        try {
            // Read stream from .NET
            const arrayBuffer = await dotNetStreamRef.arrayBuffer();
            const fileSize = arrayBuffer.byteLength;
            
            const chunkSize = 64 * 1024; // 64KB chunks for better reliability
            const totalChunks = Math.ceil(fileSize / chunkSize);
            let sentChunks = 0;

            console.log(`Sending file: size ${fileSize} bytes, chunks: ${totalChunks}`);

            // Send file metadata first
            const metadataMessage = {
                type: "fileMetadata",
                messageId: messageId,
                fileSize: fileSize,
                totalChunks: totalChunks,
                chunkSize: chunkSize
            };

            // Send metadata to all students
            studentConnections.forEach((conn, peerId) => {
                if (conn.open) {
                    try {
                        conn.send(metadataMessage);
                        console.log(`Sent metadata to ${peerId}`);
                    } catch (err) {
                        console.error(`Failed to send metadata to ${peerId}:`, err);
                    }
                }
            });

            // Wait a bit for students to prepare
            await new Promise(resolve => setTimeout(resolve, 100));

            // Send chunks
            for (let i = 0; i < totalChunks; i++) {
                const start = i * chunkSize;
                const end = Math.min(start + chunkSize, fileSize);
                const chunkData = arrayBuffer.slice(start, end);

                const chunkMessage = {
                    type: "fileChunk",
                    messageId: messageId,
                    index: i,
                    totalChunks: totalChunks,
                    data: chunkData
                };

                let successCount = 0;
                let failCount = 0;

                // Send to all connected students
                studentConnections.forEach((conn, peerId) => {
                    if (conn.open) {
                        try {
                            conn.send(chunkMessage);
                            successCount++;
                        } catch (err) {
                            console.error(`Failed to send chunk ${i} to ${peerId}:`, err);
                            failCount++;
                        }
                    }
                });

                sentChunks++;
                const progress = (sentChunks / totalChunks) * 100;

                // Notify Blazor of progress
                if (dotNetRef && sentChunks % 5 === 0) { // Update every 5 chunks
                    dotNetRef.invokeMethodAsync('OnFileUploadProgress', progress);
                }

                console.log(`Chunk ${i + 1}/${totalChunks} sent to ${successCount} students`);

                // Small delay to prevent overwhelming the connection
                if (i < totalChunks - 1) {
                    await new Promise(resolve => setTimeout(resolve, 10));
                }
            }

            // Send completion message
            const completionMessage = {
                type: "fileComplete",
                messageId: messageId,
                totalChunks: totalChunks
            };

            studentConnections.forEach((conn, peerId) => {
                if (conn.open) {
                    try {
                        conn.send(completionMessage);
                    } catch (err) {
                        console.error(`Failed to send completion to ${peerId}:`, err);
                    }
                }
            });

            // Final progress update
            if (dotNetRef) {
                dotNetRef.invokeMethodAsync('OnFileUploadProgress', 100);
            }

            console.log(`File transfer complete: ${totalChunks} chunks sent`);
            return { success: true, totalChunks: totalChunks, sentTo: studentConnections.size };

        } catch (error) {
            console.error("Error sending file:", error);
            throw error;
        }
    }

    // Cleanup all session resources
    function cleanup() {
        console.log("Cleaning up session resources");

        // Stop local stream
        if (localStream) {
            localStream.getTracks().forEach(track => track.stop());
            localStream = null;
        }

        // Stop original stream if exists
        if (originalStream) {
            originalStream.getTracks().forEach(track => track.stop());
            originalStream = null;
        }

        // Destroy peer connection
        if (peer) {
            peer.destroy();
            peer = null;
        }

        // Clear video element
        const video = document.getElementById("sessionVideo");
        if (video) {
            video.srcObject = null;
        }

        // Clear state
        isStreamAttached = false;
        attachedStreamId = null;
        studentPeers = [];
        studentConnections.clear();
        dotNetRef = null;
        currentSessionId = null;

        console.log("Cleanup completed");
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
                console.error("Battery API error:", err);
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

        document.addEventListener('visibilitychange', () => {
            if (dotNetRef) {
                const isVisible = !document.hidden;
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
        callStudent: callStudent,
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


