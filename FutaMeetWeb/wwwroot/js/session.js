// SignalR connection
let connection = new signalR.HubConnectionBuilder()
    .withUrl("/sessionHub")
    .build();
let pcs = {};
let localStream = null;
let sessionId, userId;
let displayedMessages = new Set(); // Track displayed messages to prevent duplicates

// Register ReceiveMessage handler once
function registerMessageHandler() {
    connection.off("ReceiveMessage");
    connection.on("ReceiveMessage", (senderId, message) => {
        try {
            console.log(`[DEBUG] ${userId}: ReceiveMessage: senderId=${senderId}, message=${message}`);
            if (!senderId || !message) {
                console.error(`[ERROR] ${userId}: Invalid ReceiveMessage args`);
                return;
            }
            let chat = document.getElementById("chatMessages");
            if (!chat) {
                console.error(`[ERROR] ${userId}: chatMessages element not found`);
                return;
            }
            // Create a unique key for the message to prevent duplicates
            const timestamp = new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
            const messageKey = `${senderId}:${message}:${timestamp}`;
            if (displayedMessages.has(messageKey)) {
                console.log(`[DEBUG] ${userId}: Duplicate message detected, skipping: ${messageKey}`);
                return;
            }
            displayedMessages.add(messageKey);

            const userClass = senderId === userId ? 'sender' : 'receiver'; // Sender or receiver
            chat.innerHTML += `
                <div class="${userClass}">
                    <p><strong>${senderId}:</strong> ${message}</p>
                    <span class="timestamp">${timestamp}</span>
                </div>`;
            chat.scrollTop = chat.scrollHeight;
            console.log(`[DEBUG] ${userId}: chatMessages updated`);
        } catch (e) {
            console.error(`[ERROR] ${userId}: ReceiveMessage failed:`, e);
        }
    });
}

// Initialize session (moved to top)
async function initSession(sId, uId, isLecturer) {
    sessionId = sId;
    userId = uId;
    console.log(`[DEBUG] initSession: sId=${sId}, uId=${uId}`);
    if (!sessionId || !userId) {
        console.error(`[ERROR] ${userId || 'unknown'}: Missing sessionId=${sessionId}, userId=${userId}`);
        return;
    }
    try {
        if (!connection) {
            console.error(`[ERROR] ${userId}: SignalR connection not initialized`);
            return;
        }
        console.log(`[DEBUG] ${userId}: SignalR state: ${connection.state}`);
        if (connection.state === signalR.HubConnectionState.Disconnected) {
            console.log(`[DEBUG] ${userId}: Connecting to SignalR...`);
            await connection.start();
            console.log(`[DEBUG] ${userId}: SignalR connected`);
            registerMessageHandler(); // Register chat handler after connection starts
        }

        // Audio/Video
        console.log(`[DEBUG] ${userId}: Requesting media (audio: true, video: ${isLecturer})`);
        localStream = await navigator.mediaDevices.getUserMedia({ audio: true, video: isLecturer });
        let videoElement = document.getElementById(isLecturer ? "localVideo" : "lecturerVideo");
        if (videoElement && isLecturer) {
            videoElement.srcObject = localStream;
            console.log(`[DEBUG] ${userId}: Set localVideo stream`);
        }

        console.log(`[DEBUG] ${userId}: Joining session ${sessionId}`);
        await connection.invoke("JoinSession", sessionId, userId);
        console.log(`[DEBUG] ${userId}: Joined session ${sessionId}`);
    } catch (e) {
        console.error(`[ERROR] ${userId}: Session init failed:`, e);
    }
}

// SignalR reconnect handlers (consolidated)
connection.onreconnecting(() => {
    console.log(`[DEBUG] ${userId}: SignalR reconnecting...`);
});

connection.onreconnected(() => {
    console.log(`[DEBUG] ${userId}: SignalR reconnected`);
    registerMessageHandler(); // Re-register handler after reconnect
});

