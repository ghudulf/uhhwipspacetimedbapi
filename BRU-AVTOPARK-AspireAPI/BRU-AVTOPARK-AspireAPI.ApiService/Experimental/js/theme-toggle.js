// Theme Toggle Functionality - Extracted from AuthController BaseHtmlTemplate
// Handles dark/light mode switching with localStorage persistence

(function() {
    'use strict';
    
    const themeToggleBtn = document.getElementById('themeToggle');
    if (!themeToggleBtn) {
        console.warn('Theme toggle button not found');
        return;
    }
    
    const prefersDarkScheme = window.matchMedia('(prefers-color-scheme: dark)');
    
    // Check for saved theme preference or use the system preference
    const currentTheme = localStorage.getItem('theme') || (prefersDarkScheme.matches ? 'dark' : 'light');
    
    // Set initial theme
    if (currentTheme === 'dark') {
        document.body.setAttribute('data-theme', 'dark');
        themeToggleBtn.textContent = '☀️';
    } else {
        document.body.removeAttribute('data-theme');
        themeToggleBtn.textContent = '🌙';
    }
    
    // Toggle theme when the button is clicked
    themeToggleBtn.addEventListener('click', function() {
        let theme = 'light';
        
        if (!document.body.hasAttribute('data-theme')) {
            document.body.setAttribute('data-theme', 'dark');
            themeToggleBtn.textContent = '☀️';
            theme = 'dark';
        } else {
            document.body.removeAttribute('data-theme');
            themeToggleBtn.textContent = '🌙';
        }
        
        localStorage.setItem('theme', theme);
    });
    
    // Listen for system theme changes
    prefersDarkScheme.addEventListener('change', function(e) {
        if (!localStorage.getItem('theme')) {
            if (e.matches) {
                document.body.setAttribute('data-theme', 'dark');
                themeToggleBtn.textContent = '☀️';
            } else {
                document.body.removeAttribute('data-theme');
                themeToggleBtn.textContent = '🌙';
            }
        }
    });
})();
