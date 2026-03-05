// Login Form Functionality - Extracted from AuthController RenderLoginForm
// Handles form submission, auto-login, and token management

(function() {
    'use strict';
    
    // Check if user is already logged in with a valid token
    function checkExistingAuth() {
        const token = localStorage.getItem('auth_token');
        if (token && token !== 'null' && token !== '') {
            // Show loading overlay with fade-in animation
            const overlay = document.getElementById('autoLoginOverlay');
            if (!overlay) return;
            
            overlay.style.display = 'flex';
            overlay.style.opacity = '0';
            
            // Fade in the overlay
            setTimeout(() => {
                overlay.style.transition = 'opacity 0.3s ease-in-out';
                overlay.style.opacity = '1';
            }, 10);
            
            // Try to validate the token by accessing profile
            fetch('/api/auth/profile?token=' + encodeURIComponent(token))
                .then(response => {
                    if (response.ok) {
                        // Token is valid, wait a bit for smooth transition then redirect
                        setTimeout(() => {
                            window.location.href = '/api/auth/profile?token=' + encodeURIComponent(token);
                        }, 800);
                    } else {
                        // Token is invalid, remove it and hide overlay
                        localStorage.removeItem('auth_token');
                        overlay.style.opacity = '0';
                        setTimeout(() => {
                            overlay.style.display = 'none';
                        }, 300);
                    }
                })
                .catch(error => {
                    console.error('Error validating token:', error);
                    // On error, remove the token and hide overlay
                    localStorage.removeItem('auth_token');
                    overlay.style.opacity = '0';
                    setTimeout(() => {
                        overlay.style.display = 'none';
                    }, 300);
                });
        }
    }
    
    // Submit login form
    window.submitLoginForm = function() {
        const loginButton = document.getElementById('loginButton');
        const statusDiv = document.getElementById('statusDiv');
        const usernameInput = document.getElementById('username');
        const passwordInput = document.getElementById('password');
        
        if (!loginButton || !statusDiv || !usernameInput || !passwordInput) {
            console.error('Required form elements not found');
            return false;
        }
        
        // Disable button to prevent multiple submissions
        loginButton.disabled = true;
        statusDiv.innerHTML = '<div class="text-center"><div class="loader"></div><p>Logging in...</p></div>';
        
        // Get form data
        const username = usernameInput.value;
        const password = passwordInput.value;
        
        // Submit form using fetch with JSON
        fetch('/api/auth/login', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
            },
            body: JSON.stringify({
                username: username,
                password: password
            }),
            credentials: 'same-origin',
            redirect: 'manual'
        })
        .then(response => {
            // Check if it's a redirect (status 301, 302, 303, 307, 308)
            if (response.type === 'opaqueredirect' || response.status === 0 || (response.status >= 300 && response.status < 400)) {
                // For redirects, just follow the redirect manually
                const location = response.headers.get('Location') || '/api/auth/success';
                window.location.href = location;
                return null;
            }
            
            // Check if response is HTML (redirect page)
            const contentType = response.headers.get('content-type');
            if (contentType && contentType.includes('text/html')) {
                // It's an HTML redirect, follow it
                window.location.href = response.url || '/api/auth/success';
                return null;
            }
            
            // Otherwise parse as JSON
            return response.json();
        })
        .then(data => {
            if (!data) {
                // Redirect is being handled, do nothing
                return;
            }
            
            if (data && data.success) {
                // Store token if present
                if (data.data && data.data.token) {
                    localStorage.setItem('auth_token', data.data.token);
                }
                
                // Show success message
                statusDiv.innerHTML = '<p class="success-message">Login successful! Redirecting...</p>';
                
                // Redirect to profile or success page
                setTimeout(() => {
                    window.location.href = '/api/auth/profile';
                }, 1000);
            } else if (data) {
                // Show error message
                statusDiv.innerHTML = `<p class="error-message">${data.message || 'Login failed'}</p>`;
                loginButton.disabled = false;
            }
        })
        .catch(error => {
            console.error('Login error:', error);
            statusDiv.innerHTML = `<p class="error-message">Error: ${error.message || 'Unknown error'}</p>`;
            loginButton.disabled = false;
        });
        
        return false;
    };
    
    // Initialize on page load
    document.addEventListener('DOMContentLoaded', function() {
        // Check for existing authentication
        checkExistingAuth();
        
        // Allow form submission with Enter key
        const loginForm = document.getElementById('loginForm');
        if (loginForm) {
            loginForm.addEventListener('keypress', function(event) {
                if (event.key === 'Enter') {
                    event.preventDefault();
                    submitLoginForm();
                }
            });
        }
    });
})();
