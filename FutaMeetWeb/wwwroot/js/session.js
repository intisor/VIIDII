let localStream = null;
let peer = null;
let isStreamAttached = false;
let attachedStreamId = null;
let hasJoinedSession = false;
let lastTabStatusUpdate = 0;
const tabStatusThrottle = 50000; // 50 seconds
let studentPeers = []; // Track student peer IDs
const studentConnections = new Map(); // Track open data channels
const fileChunks = new Map(); // Moved: Persist chunks across retries


const connection = new signalR.HubConnectionBuilder()
    .withUrl("/sessionHub")
    .withAutomaticReconnect()
    .build();

function getTimestamp() {
    const options = {
        timeZone: 'Africa/Lagos',
        hour: '2-digit',
        minute: '2-digit',
        hour12: true
    };
    return `${new Date().toLocaleTimeString('en-US', options)} WAT`;
}

connection.onclose((error) => {
    console.error("SignalR connection closed.", error);
    alert("Session connection lost. Please refresh or try again.");
});

connection.onreconnected(() => {
    console.log("SignalR reconnected, rejoining session...");
    const { sessionId } = window.sessionState || {};
    if (sessionId && !window.isSessionLecturer) {
        console.log("Reconnecting to session:", sessionId);
        connection.invoke("JoinSession", sessionId);
        tryConnect(sessionId);
    }
});

connection.start().then(() => {
    console.log("SignalR connection started.");
    const { sessionId, isSessionStarted } = window.sessionState || {};
    if (sessionId && isSessionStarted && window.isSessionLecturer && !localStream) {
        console.log("Starting session as lecturer:", sessionId);
        connection.invoke("StartSession", sessionId);
    } else if (sessionId && !window.isSessionLecturer) {
        console.log("Joining session as student:", sessionId);
        connection.invoke("JoinSession", sessionId)
            .then(() => console.log("JoinSession invoked successfully"))
            .catch(err => console.error("JoinSession failed:", err));
    }
    if (sessionId) {
        console.log("Loading initial messages for session:", sessionId);
        connection.invoke("GetMessages", sessionId);
    }
}).catch(err => console.error("SignalR connection failed:", err));

document.addEventListener("DOMContentLoaded", () => {
    console.log("DOM fully loaded and parsed.");
    if (window.isSessionLecturer) {
        document.getElementById("postInput").style.display = "block";
        document.getElementById("createPost").style.display = "block";
        document.getElementById("fileInput").style.display = "inline-block";
    }
});

