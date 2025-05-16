let localStream = null;
let peer = null;
let isStreamAttached = false;
let attachedStreamId = null; // Track stream ID
let hasJoinedSession = false; // Track session join

const connection = new signalR.HubConnectionBuilder()
    .withUrl("/sessionHub")
    .withAutomaticReconnect()
    .build();

connection.onclose((error) => {
    console.error("SignalR connection closed.", error);
    alert("Session connection lost. Please refresh or try again.");
});

connection.onreconnected(() => {
    console.log("SignalR reconnected, rejoining session...");
    const { sessionId } = window.sessionState || {};
    if (sessionId && !window.isSessionLecturer && !hasJoinedSession) {
        connection.invoke("StartSession", sessionId);
    }
});

connection.start().then(() => {
    const { sessionId, isSessionStarted } = window.sessionState || {};
    if (sessionId && isSessionStarted && window.isSessionLecturer && !localStream) {
        connection.invoke("StartSession", sessionId);
    } else if (sessionId && !window.isSessionLecturer) {
        connection.invoke("StartSession", sessionId);
    }
}).catch(err => console.error("SignalR connection failed:", err));

connection.on("StartSession", (sessionId) => {
    if (hasJoinedSession) {
        console.log("Already joined session, skipping StartSession:", sessionId);
        return;
    }
    hasJoinedSession = true;
    const video = document.getElementById("sessionVideo");
    console.log("Video Element Ready:", video);
    if (video && video.srcObject) {
        console.log("Stream already attached:", video.srcObject);
        return;
    }

    if (window.isSessionLecturer) {
        if (localStream && video && !video.srcObject) {
            video.srcObject = localStream;
            video.play().catch(error => console.error("Error playing lecturer video:", error));
            return;
        }

        navigator.mediaDevices.getUserMedia({ video: true, audio: true })
            .then(stream => {
                localStream = stream;
                window.localStream = stream;
                if (video) {
                    video.srcObject = stream;
                    video.play().catch(error => console.error("Error playing lecturer video:", error));
                }
                peer = new Peer(sessionId, {
                    config: {
                        iceServers: [
                            { urls: "stun:stun.l.google.com:19302" },
                            { urls: "turn:turn.bistri.com:80", username: "homeo", credential: "homeo" }
                        ]
                    }
                });
                peer.on("open", () => {
                    console.log("Lecturer peer open:", sessionId);
                    connection.invoke("SessionStarted", sessionId).catch(err => console.error("Failed to broadcast SessionStarted:", err));
                });
                peer.on("connection", (conn) => {
                    conn.on("open", () => {
                        console.log("Student connected, peer ID:", conn.peer);
                        if (!conn.peer) {
                            console.warn("Invalid student peer ID, skipping call");
                            return;
                        }
                        const call = peer.call(conn.peer, localStream);
                        call.on("open", () => console.log("Call to student opened:", conn.peer));
                        call.on("error", (err) => console.error("Call error:", err));
                    });
                    conn.on("data", (data) => console.log("Received student data:", data));
                });
                peer.on("error", (err) => {
                    console.error("Peer error:", err);
                    if (err.type === "peer-unavailable") {
                        console.warn("Student not found. They may have disconnected.");
                    } else if (err.type === "server-disconnected") {
                        alert("Lost connection to PeerServer. Reconnecting...");
                        peer.reconnect();
                    }
                });
            })
            .catch(err => {
                console.error("Failed to get local stream:", err);
                alert("Unable to access webcam/microphone. Please check permissions.");
            });
    } else {
        if (peer) {
            console.log("Student peer already exists:", peer.id);
            return;
        }
        peer = new Peer({
            config: {
                iceServers: [
                    { urls: "stun:stun.l.google.com:19302" },
                    { urls: "turn:turn.bistri.com:80", username: "homeo", credential: "homeo" }
                ]
            }
        });
        peer.on("open", (id) => {
            console.log("Student peer open:", id);
        });
        peer.on("call", (call) => {
            console.log("Received lecturer call:", call.peer);
            call.answer();
            call.on("stream", (remoteStream) => {
                if (remoteStream.id === attachedStreamId) {
                    console.log("Ignoring duplicate lecturer stream, ID:", remoteStream.id);
                    return;
                }
                if (isStreamAttached) {
                    console.log("Stream already attached, ignoring new stream:", remoteStream.id);
                    return;
                }
                console.log("Received lecturer stream:", remoteStream);
                isStreamAttached = true;
                attachedStreamId = remoteStream.id;
                if (video) {
                    video.srcObject = remoteStream;
                    video.onloadedmetadata = () => {
                        video.play().catch(error => console.error("Error playing lecturer stream:", error));
                    };
                }
            });
            call.on("error", (err) => console.error("Call error:", err));
        });
        peer.on("error", (err) => {
            console.error("Peer error:", err);
            if (err.type === "peer-unavailable") {
                console.warn("Lecturer not available. Waiting for session start...");
            } else if (err.type === "server-disconnected") {
                console.warn("PeerServer disconnected. Reconnecting...");
                peer.reconnect();
            }
        });
    }
});

