let localStream = null;
let peer = null;

const connection = new signalR.HubConnectionBuilder()
    .withUrl("/sessionHub")
    .withAutomaticReconnect()
    .build();

connection.onclose((error) => {
    console.error("SignalR connection closed.", error);
    alert("Session connection lost. Please refresh or try again.");
});

connection.start().then(() => {
    const { sessionId, isSessionStarted } = window.sessionState || {};
    if (sessionId && isSessionStarted && window.isSessionLecturer && !localStream) {
        connection.invoke("StartSession", sessionId);
    } else if (sessionId && !window.isSessionLecturer) {
        connection.invoke("StartSession", sessionId);
    }
});

connection.on("StartSession", (sessionId) => {
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
                            { urls: "turn:turn.bistri.com:80", username: "homeo", credential:"homeo" }                        ]
                    }
                });
                peer.on("open", () => console.log("Lecturer peer open:", sessionId));
                peer.on("connection", (conn) => {
                    conn.on("open", () => {
                        console.log("Student connected, peer ID:", conn.peer);
                        const call = peer.call(conn.peer, localStream);
                        call.on("stream", (remoteStream) => {
                            console.log("Received student stream (if any):", remoteStream);
                        });
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
                    { urls: "turn:turn.bistri.com:80", username: "homeo", credential: "homeo" }                ]
            }
        });
        peer.on("open", (id) => {
            console.log("Student peer open:", id);
            setTimeout(() => {
                const conn = peer.connect(sessionId);
                conn.on("open", () => {
                    console.log("Connected to lecturer:", sessionId);
                    conn.send("Student peer ready: " + id);
                });
                conn.on("error", (err) => console.error("Connection error:", err));
            }, 1000); // Delay to ensure peer is ready
        });
        peer.on("call", (call) => {
            console.log("Received lecturer call:", call.peer);
            call.answer();
            call.on("stream", (remoteStream) => {
                let isStreamAttached = false
                if (isStreamAttached) {
                    console.log("Ignoring duplicate lecturer stream:", remoteStream);
                    return;
                }
                console.log("Received lecturer stream:", remoteStream);
                if (video) {
                    video.srcObject = remoteStream;
                    video.onloadedmetadata = () => {
                        video.play().catch(error => console.error("Error playing lecturer stream:", error));
                    };
                    isStreamAttached = true
                }
            });
            call.on("error", (err) => console.error("Call error:", err));
        });
        peer.on("error", (err) => {
            console.error("Peer error:", err);
            if (err.type === "peer-unavailable") {
                alert("Lecturer not available. Please try again later.");
            }
        });
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