/**
 * Live Captions (Speech-to-Text) module for VIIDII.
 * 
 * Uses browser-native Web Speech API (SpeechRecognition) for 100% free, zero-latency
 * client-side speech-to-text.
 * Captions are broadcasted to all session attendees over LiveKit WebRTC Data Channel
 * with SignalR fallback.
 */
window.captionInterop = (function () {
    'use strict';

    let recognition = null;
    let isBroadcasting = false;
    let isCaptionsEnabled = true; // Default enabled so users see captions when host speaks
    let currentSpeaker = 'Host';
    let currentLang = 'en-US';
    let fadeTimeout = null;
    let dotNetRef = null;
    let sessionId = null;
    let recognitionErrorCount = 0;
    let recognitionRestartTimeout = null;

    // Check speech recognition support
    function isSupported() {
        return !!(window.SpeechRecognition || window.webkitSpeechRecognition);
    }

    function init(dotNet, sId, isLecturer) {
        dotNetRef = dotNet;
        sessionId = sId;

        // Restore user preference
        const savedPref = localStorage.getItem('viidii_captions_enabled');
        if (savedPref !== null) {
            isCaptionsEnabled = savedPref === 'true';
        }

        console.log('[captionInterop] Initialized. Supported:', isSupported(), 'CaptionsEnabled:', isCaptionsEnabled);
    }

    function startBroadcasting(speaker, lang) {
        if (!isSupported()) {
            console.warn('[captionInterop] Speech Recognition is not supported in this browser.');
            return { success: false, error: 'Speech Recognition is supported in Chrome, Edge, Safari, and mobile browsers.' };
        }

        currentSpeaker = speaker || 'Host';
        currentLang = lang || 'en-US';
        isBroadcasting = true;

        try {
            const SpeechRec = window.SpeechRecognition || window.webkitSpeechRecognition;
            recognition = new SpeechRec();
            recognition.continuous = true;
            recognition.interimResults = true;
            recognition.lang = currentLang;

            recognition.onstart = function () {
                console.log('[captionInterop] Live speech recognition started.');
                if (dotNetRef) {
                    dotNetRef.invokeMethodAsync('OnCaptionBroadcastStateChanged', true);
                }
            };

            recognition.onresult = function (event) {
                let interimTranscript = '';
                let finalTranscript = '';

                for (let i = event.resultIndex; i < event.results.length; ++i) {
                    const transcriptPart = event.results[i][0].transcript;
                    if (event.results[i].isFinal) {
                        finalTranscript += transcriptPart;
                    } else {
                        interimTranscript += transcriptPart;
                    }
                }

                const text = (finalTranscript || interimTranscript).trim();
                const isFinal = !!finalTranscript;

                if (text.length > 0) {
                    recognitionErrorCount = 0;
                    recordTranscript(text, isFinal);
                    broadcastCaption(currentSpeaker, text, isFinal);
                    // Also show on host's own screen if they have captions on
                    if (isCaptionsEnabled) {
                        renderCaption(currentSpeaker, text, isFinal);
                    }
                }
            };

            recognition.onerror = function (event) {
                console.warn('[captionInterop] Speech recognition event error:', event.error);
                recognitionErrorCount++;
                if (event.error === 'not-allowed') {
                    isBroadcasting = false;
                    if (dotNetRef) {
                        dotNetRef.invokeMethodAsync('OnCaptionBroadcastStateChanged', false);
                    }
                }
            };

            recognition.onend = function () {
                if (recognitionRestartTimeout) {
                    clearTimeout(recognitionRestartTimeout);
                    recognitionRestartTimeout = null;
                }

                // Browsers automatically stop recognition after pauses. Auto-restart if broadcaster still active!
                if (isBroadcasting) {
                    const delay = recognitionErrorCount > 2 ? 4000 : 350;
                    recognitionRestartTimeout = setTimeout(function () {
                        if (isBroadcasting) {
                            try {
                                recognition.start();
                            } catch (e) {
                                // Already started or restarting
                            }
                        }
                    }, delay);
                } else {
                    if (dotNetRef) {
                        dotNetRef.invokeMethodAsync('OnCaptionBroadcastStateChanged', false);
                    }
                }
            };

            recognition.start();
            return { success: true };
        } catch (err) {
            console.error('[captionInterop] Error starting speech recognition:', err);
            isBroadcasting = false;
            return { success: false, error: String(err && err.message ? err.message : err) };
        }
    }

    function stopBroadcasting() {
        isBroadcasting = false;
        recognitionErrorCount = 0;
        if (recognitionRestartTimeout) {
            clearTimeout(recognitionRestartTimeout);
            recognitionRestartTimeout = null;
        }
        if (recognition) {
            try {
                recognition.stop();
            } catch (e) {
                // Ignore
            }
            recognition = null;
        }
        if (dotNetRef) {
            dotNetRef.invokeMethodAsync('OnCaptionBroadcastStateChanged', false);
        }
        return { success: true };
    }

    function toggleBroadcasting(speaker, lang) {
        if (isBroadcasting) {
            return stopBroadcasting();
        } else {
            return startBroadcasting(speaker, lang);
        }
    }

    function broadcastCaption(speaker, text, isFinal) {
        const payload = {
            type: 'caption',
            speaker: speaker,
            text: text,
            isFinal: isFinal,
            timestamp: Date.now()
        };

        // 1. Broadcast via LiveKit WebRTC Data Channel (Ultra-low latency ~30ms)
        let sentViaLiveKit = false;
        if (window.livekitInterop && typeof window.livekitInterop.publishData === 'function') {
            sentViaLiveKit = window.livekitInterop.publishData(payload, 'captions');
        }

        // 2. Broadcast via SignalR (Fallback for reliability)
        if (!sentViaLiveKit && window.sessionHub) {
            try {
                window.sessionHub.invoke('SendLiveCaption', sessionId, speaker, text, isFinal);
            } catch (err) {
                // Silently handle
            }
        }
    }

    function onCaptionReceived(data) {
        if (!data || data.type !== 'caption') return;
        recordTranscript(data.text, data.isFinal);
        if (!isCaptionsEnabled) return;

        renderCaption(data.speaker || 'Host', data.text, data.isFinal);
    }

    function toggleCaptions() {
        isCaptionsEnabled = !isCaptionsEnabled;
        localStorage.setItem('viidii_captions_enabled', String(isCaptionsEnabled));

        const overlay = document.getElementById('captionOverlay');
        if (overlay) {
            if (!isCaptionsEnabled) {
                overlay.style.display = 'none';
            }
        }

        console.log('[captionInterop] Captions toggled:', isCaptionsEnabled);
        return isCaptionsEnabled;
    }

    function renderCaption(speaker, text, isFinal) {
        if (!isCaptionsEnabled) return;

        let overlay = document.getElementById('captionOverlay');
        if (!overlay) {
            const container = document.getElementById('videoContainer') || document.querySelector('.video-container');
            if (!container) return;

            overlay = document.createElement('div');
            overlay.id = 'captionOverlay';
            overlay.className = 'caption-overlay-container';
            overlay.innerHTML = '<div class="caption-bubble"><span class="caption-speaker" id="captionSpeaker"></span><span class="caption-text" id="captionText"></span></div>';
            container.appendChild(overlay);
        }

        const speakerEl = document.getElementById('captionSpeaker');
        const textEl = document.getElementById('captionText');

        if (speakerEl) speakerEl.textContent = (speaker ? speaker + ': ' : '');
        if (textEl) textEl.textContent = text;

        overlay.style.display = 'flex';

        // Auto-fade captions after 4 seconds of silence if this is final
        if (fadeTimeout) clearTimeout(fadeTimeout);
        if (isFinal) {
            fadeTimeout = setTimeout(function () {
                if (overlay) {
                    overlay.style.display = 'none';
                }
            }, 4500);
        }
    }

    let transcriptHistory = [];

    function recordTranscript(text, isFinal) {
        if (!isFinal || !text || text.length < 5) return;
        transcriptHistory.push({
            text: text.trim(),
            timestamp: Date.now()
        });
        if (transcriptHistory.length > 30) {
            transcriptHistory.shift();
        }
    }

    function getRecentTranscript(limit) {
        limit = limit || 10;
        return transcriptHistory.slice(-limit).map(function (t) { return t.text; });
    }

    function generateEngagementQuiz(sessionTitle) {
        sessionTitle = sessionTitle || 'the current lecture';

        // Filter sentences with at least 4 words
        const candidates = transcriptHistory
            .slice(-10)
            .map(function (t) { return t.text; })
            .filter(function (t) { return t.split(/\s+/).length >= 4; });

        if (candidates.length > 0) {
            const selectedText = candidates[candidates.length - 1];
            const words = selectedText.split(/\s+/);
            const quoteSnippet = words.length > 12 
                ? words.slice(0, 12).join(' ') + '...'
                : selectedText;

            const correctOption = selectedText.length > 48 
                ? selectedText.substring(0, 45) + '...'
                : selectedText;

            const distractor1 = "This topic will only be covered next semester in practicals";
            const distractor2 = "A previous general topic unrelated to this slide";

            const options = [correctOption, distractor1, distractor2];

            const correctIndex = Math.floor(Math.random() * 3);
            if (correctIndex !== 0) {
                const temp = options[0];
                options[0] = options[correctIndex];
                options[correctIndex] = temp;
            }

            return {
                hasCaption: true,
                sourceQuote: quoteSnippet,
                question: 'From live lecture: "' + quoteSnippet + '" — Which key point was just discussed?',
                options: options,
                correctIndex: correctIndex
            };
        } else {
            const options = [
                'Yes, I am actively following "' + sessionTitle + '"',
                'I just stepped in / reconnected',
                'Reviewing the previous slide'
            ];
            return {
                hasCaption: false,
                sourceQuote: '',
                question: 'Attendance Check: Confirm your active participation in "' + sessionTitle + '".',
                options: options,
                correctIndex: 0
            };
        }
    }

    return {
        isSupported: isSupported,
        init: init,
        startBroadcasting: startBroadcasting,
        stopBroadcasting: stopBroadcasting,
        toggleBroadcasting: toggleBroadcasting,
        toggleCaptions: toggleCaptions,
        onCaptionReceived: onCaptionReceived,
        recordTranscript: recordTranscript,
        getRecentTranscript: getRecentTranscript,
        generateEngagementQuiz: generateEngagementQuiz,
        isBroadcasting: function () { return isBroadcasting; },
        isCaptionsEnabled: function () { return isCaptionsEnabled; }
    };
})();