function tryConnect(sessionId, attempt = 1, maxAttempts = 3) {
    console.log(`Attempting to connect to lecturer, attempt ${attempt}/${maxAttempts}`);
    if (!peer || peer.disconnected) {
        console.warn("Student peer not initialized or disconnected, reinitializing...");
        peer = new Peer({
            config: {
                iceServers: [
                    { urls: "stun:stun.l.google.com:19302" },
                    { urls: "turn:turn.bistri.com:80", username: "homeo", credential: "homeo" }
                ]
            }
        });
        peer.on("open", (id) => {
            console.log("Student peer open:", id);
        });
        peer.on("call", (call) => {
            console.log("Received lecturer call:", call.peer);
            call.answer();
            call.on("stream", (remoteStream) => {
                if (remoteStream.id === attachedStreamId) {
                    console.log("Ignoring duplicate lecturer stream, ID:", remoteStream.id);
                    return;
                }
                if (isStreamAttached) {
                    console.log("Stream already attached, ignoring new stream:", remoteStream.id);
                    return;
                }
                console.log("Received lecturer stream:", remoteStream);
                isStreamAttached = true;
                attachedStreamId = remoteStream.id;
                const video = document.getElementById("sessionVideo");
                if (video) {
                    video.srcObject = remoteStream;
                    video.onloadedmetadata = () => {
                        video.play().catch(error => console.error("Error playing lecturer stream:", error));
                    };
                }
            });
            call.on("error", (err) => console.error("Call error:", err));
        });
        peer.on("error", (err) => {
            console.error("Peer error:", err);
            if (err.type === "peer-unavailable") {
                console.warn("Lecturer not available. Waiting for session start...");
            } else if (err.type === "server-disconnected") {
                console.warn("PeerServer disconnected. Reconnecting...");
                peer.reconnect();
            }
        });
    }
    const conn = peer.connect(sessionId);
    conn.on("open", () => {
        console.log("Connected to lecturer:", sessionId);
        conn.send("Student peer ready: " + peer.id);
    });
    conn.on("error", (err) => {
        console.error("Connection error:", err);
        if (attempt < maxAttempts && err.type === "peer-unavailable") {
            console.warn(`Retrying connection (attempt ${attempt + 1}/${maxAttempts})...`);
            setTimeout(() => tryConnect(sessionId, attempt + 1, maxAttempts), 2000);
        }
    });
}

connection.on("SessionStarted", (sessionId) => {
    console.log("Session started by lecturer:", sessionId);
    if (!window.isSessionLecturer) {
        tryConnect(sessionId);
    }
});

connection.on("ReceiveMessage", (user, message) => {
    const chatMessages = document.getElementById("chatMessages");
    if (chatMessages) {
        chatMessages.innerHTML += `<div>${user}: ${message}</div>`;
        chatMessages.scrollTop = chatMessages.scrollHeight;
    }
});

document.getElementById("sendMessage")?.addEventListener("click", () => {
    const input = document.getElementById("chatInput");
    if (input) {
        connection.invoke("SendMessage", "User", input.value);
        input.value = "";
    }
});

document.getElementById("startSession")?.addEventListener("click", () => {
    const sessionId = document.getElementById("sessionVideo")?.dataset.sessionId;
    if (sessionId) {
        connection.invoke("StartSession", sessionId);
    }
});