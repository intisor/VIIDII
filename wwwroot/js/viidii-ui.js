// Client-side helpers for the roster upload screen.
window.viidii = window.viidii || {};

/**
 * Triggers a browser download for generated text (join codes, CSV exports).
 *
 * Kept separate from the PeerJS session interop so this file can be loaded on
 * pages that never touch WebRTC.
 */
window.viidii.downloadTextFile = function (fileName, content, mimeType) {
    const blob = new Blob([content], { type: (mimeType || 'text/plain') + ';charset=utf-8' });
    const url = URL.createObjectURL(blob);

    const link = document.createElement('a');
    link.href = url;
    link.download = fileName;
    link.style.display = 'none';

    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);

    // Revoke on the next tick; revoking synchronously can cancel the download
    // in some browsers.
    setTimeout(function () { URL.revokeObjectURL(url); }, 1000);
};

// Exposed as a global because Blazor's JS interop resolves by name.
window.downloadTextFile = window.viidii.downloadTextFile;