connection.on("StartSession", (sessionId) => {
    console.log("StartSession event received for session:", sessionId);
    if (hasJoinedSession) {
        console.log("Already joined, attempting to reattach stream:", sessionId);
        tryConnect(sessionId);
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
        console.log("Setting up lecturer stream.");

        window.startScreenShare = async function () {
            try {
                const screenStream = await navigator.mediaDevices.getDisplayMedia({
                    audio: false,
                    video: true,
                });
                console.log("Screen captured:", screenStream);
                if (video) {
                    video.srcObject = screenStream;
                }

                // Handle screen sharing stop
                screenStream.getVideoTracks()[0].addEventListener('ended', () => {
                    console.log("Screen sharing stopped.");
                    alert("Screen sharing stopped.");
                    if (localStream && video) {
                        console.log("Reverting to local stream.");
                        video.srcObject = localStream; // Revert to original webcam stream
                    } else {
                        console.warn("No localStream or video element available to revert to.");
                    }
                });
            } catch (error) {
                console.error("Error sharing screen:", error);
                alert("Failed to share screen. Please check permissions.");
            }
        };
        if (localStream && video && !video.srcObject) {
            console.log("Attaching existing stream to video element.");
            video.srcObject = localStream;
            return;
        }
        navigator.mediaDevices.getUserMedia({ video: true, audio: true })
            .then(stream => {
                console.log("Successfully obtained local stream:", stream);
                localStream = stream;
                window.localStream = stream;
                if (video) {
                    console.log("Attaching local stream to video element.");
                    video.srcObject = stream;
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
                    console.log("Student connection received:", conn.peer);
                    conn.on("open", () => {
                        console.log("Student connected, peer ID:", conn.peer);
                        if (!conn.peer) {
                            console.warn("Invalid student peer ID, skipping call");
                            return;
                        }
                        studentConnections.set(conn.peer, conn); // Store connection

                        const call = peer.call(conn.peer, localStream);
                        call.on("open", () => console.log("Call to student opened:", conn.peer));
                        call.on("error", (err) => console.error("Call error:", err));
                    });
                    conn.on("data", (data) => {
                        console.log("Received student data:", data);
                        if (data.type === "studentReady" && !studentPeers.includes(data.studentId)) {
                            console.log(`Adding student peer ID: ${data.studentId}`);
                            studentPeers.push(data.studentId);
                        }
                    });
                    conn.on("close", () => {
                        console.log("Student connection closed:", conn.peer);
                        studentConnections.delete(conn.peer);
                        studentPeers = studentPeers.filter(id => id !== conn.peer);
                    });
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
        console.log("Setting up student to receive lecturer stream.");
        setupStudentPeer(sessionId, video);
    }
});

function setupStudentPeer(sessionId, video) {
    if (peer && !peer.disconnected) {
        console.log("Student peer exists:", peer.id);
        tryConnect(sessionId);
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
        connection.invoke("SendPeerId", sessionId, id).catch(err => console.error("Failed to send peer ID:", err));
        tryConnect(sessionId);
    });
    peer.on("call", (call) => {
        console.log("Received lecturer call:", call.peer);
        call.answer();
        call.on("stream", (remoteStream) => {
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
            }
        });
        call.on("error", (err) => console.error("Call error:", err));
    });
   
    peer.on("error", (err) => {
        console.error("Peer error:", err);
        if (err.type === "peer-unavailable") {
            console.warn("Lecturer not available, retrying...");
            setTimeout(() => tryConnect(sessionId), 3000);
        } else if (err.type === "server-disconnected") {
            console.warn("PeerServer disconnected, reconnecting...");
            peer.reconnect();
        }
    });
}

function tryConnect(sessionId, attempt = 1, maxAttempts = 15) {
    console.log(`Attempting to connect to lecturer, attempt ${attempt}/${maxAttempts}`);
    if (!peer || peer.disconnected) {
        console.warn("Student peer not initialized or disconnected, reinitializing...");
        const video = document.getElementById("sessionVideo");
        setupStudentPeer(sessionId, video);
        return;
    }
    const conn = peer.connect(sessionId);

    conn.on("open", () => {
        console.log("Connected to lecturer:", sessionId);
        conn.send("Student peer ready: " + peer.id);
    });
    conn.on("data", (data) => {
        console.log("Received data from lecturer:", data);
        if (data.type === "fileChunk") {
            console.log(`Received chunk ${data.index + 1}/${data.total} for ${data.fileName}, size: ${data.chunk.size} bytes`);
            const fileKey = `${data.fileName}-${data.messageId}`;
            if (!fileChunks.has(fileKey)) {
                console.log(`Initializing chunk array for ${fileKey}, expecting ${data.total} chunks`);
                fileChunks.set(fileKey, new Array(data.total));
            }
            fileChunks.get(fileKey)[data.index] = data.chunk;
            console.log(`Stored chunk ${data.index + 1} for ${fileKey}, received chunks: ${fileChunks.get(fileKey).filter(Boolean).length}/${data.total}`);

            if (fileChunks.get(fileKey).filter(Boolean).length === data.total) {
                console.log(`All chunks received for ${fileKey}, reassembling file`);
                const blob = new Blob(fileChunks.get(fileKey));
                console.log(`File ${data.fileName} reassembled, size: ${blob.size} bytes`);
                const url = URL.createObjectURL(blob);
                console.log(`Created Blob URL: ${url}`);

                const downloadLink = document.querySelector(`.file-download[data-file-id="${data.messageId}"]`);
                if (downloadLink) {
                    console.log(`Updating download link for ${data.messageId}`);
                    downloadLink.href = url;
                    downloadLink.download = data.fileName;
                    downloadLink.textContent = "Download";
                } else {
                    console.warn(`Download link not found for messageId: ${data.messageId}`);
                }

                console.log(`Triggering auto-download for ${data.fileName}`);
                const tempLink = document.createElement("a");
                tempLink.href = url;
                tempLink.download = data.fileName;
                document.body.appendChild(tempLink);
                tempLink.click();
                document.body.removeChild(tempLink);

                console.log(`Cleaning up chunks for ${fileKey}`);
                fileChunks.delete(fileKey);
            }
        }
    });
    conn.on("error", (err) => {
        console.error("Connection error:", err);
        if (attempt < maxAttempts && err.type === "peer-unavailable") {
            console.warn(`Retrying connection (attempt ${attempt + 1}/${maxAttempts})...`);
            setTimeout(() => tryConnect(sessionId, attempt + 1, maxAttempts), 3000);
        } else {
            console.error("Max connection attempts reached or fatal error:", err);
        }
    });
}

connection.on("SessionStarted", (sessionId) => {
    console.log("Session started by lecturer:", sessionId);
    if (!window.isSessionLecturer) {
        tryConnect(sessionId);
    }
});

// === Messaging Logic ===
document.getElementById("createPost")?.addEventListener("click", () => {
    const input = document.getElementById("postInput");
    const fileInput = document.getElementById("fileInput");
    const discussion = document.getElementById("discussion");
    const sessionId = discussion?.getAttribute("data-session-id");
    console.log("Attempting to create post with sessionId:", sessionId);
    if (fileInput?.files.length > 0 && sessionId && window.isSessionLecturer) {
        const file = fileInput.files[0];
        console.log(`Selected file: ${file.name}, size: ${file.size} bytes`);
        if (file.size > 50 * 1024 * 1024) {
            console.error("File too large (max 50MB)");
            alert("File too large (max 50MB).");
            return;
        }
        if (studentPeers.length === 0) {
            console.warn("No students connected to send file to");
            alert("No students connected.");
            return;
        }
        const fileMessageContent = `File: ${file.name}`;
        console.log(`Creating file message: ${fileMessageContent}`);
        connection.invoke("CreatePost", sessionId, fileMessageContent, true)
            .then(() => console.log("File message created successfully"))
            .catch(err => {
                console.error("Failed to create file message:", err);
                alert("Failed to send file.");
            });

        const chunkSize = 1024 * 1024; // 1MB
        const chunks = [];
        for (let start = 0; start < file.size; start += chunkSize) {
            chunks.push(file.slice(start, start + chunkSize));
        }
        console.log(`Created ${chunks.length} chunks for ${file.name}, chunk size: ${chunkSize} bytes`);

        for (const studentId of studentPeers) {
            const conn = studentConnections.get(studentId);
            if (!conn) {
                console.warn(`No open connection for student: ${studentId}, skipping`);
                continue;
            }
            console.log(`Sending ${chunks.length} chunks to student: ${studentId}`);
            chunks.forEach((chunk, index) => {
                console.log(`Sending chunk ${index + 1}/${chunks.length} for ${file.name} to ${studentId}, size: ${chunk.size} bytes`);
                conn.send({
                    type: "fileChunk",
                    fileName: file.name,
                    chunk,
                    index,
                    total: chunks.length,
                    messageId: fileMessageContent
                });
            });
            conn.on("error", (err) => console.error(`Failed to send to ${studentId}:`, err));
        }
        console.log(`Cleared inputs after sending file: ${file.name}`);
        fileInput.value = "";
        input.value = "";
    }
    else if (input && sessionId && window.isSessionLecturer) {
        const messageContent = input.value.trim();
        if (messageContent) {
            // Client-side rendering of the post is removed.
            // The server will send the post back via ReceivePost, which will handle rendering.
            connection.invoke("CreatePost", sessionId, messageContent, false)
                .catch(err => console.error("CreatePost Hub error:", err));
            // Input is cleared in PostCreated handler now, or could be cleared here if preferred.
        }
    }
}, { once: false });

connection.on("ReceivePost", (message) => {
    const discussion = document.getElementById("discussion");
    if (discussion) {
        const messageBox = document.createElement("div");
        messageBox.className = "message-box";
        const messageDiv = document.createElement("div");
        messageDiv.className = "message lecturer-msg"; // Added 'message' class
        messageDiv.setAttribute("data-post-id", message.id);

        let contentHtml = message.content;
        let isFileMessage = message.content.startsWith("File:");
        if (isFileMessage && !window.isSessionLecturer) {
            const fileName = message.content.replace("File: ", "");
            contentHtml = `${fileName} <a href="#" class="file-download" data-file-id="${message.content}">Download</a>`;
        }

        messageDiv.innerHTML = `
            <div class="username">${message.userName || "Lecturer"}</div>
            <div class="content">${contentHtml}</div>
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
                replyInput.dataset.postId = message.id;
            }
        });
    }
});

connection.on("ReceiveComment", (comment) => {
    const discussion = document.getElementById("discussion");
    if (discussion) {
        const parentMessage = discussion.querySelector(`[data-post-id="${comment.parentId}"]`);
        if (parentMessage) {
            const messageBox = document.createElement("div");
            messageBox.className = "message-box";
            const messageDiv = document.createElement("div");
            // Determine if the comment is from the current user or another student/lecturer
            let messageClass = "message student-msg";
            if (comment.userId === window.currentUserId) {
                messageClass = "message self-reply-msg"; 
            } else if (comment.isLecturerPost) { // Check if the commenter is a lecturer
                messageClass = "message lecturer-reply-msg";
            } else {
                messageClass = "message student-reply-msg";
            }
            messageDiv.className = messageClass;
            messageDiv.innerHTML = `
                <div class="username">${comment.userName || "User"}</div>
                <div class="content">${comment.content}</div>
                <div class="message-footer">
                    <div class="timestamp">${getTimestamp()}</div>
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
        discussion.innerHTML = ''; // Clear existing messages before loading all

        const postElements = {}; // To store created post messageBox elements: { postId: element }

        // First pass: Render all posts
        messages.filter(m => m.isLecturerPost && m.parentId === m.id).forEach(message => {
            const messageBox = document.createElement("div");
            messageBox.className = "message-box";
            const messageDiv = document.createElement("div");
            messageDiv.className = "message lecturer-msg";
            messageDiv.setAttribute("data-post-id", message.id);
            messageDiv.innerHTML = `
                <div class="username">${message.userName || "Lecturer"}</div>
                <div class="content">${message.content}</div>
                <div class="message-footer">
                    <div class="timestamp">${getTimestamp()}</div>
                    <button class="reply-btn">Reply</button>
                </div>
            `;
            messageBox.appendChild(messageDiv);
            discussion.appendChild(messageBox);
            postElements[message.id] = messageBox; // Store the messageBox which is the parent for insertAdjacentElement

            messageDiv.querySelector(".reply-btn").addEventListener("click", () => {
                const replyArea = document.getElementById("replyArea");
                const replyInput = document.getElementById("replyInput");
                if (replyArea && replyInput) {
                    replyArea.style.display = "block";
                    replyInput.focus();
                    replyInput.dataset.postId = message.id;
                }
            });
        });

        // Second pass: Render all comments and attach them
        // Sort comments by timestamp or ID to ensure they are appended in order relative to each other under the same post
        const comments = messages.filter(m => m.parentId !== m.id).sort((a, b) => new Date(a.timestamp) - new Date(b.timestamp) || a.id.localeCompare(b.id));

        comments.forEach(comment => {
            const parentPostBox = postElements[comment.parentId];
            if (parentPostBox) {
                const messageBox = document.createElement("div");
                messageBox.className = "message-box";
                const messageDiv = document.createElement("div");
                
                let messageClass = "message student-msg"; // Default
                if (comment.userId === window.currentUserId) {
                    messageClass = "message self-reply-msg";
                } else if (comment.isLecturerPost) { // Commenter is a lecturer
                    messageClass = "message lecturer-reply-msg";
                } else { // Another student's reply
                    messageClass = "message student-reply-msg";
                }
                messageDiv.className = messageClass;
                messageDiv.innerHTML = `
                    <div class="username">${comment.userName || "User"}</div>
                    <div class="content">${comment.content}</div>
                    <div class="message-footer">
                        <div class="timestamp">${getTimestamp()}</div>
                    </div>
                `;
                messageBox.appendChild(messageDiv);
                // Insert the new comment messageBox after the parentPostBox or after the last reply to parentPostBox
                // To ensure replies are ordered correctly under a post, find the last reply to parentPostBox
                let lastReplyToParent = parentPostBox;
                let nextSibling = parentPostBox.nextElementSibling;
                while(nextSibling && nextSibling.classList.contains('message-box')){
                    // This logic assumes replies are directly after the post or other replies to the same post.
                    // A more robust way would be to check if the message inside nextSibling is a reply to parentPostBox.id
                    // For now, this simpler check might work if DOM structure is consistent.
                    // To be very precise, we'd need to find the actual last reply to *this specific post*.
                    // However, since we sorted comments, appending them sequentially after the parent post should work.
                    lastReplyToParent = nextSibling;
                    nextSibling = nextSibling.nextElementSibling;
                }
                lastReplyToParent.insertAdjacentElement("afterend", messageBox);

            } else {
                console.warn(`ReceiveMessages: Parent post with ID ${comment.parentId} not found for comment. Appending to end of discussion as orphan.`);
                const messageBox = document.createElement("div");
                messageBox.className = "message-box";
                const messageDiv = document.createElement("div");
                // Determine class for orphaned comment
                let orphanMessageClass = "message student-reply-msg"; // Default
                if (comment.userId === window.currentUserId) {
                    orphanMessageClass = "message self-reply-msg";
                } else if (comment.isLecturerPost) {
                    orphanMessageClass = "message lecturer-reply-msg";
                }
                messageDiv.className = orphanMessageClass;
                messageDiv.innerHTML = `
                    <div class="username">${comment.userName || "User"}</div>
                    <div class="content">${comment.content} (Orphaned: Parent post not found)</div>
                    <div class="message-footer">
                        <div class="timestamp">${getTimestamp()}</div>
                    </div>
                `;
                messageBox.appendChild(messageDiv);
                discussion.appendChild(messageBox);
            }
        });
        discussion.scrollTop = discussion.scrollHeight;
    }
});

connection.on("PostCreated", (postId) => {
    // This handler is now primarily for UI cues specific to the sender, like clearing the input field.
    // The actual message rendering and data-post-id attribute setting is handled by ReceivePost.
    const input = document.getElementById("postInput");
    if (input) {
        input.value = ""; // Clear the input field after successful post creation.
    }
    console.log("PostCreated event received, postId:", postId);
    // If there are other UI elements that need to react to the post being successfully created by the current user,
    // those can be handled here. For example, temporarily disabling the post button until confirmation.
});

connection.on("AreYouThere", () => {
    if (window.isSessionLecturer) return;
    const activityModal = document.getElementById("activityModal");
    if (!activityModal) {
        console.error("Activity modal not found in DOM");
        return;
    }
    let bsModal = bootstrap.Modal.getInstance(activityModal) || new bootstrap.Modal(activityModal, { backdrop: false });
    bsModal.show();
    const confirmButton = document.getElementById("confirmActive");
    const closeButton = activityModal.querySelector(".btn-close");
    const hideModal = () => {
        bsModal.hide();
        const backdrop = document.querySelector(".modal-backdrop");
        if (backdrop) backdrop.remove();
        document.body.classList.remove("modal-open");
    };
    if (!confirmButton.dataset.listener) {
        confirmButton.addEventListener("click", () => {
            connection.invoke("ConfirmActive").catch(err => console.error("Failed to confirm active:", err));
            hideModal();
        });
        confirmButton.dataset.listener = "true";
    }
    if (!closeButton.dataset.listener) {
        closeButton.addEventListener("click", hideModal);
        closeButton.dataset.listener = "true";
    }
});

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

document.getElementById("flagDataFinished")?.addEventListener("click", () => {
    if (window.isSessionLecturer) return;
    connection.invoke("FlagIssue", "DataFinished")
        .catch(err => console.error("Failed to flag data issue:", err));
});

connection.on("ReceivePeerId", (userId, peerId) => {
    console.log(`Received peer ID: ${peerId} for user: ${userId}`);
    if (!window.isSessionLecturer) return;
    if (!studentPeers.includes(peerId)) {
        console.log(`Adding student peer ID: ${peerId}`);
        studentPeers.push(peerId);
    }
});
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

connection.on("ReceiveParticipantStatuses", (statuses) => {
    if (!window.isSessionLecturer) return;
    const panel = document.getElementById("participantPanel");
    if (panel) {
        console.log("Received statuses:", statuses);
        Object.entries(statuses).forEach(([id, status]) => {
            const item = document.getElementById(`participant-${id}`);
            if (item) {
                const name = item.textContent.split(" (")[0].replace(/<i[^>]*>.*<\/i>/, "").trim();
                const statusString = mapStatusToString(status);
                item.innerHTML = `<i class="${getStatusIcon(statusString)} me-2"></i>${name} (${statusString})`;
                item.className = `list-group-item ${getStatusClass(statusString)}`;
            }
        });
    }
});

function mapStatusToString(status) {
    switch (String(status)) {
        case "0": return "Active";
        case "1": return "Inactive";
        case "2": return "BatteryLow";
        case "3": return "DataFinished";
        case "4": return "Disconnected";
        default: return String(status);
    }
}

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