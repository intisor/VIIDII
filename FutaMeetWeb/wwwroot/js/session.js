// === Initialization ===
// Global variables for stream and session management
let localStream = null;          // Holds the local video/audio stream
let peer = null;                 // PeerJS instance for video calls
let isStreamAttached = false;    // Tracks if a stream is already attached to video
let attachedStreamId = null;     // Tracks the ID of the attached stream
let hasJoinedSession = false;    // Tracks if the user has joined the session
let lastTabStatusUpdate = 0;
const tabStatusThrottle = 50000; // 50 seconds

// Initialize SignalR connection
const connection = new signalR.HubConnectionBuilder()
    .withUrl("/sessionHub")
    .withAutomaticReconnect()
    .build();

// Function to get dynamic timestamp
function getTimestamp() {
    const options = {
        timeZone: 'Africa/Lagos',
        hour: '2-digit',
        minute: '2-digit',
        hour12: true
    };
    const time = new Date().toLocaleTimeString('en-US', options);
    return `${time} WAT`;
}

// === SignalR Connection Management ===
// Handle connection closure with error notification
connection.onclose((error) => {
    console.error("SignalR connection closed.", error);
    alert("Session connection lost. Please refresh or try again.");
});

// Handle reconnection and rejoin logic for students
connection.onreconnected(() => {
    console.log("SignalR reconnected, rejoining session...");
    const { sessionId } = window.sessionState || {};
    if (sessionId && !window.isSessionLecturer && !hasJoinedSession) {
        connection.invoke("StartSession", sessionId);
    }
});

// Start the SignalR connection and initialize session
connection.start().then(() => {
    const { sessionId, isSessionStarted } = window.sessionState || {};
    if (sessionId && isSessionStarted && window.isSessionLecturer && !localStream) {
        connection.invoke("StartSession", sessionId);
    } else if (sessionId && !window.isSessionLecturer) {
        connection.invoke("StartSession", sessionId);
    }
    // Load initial messages if session exists
    if (sessionId) {
        connection.invoke("GetMessages", sessionId);
    }
}).catch(err => console.error("SignalR connection failed:", err));

// Show/hide lecturer post input on page load (already visible per CSS)
document.addEventListener("DOMContentLoaded", () => {
    if (window.isSessionLecturer) {
        document.getElementById("postInput").style.display = "block"; // Redundant with CSS, kept for clarity
        document.getElementById("createPost").style.display = "block"; // Redundant with CSS, kept for clarity
    }
});

// === Streaming Logic ===
// Handle session start and video streaming setup
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
        // Attach existing stream or get new one for lecturer
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
        // Student setup to receive lecturer stream
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

// Function to handle student connection attempts to lecturer
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

// === Session Management ===
// Handle session start broadcast from lecturer
connection.on("SessionStarted", (sessionId) => {
    console.log("Session started by lecturer:", sessionId);
    if (!window.isSessionLecturer) {
        tryConnect(sessionId);
    }
});

// === Messaging Logic ===

// Handle sending a post on button click (lecturer only)
document.getElementById("createPost")?.addEventListener("click", () => {
    const input = document.getElementById("postInput");
    const discussion = document.getElementById("discussion");
    const sessionId = discussion?.getAttribute("data-session-id");
    console.log("Attempting to create post with sessionId:", sessionId);
    if (input && sessionId && window.isSessionLecturer) {
        const messageContent = input.value.trim();
        if (messageContent) {
            const messageBox = document.createElement("div");
            messageBox.className = "message-box";
            const messageDiv = document.createElement("div");
            messageDiv.className = "lecturer-msg";
            messageDiv.innerHTML = `
                <div class="lecturer-username">Lecturer</div>
                <div class="lecturer-content">${messageContent}</div>
                <div class="message-footer">
                    <div class="timestamp">${getTimestamp()}</div>
                    <button class="reply-btn">Reply</button>
                </div>
            `;
            messageBox.appendChild(messageDiv);
            discussion.appendChild(messageBox);
            discussion.scrollTop = discussion.scrollHeight;

            connection.invoke("CreatePost", sessionId, messageContent).catch(err => console.error("Hub error:", err));
            input.value = "";
        }
    } else {
        console.warn("Cannot create post: sessionId, input, or lecturer check failed", { sessionId, isLecturer: window.isSessionLecturer });
    }
}, { once: false });
// Handle receiving a post (for all clients)
connection.on("ReceivePost", (message) => {
    const discussion = document.getElementById("discussion");
    if (discussion) {
        const messageBox = document.createElement("div");
        messageBox.className = "message-box";
        const messageDiv = document.createElement("div");
        messageDiv.className = "lecturer-msg";
        messageDiv.setAttribute("data-post-id", message.id); // Changed from message.PostId
        messageDiv.innerHTML = `
            <div class="lecturer-username">${message.userName || "Lecturer"}</div>
            <div class="lecturer-content">${message.content}</div>
            <div class="message-footer">
                <div class="timestamp">${getTimestamp()}</div>
                <button class="reply-btn">Reply</button>
            </div>
        `;
        messageBox.appendChild(messageDiv);
        discussion.appendChild(messageBox);
        discussion.scrollTop = discussion.scrollHeight;

        messageDiv.querySelector(".reply-btn").addEventListener("click", () => {
            const replyArea = document.getElementById("replyArea");
            const replyInput = document.getElementById("replyInput");
            if (replyArea && replyInput) {
                replyArea.style.display = "block";
                replyInput.dataset.postId = message.id; // Changed from message.PostId
            }
        });
    }
});

