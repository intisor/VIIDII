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
    },
    
    applyTheme: (theme) => {
        const root = document.documentElement;
        if (theme === 'dark') {
            root.classList.add('dark');
        } else {
            root.classList.remove('dark');
        }
        localStorage.setItem('viidii-theme', theme);
    },
    
    getTheme: () => {
        return localStorage.getItem('viidii-theme');
    },
    
    initializeTheme: () => {
        const savedTheme = localStorage.getItem('viidii-theme');
        if (savedTheme) {
            window.blazorTheme.applyTheme(savedTheme);
            return savedTheme;
        } else {
            const prefersDark = window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches;
            const theme = prefersDark ? 'dark' : 'light';
            window.blazorTheme.applyTheme(theme);
            return theme;
        }
    }
};

// Initialize theme immediately on page load to prevent flash
(function() {
    window.blazorTheme.initializeTheme();
})();