// WebRTC: New user (consolidated)
connection.on("UserJoined", async (otherUserId, otherConnectionId) => {
    try {
        console.log(`[DEBUG] ${userId}: UserJoined: otherUserId=${otherUserId}, otherConnectionId=${otherConnectionId}`);
        if (!otherUserId || !otherConnectionId) {
            console.error(`[ERROR] ${userId}: Invalid UserJoined args: otherUserId=${otherUserId}, otherConnectionId=${otherConnectionId}`);
            return;
        }
        if (!localStream) {
            console.error(`[ERROR] ${userId}: localStream not ready`);
            return;
        }
        let pc = new RTCPeerConnection();
        pcs[otherConnectionId] = pc;
        localStream.getTracks().forEach(track => pc.addTrack(track, localStream));
        pc.ontrack = event => addRemoteAudio(otherConnectionId, event.streams[0]);
        let offer = await pc.createOffer();
        await pc.setLocalDescription(offer);
        console.log(`[DEBUG] ${userId}: Sending offer to ${otherUserId}`);
        await connection.invoke("SendOffer", sessionId, JSON.stringify(offer), otherConnectionId);
        pc.onicecandidate = event => {
            if (event.candidate) {
                connection.invoke("SendIceCandidate", sessionId, JSON.stringify(event.candidate), otherConnectionId);
            }
        };
    } catch (e) {
        console.error(`[ERROR] ${userId}: UserJoined failed:`, e);
    }
});

// WebRTC: Handle answer
connection.on("ReceiveAnswer", async (answer, fromConnectionId) => {
    if (pcs[fromConnectionId]) {
        await pcs[fromConnectionId].setRemoteDescription(JSON.parse(answer));
    }
});

// WebRTC: Handle ICE
connection.on("ReceiveIceCandidate", async (candidate, fromConnectionId) => {
    if (pcs[fromConnectionId]) {
        await pcs[fromConnectionId].addIceCandidate(JSON.parse(candidate));
    }
});

// Send chat message
async function sendMessage() {
    let input = document.getElementById("chatInput");
    let message = input.value.trim();
    if (!message) {
        console.log(`[DEBUG] ${userId || 'unknown'}: Empty message, ignoring`);
        return;
    }
    if (!connection || connection.state !== signalR.HubConnectionState.Connected) {
        console.error(`[ERROR] ${userId || 'unknown'}: SignalR not connected`);
        return;
    }
    if (!userId || !sessionId) {
        console.error(`[ERROR] SendMessage: userId=${userId}, sessionId=${sessionId}`);
        return;
    }
    try {
        console.log(`[DEBUG] ${userId}: Sending: ${message} (sessionId=${sessionId})`);
        await connection.invoke("SendMessage", sessionId, userId, message);
        input.value = "";
        console.log(`[DEBUG] ${userId}: Message sent, input cleared`);
    } catch (e) {
        console.error(`[ERROR] ${userId}: Send message failed:`, e);
    }
}

// Toggle mic mute
function toggleAudio() {
    if (localStream) {
        localStream.getAudioTracks().forEach(track => track.enabled = !track.enabled);
        let button = document.getElementById("toggleAudio");
        if (button) {
            button.textContent = localStream.getAudioTracks()[0].enabled ? "Mute" : "Unmute";
            console.log(`[DEBUG] ${userId}: Audio ${localStream.getAudioTracks()[0].enabled ? "unmuted" : "muted"}`);
        }
    }
}

// Add remote audio
function addRemoteAudio(connectionId, stream) {
    let audio = document.createElement("audio");
    audio.id = `audio-${connectionId}`;
    audio.srcObject = stream;
    audio.autoplay = true;
    let container = document.getElementById("remoteAudios");
    if (container) {
        container.appendChild(audio);
        console.log(`[DEBUG] Added audio for ${connectionId}`);
    } else {
        console.error(`[ERROR] remoteAudios not found`);
    }
}