// Handle receiving a reply (for all clients)
connection.on("ReceiveComment", (comment) => {
    const discussion = document.getElementById("discussion");
    if (discussion) {
        const parentMessage = discussion.querySelector(`[data-post-id="${comment.parentId}"]`); // Fix: Use parentId
        if (parentMessage) {
            const messageBox = document.createElement("div");
            messageBox.className = "message-box";
            const messageDiv = document.createElement("div");
            messageDiv.className = "student-msg";
            messageDiv.innerHTML = `
                <div class="student-username">${comment.userName || "Student"}</div>
                <div class="student-content">${comment.content}</div>
                <div class="message-footer">
                    <div class="timestamp">${getTimestamp()}</div>
                    <button class="reply-btn">Reply</button>
                </div>
            `;
            messageBox.appendChild(messageDiv);
            parentMessage.parentNode.insertAdjacentElement("afterend", messageBox);
            discussion.scrollTop = discussion.scrollHeight;
        } else {
            console.warn(`Parent post not found for comment with parentId: ${comment.parentId}`);
        }
    }
});
// Handle sending a reply (for students)
document.getElementById("sendReply")?.addEventListener("click", () => {
    const input = document.getElementById("replyInput");
    const replyArea = document.getElementById("replyArea");
    const { sessionId } = window.sessionState || {};
    const postId = input?.dataset.postId;
    if (input && sessionId && postId) {
        const replyContent = input.value.trim();
        if (replyContent) {
            connection.invoke("CreateComment", sessionId, postId, replyContent).catch(err => console.error("Hub error:", err));
            input.value = "";
            input.dataset.postId = "";
            replyArea.style.display = "none";
        }
    }
});

connection.on("ReceiveMessages", (messages) => {
    const discussion = document.getElementById("discussion");
    if (discussion) {
        messages.forEach(message => {
            const messageBox = document.createElement("div");
            messageBox.className = "message-box";
            const messageDiv = document.createElement("div");

            if (message.isLecturerPost && message.parentId === message.id) { // Changed from message.IsLecturerPost, message.ParentId, message.Id
                messageDiv.className = "lecturer-msg";
                messageDiv.setAttribute("data-post-id", message.id); // Changed from message.Id
                messageDiv.innerHTML = `
                    <div class="lecturer-username">${message.userName || "Lecturer"}</div>
                    <div class="lecturer-content">${message.content}</div>
                    <div class="message-footer">
                        <div class="timestamp">${getTimestamp()}</div>
                        <button class="reply-btn">Reply</button>
                    </div>
                `;
                messageBox.appendChild(messageDiv);
                discussion.appendChild(messageBox);

                messageDiv.querySelector(".reply-btn").addEventListener("click", () => {
                    const replyArea = document.getElementById("replyArea");
                    const replyInput = document.getElementById("replyInput");
                    if (replyArea && replyInput) {
                        replyArea.style.display = "block";
                        replyInput.dataset.postId = message.id; // Changed from message.Id
                    }
                });
            } else {
                messageDiv.className = "student-msg";
                messageDiv.innerHTML = `
                    <div class="student-username">${message.userName || "Student"}</div>
                    <div class="student-content">${message.content}</div>
                    <div class="message-footer">
                        <div class="timestamp">${getTimestamp()}</div>
                        <button class="reply-btn">Reply</button>
                    </div>
                `;
                messageBox.appendChild(messageDiv);
                const parentMessage = discussion.querySelector(`[data-post-id="${message.parentId}"]`); // Changed from message.ParentId
                if (parentMessage) {
                    parentMessage.parentNode.insertAdjacentElement("afterend", messageBox);
                } else {
                    discussion.appendChild(messageBox);
                }
            }
        });
        discussion.scrollTop = discussion.scrollHeight;
    }
});
connection.on("PostCreated", (postId) => {
    const discussion = document.getElementById("discussion");
    const lastMessage = discussion.lastElementChild;
    if (lastMessage && lastMessage.className === "message-box") {
        const messageDiv = lastMessage.querySelector(".lecturer-msg");
        if (messageDiv && !messageDiv.getAttribute("data-post-id")) {
            messageDiv.setAttribute("data-post-id", postId);
            messageDiv.querySelector(".reply-btn").addEventListener("click", () => {
                const replyArea = document.getElementById("replyArea");
                const replyInput = document.getElementById("replyInput");
                if (replyArea && replyInput) {
                    replyArea.style.display = "block";
                    replyInput.dataset.postId = postId;
                }
            });
        }
    }
});



