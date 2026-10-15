/**
 * LiveKit media layer for VIIDII.
 *
 * Runs ALONGSIDE sessionInterop.js, not instead of it. PeerJS is left in place until
 * this path has demonstrably worked on a real phone, so a failed experiment costs an
 * afternoon rather than the attendance and join flows.
 *
 * Why this exists: the PeerJS path signs every session through 0.peerjs.com, a free
 * public cloud with no configuration hook in the codebase. When its connection
 * negotiation fails there is nothing to fall back on. LiveKit supplies its own
 * signalling and its own relays, so neither failure has anywhere to hide.
 *
 * The token is minted by /api/livekit/token, which verifies the caller against the
 * session owner or the class roster before signing anything. This file never sees the
 * API secret and never decides who is allowed in.
 */
window.livekitInterop = (function () {
    'use strict';

    const SDK_URL = 'https://cdn.jsdelivr.net/npm/livekit-client@2.22.4/dist/livekit-client.umd.js';

    // The SDK's global is "LivekitClient", with a lower-case k. Reading it as
    // "LiveKitClient" yields undefined and looks exactly like a broken integration.
    const SDK_GLOBAL = 'LivekitClient';

    let room = null;
    let dotNetRef = null;
    let sessionId = null;
    let isLecturer = false;
    let activeVideoTrack = null;
    let mediaState = { stage: 'starting', detail: '' };
    let sdkPromise = null;

    function report(stage, detail) {
        mediaState = { stage: stage, detail: detail || '' };
        console.log('[livekit JS]', stage, detail || '');
        if (dotNetRef) {
            // The source is sent explicitly. It used to be added to the stage text in
            // C#, which meant a message from any path without a recognised prefix was
            // labelled LiveKit - including the PeerJS stall that shares this callback.
            dotNetRef.invokeMethodAsync('OnMediaState', stage, detail || '', 'livekit');
        }
    }

    function loadSdk() {
        if (window[SDK_GLOBAL]) return Promise.resolve(window[SDK_GLOBAL]);
        if (sdkPromise) return sdkPromise;

        sdkPromise = new Promise(function (resolve, reject) {
            const script = document.createElement('script');
            script.src = SDK_URL;
            script.async = true;
            script.onload = function () {
                if (window[SDK_GLOBAL]) {
                    resolve(window[SDK_GLOBAL]);
                } else {
                    // The file loaded but exposed no global. Almost always a renamed
                    // global, so say which name was expected instead of failing blank.
                    reject(new Error('SDK loaded but window.' + SDK_GLOBAL + ' is undefined'));
                }
            };
            script.onerror = function () {
                reject(new Error('Could not load the LiveKit SDK. Check the network.'));
            };
            document.head.appendChild(script);
        });

        return sdkPromise;
    }

    async function fetchToken() {
        const response = await fetch('/api/livekit/token?sessionId=' + encodeURIComponent(sessionId), {
            method: 'POST',
            credentials: 'same-origin',
            cache: 'no-store',
        });

        if (!response.ok) {
            let detail = '';
            try {
                const body = await response.json();
                detail = body.error || body.detail || body.title || '';
            } catch (e) {
                detail = '';
            }
            throw new Error(detail || 'The server would not issue a video token (HTTP ' + response.status + ').');
        }

        return response.json();
    }

    /**
     * Polls for the video element. Blazor renders asynchronously, so the element can
     * land after this call starts; a thrown error here reads to a student as a broken
     * camera with no cause.
     */
    function waitForVideoElement(id, attempts, delayMs) {
        attempts = attempts || 10;
        delayMs = delayMs || 200;

        return new Promise(function (resolve) {
            let tries = 0;
            const tick = function () {
                const el = document.getElementById(id);
                if (el) return resolve(el);
                if (tries++ >= attempts) return resolve(null);
                setTimeout(tick, delayMs);
            };
            tick();
        });
    }

    function describeRoomState(room) {
        if (!room) return 'no room';
        const parts = [];
        if (room.state) parts.push('state=' + room.state);
        if (room.engine && room.engine.signalingConnection &&
            room.engine.signalingConnection.primaryConnected !== undefined) {
            parts.push('signal=' + (room.engine.signalingConnection.primaryConnected ? 'up' : 'down'));
        }
        return parts.join(' ');
    }

    async function connect(session, isLecturerRole, reference) {
        // The session id arrives from the route parameter. It has to be passed in
        // rather than held here: nothing else in this module can know it, and an
        // unset sessionId makes every later step fail on a request for a room that
        // was never named.
        sessionId = session || null;
        dotNetRef = reference || null;
        isLecturer = !!isLecturerRole;

        if (!sessionId) {
            report('not configured', 'No session was given.');
            return { success: false };
        }

        try {
            report('asking the server for permission', sessionId);
            const token = await fetchToken();

            const lk = await loadSdk();
            const events = lk.RoomEvent || {};

            room = new lk.Room({
                // Adaptive stream and dynacast matter more than usual here: students
                // are on 3G, so only subscribe to what is actually on screen and only
                // send video when someone is watching.
                adaptiveStream: true,
                dynacast: true,
            });

            room.on(events.Connected, function () {
                report('connected to the video server', token.room);
            });
            room.on(events.SignalConnected, function () {
                console.log('[livekit JS] signalling connected');
            });
            room.on(events.Disconnected, function (why) {
                report('disconnected from the video server', String(why || ''));
            });
            room.on(events.Reconnecting, function () {
                report('reconnecting to the video server', '');
            });
            room.on(events.Reconnected, function () {
                report('reconnected to the video server', '');
            });
            room.on(events.ConnectionStateChanged, function (state) {
                console.log('[livekit JS] connection state:', state);
            });
            room.on(events.ParticipantConnected, function (p) {
                report('a student joined', p.identity);
            });
            room.on(events.ParticipantDisconnected, function (p) {
                console.log('[livekit JS] participant left:', p.identity);
            });
            room.on(events.MediaDevicesError, function (e) {
                report('camera unavailable', String(e && e.name ? e.name + ': ' + e.message : e));
            });

            // A track arriving is the event that actually matters to a student: it is
            // the difference between a black rectangle and a lecture.
            room.on(events.TrackSubscribed, async function (track, publication, participant) {
                console.log('[livekit JS] track subscribed from', participant ? participant.identity : 'unknown', track.kind);
                if (track.kind === 'video') {
                    const video = await waitForVideoElement('sessionVideo', 50, 200);
                    if (video) {
                        if (activeVideoTrack && activeVideoTrack !== track) {
                            try { activeVideoTrack.detach(video); } catch (e) {}
                        }
                        activeVideoTrack = track;
                        try {
                            track.attach(video);
                        } catch (attachErr) {
                            console.warn('[livekit JS] track attach error:', attachErr);
                        }

                        if (!video._viidiiPlayingBound) {
                            video._viidiiPlayingBound = true;
                            video.addEventListener('playing', function () {
                                console.log('[livekit JS] video element began playing frames');
                                if (dotNetRef) {
                                    dotNetRef.invokeMethodAsync('OnVideoPlaying');
                                }
                            });
                        }

                        try {
                            await video.play();
                        } catch (playErr) {
                            console.warn('[livekit JS] Autoplay prevented for video; overlay will handle it:', playErr);
                        }
                    }
                    report('video received', participant ? participant.identity : '');
                    if (dotNetRef) {
                        dotNetRef.invokeMethodAsync('OnStreamReceived');
                    }
                } else if (track.kind === 'audio') {
                    if (isLecturer) {
                        console.log('[livekit JS] Lecturer role: skipping incoming audio track to prevent audio loopback/echo');
                        return;
                    }
                    if (participant && (participant.isLocal || (room.localParticipant && participant.identity === room.localParticipant.identity))) {
                        console.log('[livekit JS] Skipping local participant audio track attachment');
                        return;
                    }
                    try {
                        track.attach();
                        if (room && room.startAudio) {
                            await room.startAudio();
                        }
                    } catch (audioErr) {
                        console.warn('[livekit JS] Audio attach or autoplay error:', audioErr);
                    }
                    console.log('[livekit JS] audio track attached and playing');
                }
            });

            room.on(events.TrackUnsubscribed, function (track, publication, participant) {
                console.log('[livekit JS] track unsubscribed from', participant ? participant.identity : 'unknown', track.kind);
                try {
                    track.detach();
                } catch (detachErr) {
                    console.warn('[livekit JS] error detaching track:', detachErr);
                }
                if (track === activeVideoTrack) {
                    activeVideoTrack = null;
                    // Fallback to remaining video track (e.g. reverting screen share to camera)
                    if (room && room.remoteParticipants) {
                        for (const [, p] of room.remoteParticipants) {
                            for (const [, pub] of p.trackPublications) {
                                if (pub.track && pub.track.kind === 'video' && pub.track !== track) {
                                    const video = document.getElementById('sessionVideo');
                                    if (video) {
                                        activeVideoTrack = pub.track;
                                        pub.track.attach(video);
                                        video.play().catch(function () {});
                                        return;
                                    }
                                }
                            }
                        }
                    }
                    report('video ended', participant ? participant.identity : '');
                    if (dotNetRef) {
                        dotNetRef.invokeMethodAsync('OnStreamLost');
                    }
                }
            });

            room.on(events.DataReceived, function (payload, participant, kind, topic) {
                if (topic === 'captions' || !topic) {
                    try {
                        const decoder = new TextDecoder();
                        const jsonStr = decoder.decode(payload);
                        const data = JSON.parse(jsonStr);
                        if (window.captionInterop && typeof window.captionInterop.onCaptionReceived === 'function') {
                            window.captionInterop.onCaptionReceived(data);
                        }
                    } catch (e) {
                        // Not caption data, ignore
                    }
                } else if (topic === 'file-transfer') {
                    handleIncomingP2PFile(payload);
                }
            });

            report('connecting to the video server', token.room);
            await room.connect(token.url, token.token);

            if (isLecturer) {
                // If the video element is already present (e.g. session already started or page refreshed mid-lecture),
                // publish immediately. If not, publishCamera() will be called when the lecturer clicks Start Session.
                const video = document.getElementById('sessionVideo');
                if (video) {
                    await publishCamera();
                } else {
                    console.log('[livekit JS] Lecturer connected. sessionVideo not yet in DOM; camera will publish upon session start.');
                }
            }

            return { success: true, room: token.room, identity: token.identity };
        } catch (err) {
            report('could not start video', String(err && err.message ? err.message : err));
            return { success: false, error: String(err && err.message ? err.message : err) };
        }
    }

    async function publishCamera() {
        if (!room) {
            console.warn('[livekit JS] publishCamera called before room connection');
            return { success: false, error: 'Not connected to video room.' };
        }
        if (!isLecturer) {
            return { success: false, error: 'Only lecturers can publish media.' };
        }

        const video = await waitForVideoElement('sessionVideo', 50, 200);
        if (!video) {
            report('camera unavailable', 'The video element never appeared. Reload and try again.');
            return { success: false, error: 'Video element not found.' };
        }

        try {
            video.muted = true;
            video.defaultMuted = true;
            video.volume = 0;
            const publication = await room.localParticipant.setCameraEnabled(true);
            if (publication && publication.track) {
                publication.track.attach(video);
                video.muted = true;
                video.defaultMuted = true;
                video.volume = 0;
                try {
                    await video.play();
                } catch (playErr) {
                    console.warn('[livekit JS] Local video play error:', playErr);
                }
                report('camera is live', 'students can now see you');
            } else {
                report('camera unavailable', 'No camera track was produced.');
            }
        } catch (err) {
            report('camera unavailable', String(err && err.name ? err.name : err));
            return { success: false, error: String(err && err.message ? err.message : err) };
        }

        try {
            await room.localParticipant.setMicrophoneEnabled(true);
            console.log('[livekit JS] microphone is live');
        } catch (micErr) {
            console.warn('[livekit JS] microphone could not be enabled:', micErr);
        }

        return { success: true };
    }

    async function unpublishCamera() {
        if (!room || !isLecturer) return { success: false };
        try {
            await room.localParticipant.setCameraEnabled(false);
            await room.localParticipant.setMicrophoneEnabled(false);
            report('camera off', '');
            return { success: true };
        } catch (err) {
            console.warn('[livekit JS] unpublishCamera error:', err);
            return { success: false, error: err.message };
        }
    }

    async function startScreenShare() {
        if (!room || !isLecturer) return { success: false, error: 'Only lecturers can share screen.' };
        try {
            const pub = await room.localParticipant.setScreenShareEnabled(true);
            const video = document.getElementById('sessionVideo');
            if (video && pub && pub.track) {
                pub.track.attach(video);
            }
            return { success: true };
        } catch (err) {
            console.warn('[livekit JS] startScreenShare error:', err);
            return { success: false, error: err.message };
        }
    }

    async function stopScreenShare() {
        if (!room || !isLecturer) return { success: false };
        try {
            await room.localParticipant.setScreenShareEnabled(false);
            const video = document.getElementById('sessionVideo');
            const camPub = room.localParticipant.getTrackPublication('camera');
            if (video && camPub && camPub.track) {
                camPub.track.attach(video);
            }
            return { success: true };
        } catch (err) {
            console.warn('[livekit JS] stopScreenShare error:', err);
            return { success: false, error: err.message };
        }
    }

    async function startAudio() {
        if (room && room.startAudio) {
            try {
                await room.startAudio();
                console.log('[livekit JS] Audio resumed successfully');
            } catch (err) {
                console.warn('[livekit JS] startAudio error:', err);
            }
        }
    }

    async function disconnect() {
        try {
            if (room) {
                await room.disconnect();
            }
        } catch (err) {
            console.warn('[livekit JS] error while disconnecting:', err);
        } finally {
            room = null;
            activeVideoTrack = null;
            report('disconnected', '');
        }
    }

    async function playVideo() {
        console.log('[livekit JS] playVideo requested');
        const video = document.getElementById('sessionVideo');
        if (video) {
            try {
                await video.play();
                console.log('[livekit JS] video.play() succeeded');
            } catch (err) {
                console.warn('[livekit JS] Unmuted play failed, trying muted:', err);
                video.muted = true;
                try {
                    await video.play();
                    console.log('[livekit JS] video.play() (muted) succeeded');
                } catch (mutedErr) {
                    console.error('[livekit JS] Muted play also failed:', mutedErr);
                }
            }
        }
        await startAudio();
    }

    function setAudioOnly(isAudioOnly) {
        console.log('[livekit JS] setAudioOnly:', isAudioOnly);
        const video = document.getElementById('sessionVideo');
        if (activeVideoTrack) {
            if (isAudioOnly) {
                if (video) try { activeVideoTrack.detach(video); } catch (e) {}
            } else {
                if (video) {
                    try {
                        activeVideoTrack.attach(video);
                        video.play();
                    } catch (e) {}
                }
            }
        }
        if (room) {
            room.remoteParticipants.forEach(function (participant) {
                participant.trackPublications.forEach(function (pub) {
                    if (pub.kind === 'video') {
                        try { pub.setEnabled(!isAudioOnly); } catch (e) {}
                    }
                });
            });
        }
        return { success: true, audioOnly: isAudioOnly };
    }

    let watermarkInterval = null;
    function enableWatermark(identifierText) {
        if (!identifierText) return;
        const video = document.getElementById('sessionVideo');
        const container = video ? video.parentElement : null;
        if (!container) return;

        let existing = document.getElementById('viidii-watermark-overlay');
        if (!existing) {
            existing = document.createElement('div');
            existing.id = 'viidii-watermark-overlay';
            existing.style.position = 'absolute';
            existing.style.pointerEvents = 'none';
            existing.style.userSelect = 'none';
            existing.style.zIndex = '25';
            existing.style.color = 'rgba(255, 255, 255, 0.28)';
            existing.style.fontSize = '12px';
            existing.style.fontWeight = '700';
            existing.style.fontFamily = 'monospace';
            existing.style.letterSpacing = '1px';
            existing.style.textShadow = '0 1px 2px rgba(0,0,0,0.6)';
            existing.style.transition = 'all 2.5s ease-in-out';
            container.style.position = 'relative';
            container.appendChild(existing);
        }

        const updatePosition = function() {
            const top = Math.floor(Math.random() * 75) + 10;
            const left = Math.floor(Math.random() * 70) + 10;
            existing.style.top = top + '%';
            existing.style.left = left + '%';
            existing.textContent = '🔒 ' + identifierText + ' • VIIDII SECURE';
        };

        updatePosition();
        if (watermarkInterval) clearInterval(watermarkInterval);
        watermarkInterval = setInterval(updatePosition, 7000);
    }

    function disableWatermark() {
        if (watermarkInterval) {
            clearInterval(watermarkInterval);
            watermarkInterval = null;
        }
        const existing = document.getElementById('viidii-watermark-overlay');
        if (existing && existing.parentElement) {
            existing.parentElement.removeChild(existing);
        }
    }

    function getMediaState() {
        return mediaState;
    }

    function publishData(payloadObj, topic) {
        if (!room || !room.localParticipant) {
            return false;
        }
        try {
            const encoder = new TextEncoder();
            const jsonStr = JSON.stringify(payloadObj);
            const data = encoder.encode(jsonStr);
            room.localParticipant.publishData(data, {
                reliable: false,
                topic: topic || 'captions'
            });
            return true;
        } catch (e) {
            console.warn('[livekit JS] publishData error:', e);
            return false;
        }
    }

    // P2P File Sharing (Zero Server Storage via WebRTC reliable data channel)
    window.p2pFiles = window.p2pFiles || new Map();
    const p2pIncomingChunks = new Map();

    window.downloadP2PFile = function (e, messageId, fileName) {
        if (e && e.preventDefault) e.preventDefault();
        const fileInfo = window.p2pFiles.get(messageId);
        if (fileInfo && fileInfo.url) {
            const a = document.createElement('a');
            a.href = fileInfo.url;
            a.download = fileInfo.fileName || fileName || 'download';
            document.body.appendChild(a);
            a.click();
            document.body.removeChild(a);
        } else {
            alert('File is still transferring via P2P stream. Please wait a moment...');
        }
    };

    function handleIncomingP2PFile(payload) {
        try {
            const view = new DataView(payload.buffer, payload.byteOffset, payload.byteLength);
            const metaLen = view.getUint32(0, true);
            const metaBytes = payload.subarray(4, 4 + metaLen);
            const chunkBytes = payload.subarray(4 + metaLen);
            const meta = JSON.parse(new TextDecoder().decode(metaBytes));

            const fileKey = meta.messageId;
            if (!p2pIncomingChunks.has(fileKey)) {
                p2pIncomingChunks.set(fileKey, new Array(meta.total));
                console.log(`[livekit JS] [P2P File] Receiving '${meta.fileName}' (${meta.total} chunks)...`);
            }

            const chunkArr = p2pIncomingChunks.get(fileKey);
            chunkArr[meta.index] = chunkBytes;

            const receivedCount = chunkArr.filter(c => c !== undefined).length;

            const downloadBtns = document.querySelectorAll(`[data-file-id="${fileKey}"]`);
            downloadBtns.forEach(btn => {
                const textSpan = btn.querySelector('.download-text');
                if (textSpan && receivedCount < meta.total) {
                    const pct = Math.round((receivedCount / meta.total) * 100);
                    textSpan.textContent = `Receiving ${pct}%`;
                }
            });

            if (receivedCount === meta.total) {
                console.log(`[livekit JS] [P2P File] All chunks received for '${meta.fileName}'. Reassembling in memory...`);
                const blob = new Blob(chunkArr, { type: meta.mimeType });
                const blobUrl = URL.createObjectURL(blob);
                window.p2pFiles.set(fileKey, { url: blobUrl, fileName: meta.fileName, size: meta.fileSize });
                p2pIncomingChunks.delete(fileKey);

                downloadBtns.forEach(btn => {
                    btn.href = blobUrl;
                    btn.download = meta.fileName;
                    const textSpan = btn.querySelector('.download-text');
                    if (textSpan) textSpan.textContent = 'Download';
                });

                if (!isLecturer) {
                    const tempLink = document.createElement('a');
                    tempLink.href = blobUrl;
                    tempLink.download = meta.fileName;
                    document.body.appendChild(tempLink);
                    tempLink.click();
                    document.body.removeChild(tempLink);
                }
            }
        } catch (err) {
            console.error('[livekit JS] Error receiving file chunk:', err);
        }
    }

    async function sendP2PFile(inputElementId, messageId) {
        if (!room || !room.localParticipant) {
            console.warn('[livekit JS] sendP2PFile: room not connected');
            return { success: false, error: 'Room not connected' };
        }
        const fileInput = document.getElementById(inputElementId);
        if (!fileInput || !fileInput.files || fileInput.files.length === 0) {
            console.warn('[livekit JS] sendP2PFile: no file selected in element', inputElementId);
            return { success: false, error: 'No file selected' };
        }
        const file = fileInput.files[0];
        console.log(`[livekit JS] [P2P File] Starting transmission for '${file.name}', size: ${file.size} bytes, messageId: ${messageId}`);

        // Store sender's local Blob URL so sender can also download immediately from memory
        const localUrl = URL.createObjectURL(file);
        window.p2pFiles.set(messageId, { url: localUrl, fileName: file.name, size: file.size });

        const chunkSize = 60 * 1024; // 60KB per chunk for WebRTC reliable data channel
        const totalChunks = Math.ceil(file.size / chunkSize);

        for (let i = 0; i < totalChunks; i++) {
            const start = i * chunkSize;
            const end = Math.min(start + chunkSize, file.size);
            const slice = file.slice(start, end);
            const arrayBuffer = await slice.arrayBuffer();
            const chunkBytes = new Uint8Array(arrayBuffer);

            const metaStr = JSON.stringify({
                type: 'p2pFileChunk',
                messageId: messageId,
                fileName: file.name,
                fileSize: file.size,
                mimeType: file.type || 'application/octet-stream',
                index: i,
                total: totalChunks
            });
            const metaBytes = new TextEncoder().encode(metaStr);

            // Frame format: [4 bytes uint32 metaLen][metaBytes][raw chunkBytes]
            const packet = new Uint8Array(4 + metaBytes.length + chunkBytes.length);
            const view = new DataView(packet.buffer);
            view.setUint32(0, metaBytes.length, true);
            packet.set(metaBytes, 4);
            packet.set(chunkBytes, 4 + metaBytes.length);

            try {
                await room.localParticipant.publishData(packet, {
                    reliable: true,
                    topic: 'file-transfer'
                });
            } catch (err) {
                console.error(`[livekit JS] Error publishing chunk ${i + 1}/${totalChunks}:`, err);
            }

            if (i % 5 === 0) {
                await new Promise(r => setTimeout(r, 8));
            }
        }

        console.log(`[livekit JS] [P2P File] Completed transmission of ${totalChunks} chunks for '${file.name}'`);
        return { success: true, fileName: file.name, totalChunks: totalChunks };
    }

    return {
        connect: connect,
        disconnect: disconnect,
        publishCamera: publishCamera,
        unpublishCamera: unpublishCamera,
        startScreenShare: startScreenShare,
        stopScreenShare: stopScreenShare,
        startAudio: startAudio,
        playVideo: playVideo,
        setAudioOnly: setAudioOnly,
        enableWatermark: enableWatermark,
        disableWatermark: disableWatermark,
        getMediaState: getMediaState,
        publishData: publishData,
        sendP2PFile: sendP2PFile,
        isConfigured: function () { return true; },
    };
})();
