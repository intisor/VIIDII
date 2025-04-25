let localStream = null;
let peer = null;

const connection = new signalR.HubConnectionBuilder()
    .withUrl("/sessionHub")
    .withAutomaticReconnect()
    .build();

connection.onclose((error) => {
    console.error("SignalR connection closed.", error);
    alert("Connection to the session was lost. Please refresh the page or try again.");
});

connection.start().then(() => {
    const { sessionId, isSessionStarted } = window.sessionState || {};
    if (sessionId && isSessionStarted && window.isSessionLecturer && !localStream) {
        connection.invoke("StartSession", sessionId);
    } else if (sessionId && !window.isSessionLecturer) {
        connection.invoke("StartSession", sessionId);
    }
});

connection.on("ReceiveMessage", (user, message) => {
    const chatMessages = document.getElementById("chatMessages");
    if (chatMessages) {
        chatMessages.innerHTML += `<div>${user}: ${message}</div>`;
        chatMessages.scrollTop = chatMessages.scrollHeight;
    }
});

connection.on("StartSession", (sessionId) => {
    const video = document.getElementById("sessionVideo");

    if (video && video.srcObject) {
        return;
    }

    if (window.isSessionLecturer) {
        if (localStream && video && !video.srcObject) {
            video.srcObject = localStream;
            video.play().catch(error => {
                console.error("Error playing video stream:", error);
                alert("Unable to play the video stream. Please check your device.");
            });
            return;
        }

        navigator.mediaDevices.getUserMedia({ video: true, audio: true })
            .then(stream => {
                localStream = stream;
                window.localStream = stream;
                if (video) {
                    video.srcObject = stream;
                    video.play();
                }
                peer = new SimplePeer({
                    initiator: true,
                    stream: stream,
                    config: { iceServers: [{ urls: "stun:stun.l.google.com:19302" }] }
                });
                setupPeerEvents(peer, sessionId);
            });
    } else {
        if (peer) return;
        peer = new SimplePeer({ config: { iceServers: [{ urls: "stun:stun.l.google.com:19302" }] } });
        setupPeerEvents(peer, sessionId);

        peer.on("track", (track, stream) => {
            if (video) {
                if (track.kind === "video") {
                    video.srcObject = stream;
                    video.play().catch(error => {
                        console.error("Error playing video stream:", error);
                        alert("Unable to play the video stream. Please check your device.");
                    });
                }
            }
        });
    }
});

function setupPeerEvents(peer, sessionId) {
    if (peer != null) {
        const processedSignals = new Set();

        peer.on("signal", data => {
            if (data.type === "answer") {
                if (!peer._pc.localDescription) {
                    console.warn("Attempted to send SDP answer before processing SDP offer. Ignoring.");
                    return;
                }
            }
            connection.invoke("SendSignal", sessionId, [data])
                .catch(err => console.error("Error invoking SendSignal:", err));
        });

        peer.on("signalingStateChange", () => {
            if (pendingAnswer) {
                if (peer._pc.signalingState == "have-local-offer") {
                    try {
                        peer.signal(pendingAnswer);
                        pendingAnswer = null;
                    } catch (error) {
                        console.error("Error processing pending SDP answer:", error);
                    }
                }
            }
        });

        peer.on("connect", () => console.log("Peer connected."));
        peer.on("close", () => console.log("Peer closed."));
        peer.on("error", (err) => {
            console.error("Peer error:", err);
            if (err.message.includes("setRemoteDescription")) {
                console.warn("Ignoring setRemoteDescription error. Peer will continue functioning.");
                return;
            }
        });
    }
}

let pendingAnswer = null;
connection.on("ReceiveSignal", (connectionId, serializedData) => {
    try {
        const signalDataArray = JSON.parse(serializedData);

        signalDataArray.forEach(signalData => {
            if (peer && !peer.destroyed) {
                if (signalData.type === "answer") {
                    if (peer._pc.signalingState !== "have-local-offer") {
                        console.warn("Storing out-of-order SDP answer. Will process later.");
                        pendingAnswer = signalData;
                        return;
                    }
                }
                peer.signal(signalData);

                let isPendingAnswerProcessed = false;
                setTimeout(() => {
                    if (pendingAnswer != null && !isPendingAnswerProcessed) {
                        if (peer._pc.signalingState === "have-local-offer") {
                            try {
                                peer.signal(pendingAnswer);
                                pendingAnswer = null;
                                isPendingAnswerProcessed = true;
                            } catch (error) {
                                console.error("Error processing pending SDP answer:", error);
                            }
                        } else {
                            console.warn("Cannot process pending SDP answer. Current signaling state:", peer._pc.signalingState);
                        }
                    }
                }, 500);
            } else {
                console.warn("Peer is not initialized or destroyed. Recreating peer...");
                recreatePeer(false, localStream);
            }
        });
    } catch (error) {
        console.error("Error processing signal data:", serializedData, error);
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