// SignalR connection
const connection = new signalR.HubConnectionBuilder()
    .withUrl("/sessionHub")
    .build();
let pcs = {};
let localStream = null;
let sessionLecturerConnectionId = null; // Track the actual lecturer's connectionId
let myConnectionId = null; // Track this client's connectionId

// Refactored initSession to accept isLecturer and handle logic accordingly
async function initSession(sId, uId, isLecturer) {
    console.log(`[DEBUG] initSession: sId=${sId}, uId=${uId}, isLecturer=${isLecturer}`);
    if (!sId || !uId) {
        console.error(`[ERROR] Invalid sId or uId: sId=${sId}, uId=${uId}`);
        return;
    }
    try {
        if (connection.state !== signalR.HubConnectionState.Connected) {
            await connection.start();
            // Get this client's connectionId
            myConnectionId = connection.connectionId;
            console.log(`[DEBUG] SignalR connection started, myConnectionId=${myConnectionId}`);
        }
        if (isLecturer) {
            // Lecturer starts the session first, then joins
            console.log(`[DEBUG] ${uId}: Invoking StartSession for sId=${sId}`);
            await connection.invoke("StartSession", sId);
            console.log(`[DEBUG] ${uId}: Invoking JoinSession for sId=${sId}`);
            await connection.invoke("JoinSession", sId, uId, true);
        } else {
            // Non-lecturer checks if session is started
            console.log(`[DEBUG] ${uId}: Checking session status for sId=${sId}`);
            const [isStarted, isMuted, lecturerConnectionId] = await connection.invoke("GetSessionStatusFull", sId);
            if (!isStarted) {
                console.error(`[ERROR] ${uId}: Session ${sId} not started`);
                return;
            }
            sessionLecturerConnectionId = lecturerConnectionId;
            console.log(`[DEBUG] ${uId}: Got lecturerConnectionId=${lecturerConnectionId}`);
            await connection.invoke("JoinSession", sId, uId, false);
        }
        // Set up handlers
        connection.on("ReceiveMessage", (senderId, message, timestamp) => {
            let chat = document.getElementById("chatMessages");
            if (!chat) return;
            chat.innerHTML += `
                <div class="${senderId === uId ? 'sender' : 'receiver'}">
                    <p><strong>${senderId}:</strong> ${message}</p>
                    <span class="timestamp"><i class="fas fa-clock"></i> ${timestamp}</span>
                </div>`;
            chat.scrollTop = chat.scrollHeight;
        });
        connection.on("SessionStarted", () => {
            console.log(`[DEBUG] ${uId}: SessionStarted received`);
            if (isLecturer && document.getElementById("localVideo")) {
                setupLocalVideo();
            }
        });
        connection.on("SessionEnded", () => {
            console.log(`[DEBUG] ${uId}: Session ended`);
            if (localStream) {
                localStream.getTracks().forEach(track => track.stop());
                localStream = null;
            }
            Object.values(pcs).forEach(pc => pc.close());
            pcs = {};
            let chat = document.getElementById("chatMessages");
            if (chat) chat.innerHTML = "<p>Session has ended.</p>";
        });

        connection.on("SessionStatus", (isStarted, isMuted, lecturerConnectionId) => {
            console.log(`[DEBUG] ${uId}: SessionStatus - started=${isStarted}, muted=${isMuted}, lecturerConnectionId=${lecturerConnectionId}`);
            sessionLecturerConnectionId = lecturerConnectionId;
            let status = document.getElementById("lecturerStatus");
            if (status) status.textContent = `Lecturer: ${isMuted ? "Muted" : "Unmuted"}`;
        });

        connection.on("MuteStateUpdated", (isMuted) => {
            console.log(`[DEBUG] ${uId}: Lecturer mute state: ${isMuted}`);
            let status = document.getElementById("lecturerStatus");
            if (status) status.textContent = `Lecturer: ${isMuted ? "Muted" : "Unmuted"}`;
        });

        connection.on("UserJoined", async (otherUserId, otherConnectionId) => {
            if (!localStream || myConnectionId !== sessionLecturerConnectionId) return;
            console.log(`[DEBUG] ${uId}: UserJoined - otherUserId=${otherUserId}, otherConnectionId=${otherConnectionId}`);
            try {
                let pc = new RTCPeerConnection({ iceServers: [{ urls: "stun:stun.l.google.com:19302" }] });
                pcs[otherConnectionId] = pc;
                localStream.getTracks().forEach(track => {
                    console.log(`[DEBUG] ${uId}: Adding track: ${track.kind}, enabled=${track.enabled}`);
                    pc.addTrack(track, localStream);
                });
                let offer = await pc.createOffer();
                await pc.setLocalDescription(offer);
                console.log(`[DEBUG] ${uId}: Sending offer to ${otherConnectionId}`);
                await connection.invoke("SendOffer", sId, JSON.stringify(offer), otherConnectionId);
                pc.onicecandidate = event => {
                    if (event.candidate) {
                        console.log(`[DEBUG] ${uId}: Sending ICE candidate to ${otherConnectionId}`);
                        connection.invoke("SendIceCandidate", sId, JSON.stringify(event.candidate), otherConnectionId);
                    }
                };
                pc.oniceconnectionstatechange = () => {
                    console.log(`[DEBUG] ${uId}: ICE state for ${otherConnectionId}: ${pc.iceConnectionState}`);
                    if (pc.iceConnectionState === "failed") console.error(`[ERROR] ${uId}: ICE connection failed`);
                };
            } catch (e) {
                console.error(`[ERROR] ${uId}: UserJoined failed:`, e);
            }
        });

        connection.on("ReceiveOffer", async (offer, fromConnectionId) => {
            if (fromConnectionId !== sessionLecturerConnectionId) return;
            console.log(`[DEBUG] ${uId}: ReceiveOffer from ${fromConnectionId}`);
            try {
                let pc = new RTCPeerConnection({ iceServers: [{ urls: "stun:stun.l.google.com:19302" }] });
                pcs[fromConnectionId] = pc;
                pc.ontrack = event => {
                    if (event.streams && event.streams[0]) {
                        console.log(`[DEBUG] ${uId}: Received stream tracks:`, event.streams[0].getTracks());
                        let videoElement = document.getElementById("lecturerVideo");
                        if (videoElement) {
                            videoElement.srcObject = event.streams[0];
                            videoElement.muted = false;
                            videoElement.play().catch(e => console.error(`[ERROR] ${uId}: Video playback failed:`, e));
                            console.log(`[DEBUG] ${uId}: Lecturer video set, srcObject=${!!videoElement.srcObject}, muted=${videoElement.muted}`);
                        } else {
                            console.error(`[ERROR] ${uId}: lecturerVideo element not found`);
                        }
                    }
                };
                await pc.setRemoteDescription(JSON.parse(offer));
                let answer = await pc.createAnswer();
                await pc.setLocalDescription(answer);
                console.log(`[DEBUG] ${uId}: Sending answer to ${fromConnectionId}`);
                await connection.invoke("SendAnswer", sId, JSON.stringify(answer), fromConnectionId);
                pc.onicecandidate = event => {
                    if (event.candidate) {
                        console.log(`[DEBUG] ${uId}: Sending ICE candidate to ${fromConnectionId}`);
                        connection.invoke("SendIceCandidate", sId, JSON.stringify(event.candidate), fromConnectionId);
                    }
                };
                pc.oniceconnectionstatechange = () => {
                    console.log(`[DEBUG] ${uId}: ICE state for ${fromConnectionId}: ${pc.iceConnectionState}`);
                    if (pc.iceConnectionState === "failed") console.error(`[ERROR] ${uId}: ICE connection failed`);
                };
            } catch (e) {
                console.error(`[ERROR] ${uId}: ReceiveOffer failed:`, e);
            }
        });

        connection.on("ReceiveAnswer", async (answer, fromConnectionId) => {
            console.log(`[DEBUG] ${uId}: ReceiveAnswer from ${fromConnectionId}`);
            try {
                if (pcs[fromConnectionId]) await pcs[fromConnectionId].setRemoteDescription(JSON.parse(answer));
            } catch (e) {
                console.error(`[ERROR] ${uId}: ReceiveAnswer failed:`, e);
            }
        });

        connection.on("ReceiveIceCandidate", async (candidate, fromConnectionId) => {
            console.log(`[DEBUG] ${uId}: ReceiveIceCandidate from ${fromConnectionId}`);
            try {
                if (pcs[fromConnectionId]) await pcs[fromConnectionId].addIceCandidate(JSON.parse(candidate));
            } catch (e) {
                console.error(`[ERROR] ${uId}: ReceiveIceCandidate failed:`, e);
            }
        });

        connection.on("UserLeft", (connectionId) => {
            if (pcs[connectionId]) {
                pcs[connectionId].close();
                delete pcs[connectionId];
                console.log(`[DEBUG] ${uId}: User ${connectionId} left`);
            }
        });
    } catch (e) {
        console.error(`[ERROR] ${uId}: Session init failed:`, e);
    }
}

function setupLocalVideo() {
    console.log(`[DEBUG] Setting up local video`);
    navigator.mediaDevices.getUserMedia({ audio: true, video: true })
        .then(stream => {
            localStream = stream;
            console.log(`[DEBUG] Local stream tracks:`, localStream.getTracks().map(t => `${t.kind}: ${t.enabled}`));
            let videoElement = document.getElementById("localVideo");
            if (videoElement) {
                videoElement.srcObject = stream;
                videoElement.muted = true; // Mute local video to avoid feedback
                videoElement.play().catch(e => console.error(`[ERROR] Local video playback failed:`, e));
                console.log(`[DEBUG] Local video set, srcObject=${!!videoElement.srcObject}, muted=${videoElement.muted}`);
            } else {
                console.error(`[ERROR] localVideo element not found`);
            }
        })
        .catch(e => console.error(`[ERROR] getUserMedia failed:`, e));
}

// Send chat message
async function sendMessage(sessionId, userId) {
    let input = document.getElementById("chatInput");
    let message = input.value.trim();
    if (!message) return;
    try {
        await connection.invoke("SendMessage", sessionId, userId, message);
        input.value = "";
    } catch (e) {
        console.error(`[ERROR] ${userId}: Send message failed:`, e);
    }
}