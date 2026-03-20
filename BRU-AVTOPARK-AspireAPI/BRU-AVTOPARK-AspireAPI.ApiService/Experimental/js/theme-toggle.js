// Theme Toggle Functionality - Modern implementation
// Handles dark/light mode switching with smooth transitions and localStorage persistence

(function() {
    'use strict';
    
    const themeToggleBtn = document.getElementById('themeToggle');
    if (!themeToggleBtn) {
        console.warn('Theme toggle button not found');
        return;
    }
    
    const prefersDarkScheme = window.matchMedia('(prefers-color-scheme: dark)');
    const root = document.documentElement;
    
    // Update meta theme-color for mobile browsers
    function updateThemeColor(isDark) {
        const themeColor = document.querySelector('meta[name="theme-color"]');
        if (themeColor) {
            themeColor.setAttribute('content', isDark ? '#121212' : '#F0F0F0');
        }
        // Also update html background so overscroll never flashes wrong color
        document.documentElement.style.backgroundColor = isDark ? '#121212' : '#F0F0F0';
    }
    
    // Apply theme with smooth transition
    function applyTheme(theme, animate = true) {
        const isDark = theme === 'dark';
        
        if (animate) {
            root.style.transition = 'background-color 0.3s ease, color 0.3s ease';
        }
        
        if (isDark) {
            root.setAttribute('data-theme', 'dark');
        } else {
            root.removeAttribute('data-theme');
        }
        
        updateThemeColor(isDark);
        
        if (animate) {
            setTimeout(() => {
                root.style.transition = '';
            }, 300);
        }
    }
    
    // Check for saved theme preference or use the system preference
    const savedTheme = localStorage.getItem('theme');
    const currentTheme = savedTheme || (prefersDarkScheme.matches ? 'dark' : 'light');
    
    // Set initial theme without animation
    applyTheme(currentTheme, false);
    
    // Toggle theme when the button is clicked
    themeToggleBtn.addEventListener('click', function() {
        const isDark = root.hasAttribute('data-theme');
        const newTheme = isDark ? 'light' : 'dark';
        
        // Add press animation
        this.style.transform = 'scale(0.9)';
        setTimeout(() => {
            this.style.transform = '';
        }, 100);
        
        applyTheme(newTheme);
        localStorage.setItem('theme', newTheme);
    });
    
    // Listen for system theme changes
    prefersDarkScheme.addEventListener('change', function(e) {
        // Only auto-switch if user hasn't set a preference
        if (!localStorage.getItem('theme')) {
            applyTheme(e.matches ? 'dark' : 'light');
        }
    });
    
    // Handle visibility change (sync across tabs)
    document.addEventListener('visibilitychange', function() {
        if (!document.hidden) {
            const storedTheme = localStorage.getItem('theme');
            if (storedTheme) {
                const currentIsDark = root.hasAttribute('data-theme');
                const storedIsDark = storedTheme === 'dark';
                if (currentIsDark !== storedIsDark) {
                    applyTheme(storedTheme, false);
                }
            }
        }
    });
})();
