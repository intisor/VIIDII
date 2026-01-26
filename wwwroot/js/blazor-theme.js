window.matchMedia = window.matchMedia || function(media) {
    return {
        matches: false,
        addListener: function() {},
        removeListener: function() {}
    };
};

window.blazorTheme = {
    getSystemPreference: () => {
        return window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches;
    }
};