// === Participant Status & Issue Reporting ===

// Respond to server ping for activity check
connection.on("AreYouThere", () => {
    if (window.isSessionLecturer) return;
    const modal = document.createElement("div");
    modal.innerHTML = `
        <div class="modal fade" id="activityModal" tabindex="-1">
            <div class="modal-dialog">
                <div class="modal-content">
                    <div class="modal-header">
                        <h5 class="modal-title">Are you still there?</h5>
                        <button type="button" class="btn-close" data-bs-dismiss="modal"></button>
                    </div>
                    <div class="modal-body">
                        <p>Please confirm you're active.</p>
                        <button id="confirmActive" class="btn btn-primary">I'm Here</button>
                    </div>
                </div>
            </div>
        </div>`;
    document.body.appendChild(modal);
    const bsModal = new bootstrap.Modal(document.getElementById("activityModal"));
    bsModal.show();
    document.getElementById("confirmActive").onclick = () => {
        connection.invoke("ConfirmActive").catch(err => console.error("Failed to confirm active:", err));
        bsModal.hide();
        modal.remove();
    };
    document.querySelector("#activityModal .btn-close").onclick = () => {
        modal.remove();
    };
});

// Report tab activity status to server (students only)
document.addEventListener("visibilitychange", () => {
    if (window.isSessionLecturer) return;
    const now = Date.now();
    if (now - lastTabStatusUpdate < tabStatusThrottle) {
        console.log("Throttled tab status update");
        return;
    }
    lastTabStatusUpdate = now;
    const isActive = !document.hidden;
    connection.invoke("UpdateTabStatus", isActive)
        .catch(err => console.error("Failed to update tab status:", err));
});

// Flag battery low issue (students only)
document.getElementById("flagBatteryLow")?.addEventListener("click", () => {
    if (window.isSessionLecturer) return;
    if ("getBattery" in navigator) {
        navigator.getBattery().then(battery => {
            let batteryLevel = battery.level * 100;
            if (batteryLevel < 15) {
                connection.invoke("FlagIssue", "BatteryLow")
                    .catch(err => console.error("Failed to flag battery issue:", err));
            } else {
                alert(`Battery level is ${batteryLevel}%. Only flag if below 15%.`);
            }
        });
    } else {
        connection.invoke("FlagIssue", "BatteryLow")
            .catch(err => console.error("Failed to flag battery issue:", err));
        console.log("Battery API not supported, flagging issue anyway.");
    }
});
// Flag data finished issue (students only)
document.getElementById("flagDataFinished")?.addEventListener("click", () => {
    if (window.isSessionLecturer) return;
    connection.invoke("FlagIssue", "DataFinished") 
        .catch(err => console.error("Failed to flag data issue:", err));
});

// Handle receiving participant list (lecturer only)
connection.on("ReceiveParticipants", (participants) => {
    const panel = document.getElementById("participantPanel");
    if (panel) {
        console.log("Received participants:", participants);
        panel.innerHTML = "";
        const list = document.createElement("ul");
        list.className = "list-group";
        Object.entries(participants).forEach(([id, name]) => {
            const item = document.createElement("li");
            item.className = "list-group-item";
            item.id = `participant-${id}`;
            item.innerHTML = `<i class="fas fa-user me-2"></i>${name}`;
            list.appendChild(item);
        });
        panel.appendChild(list);
        console.log("Panel updated:", panel.innerHTML);
    }
});

// Lecturer receives participant statuses 
connection.on("ReceiveParticipantStatuses", (statuses) => {
    if (!window.isSessionLecturer) return;
    const panel = document.getElementById("participantPanel");
    if (panel) {
        console.log("Received statuses:", statuses);
        Object.entries(statuses).forEach(([id, status]) => {
            const item = document.getElementById(`participant-${id}`);
            if (item) {
                const name = item.textContent.split(" (")[0].replace(/<i[^>]*>.*<\/i>/, "").trim();
                item.innerHTML = `<i class="${getStatusIcon(status)} me-2"></i>${name} (${status})`;
                item.className = `list-group-item ${getStatusClass(status)}`;
            }
        });
    }
});
// status icon
function getStatusIcon(status) {
    switch (status) {
        case "Active": return "fas fa-check-circle";
        case "Inactive": return "fas fa-exclamation-circle";
        case "BatteryLow": return "fas fa-battery-quarter";
        case "DataFinished": return "fas fa-signal";
        case "Disconnected": return "fas fa-times-circle";
        default: return "fas fa-user";
    }
}
// status class 
function getStatusClass(status) {
    switch (status) {
        case "Active": return "list-group-item-success";
        case "Inactive": return "list-group-item-warning";
        case "BatteryLow": return "list-group-item-danger";
        case "DataFinished": return "list-group-item-danger";
        case "Disconnected": return "list-group-item-secondary";
        default: return "";
    }
}