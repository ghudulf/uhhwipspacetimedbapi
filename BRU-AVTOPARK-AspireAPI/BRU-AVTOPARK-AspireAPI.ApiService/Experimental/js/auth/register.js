// Registration Form with Admin Validation - Extracted from AuthController RenderRegisterForm
// Handles admin token validation with 3-attempt retry logic

(function() {
    'use strict';
    
    let validationAttempts = 0;
    const MAX_ATTEMPTS = 3;
    
    // Check admin status from JWT token
    function checkAdminStatus() {
        const authHeader = getAuthorizationHeader();
        
        if (!authHeader) {
            showAdminStatus(false, 'No authorization token found. Admin privileges required.');
            return;
        }
        
        const token = extractToken(authHeader);
        if (!token) {
            showAdminStatus(false, 'Invalid authorization header format.');
            return;
        }
        
        try {
            const payload = parseJwt(token);
            const isAdmin = checkAdminRole(payload);
            
            if (isAdmin) {
                showAdminStatus(true, 'Admin privileges verified.');
                enableRegistrationForm();
            } else {
                showAdminStatus(false, 'Admin privileges required for user registration.');
                disableRegistrationForm();
            }
        } catch (error) {
            console.error('Error parsing JWT:', error);
            showAdminStatus(false, 'Error validating admin status.');
            
            // Auto-retry logic
            if (validationAttempts < MAX_ATTEMPTS) {
                validationAttempts++;
                setTimeout(() => {
                    console.log(`Retrying admin validation (attempt ${validationAttempts}/${MAX_ATTEMPTS})...`);
                    checkAdminStatus();
                }, 1000);
            }
        }
    }
    
    function getAuthorizationHeader() {
        // Try to get from meta tag first (if set by server)
        const metaToken = document.querySelector('meta[name="auth-token"]');
        if (metaToken) {
            return 'Bearer ' + metaToken.content;
        }
        
        // Try localStorage
        const storedToken = localStorage.getItem('auth_token');
        if (storedToken) {
            return 'Bearer ' + storedToken;
        }
        
        return null;
    }
    
    function extractToken(authHeader) {
        if (authHeader.startsWith('Bearer ')) {
            return authHeader.substring(7).trim();
        }
        return null;
    }
    
    function parseJwt(token) {
        const base64Url = token.split('.')[1];
        const base64 = base64Url.replace(/-/g, '+').replace(/_/g, '/');
        const jsonPayload = decodeURIComponent(atob(base64).split('').map(function(c) {
            return '%' + ('00' + c.charCodeAt(0).toString(16)).slice(-2);
        }).join(''));
        
        return JSON.parse(jsonPayload);
    }
    
    function checkAdminRole(payload) {
        // Check primary_role claim
        if (payload.primary_role === '1' || payload.primary_role === 1) {
            return true;
        }
        
        // Check role claims array
        if (payload.role) {
            if (Array.isArray(payload.role)) {
                return payload.role.includes('1') || payload.role.includes(1);
            }
            return payload.role === '1' || payload.role === 1;
        }
        
        return false;
    }
    
    function showAdminStatus(isAdmin, message) {
        const statusSuccess = document.getElementById('admin-check-status');
        const statusPending = document.getElementById('admin-check-pending');
        
        if (!statusSuccess || !statusPending) return;
        
        if (isAdmin) {
            statusSuccess.style.display = 'flex';
            statusPending.style.display = 'none';
            // Update the success message text
            const statusText = statusSuccess.querySelector('span');
            if (statusText) {
                statusText.innerHTML = `<strong>${message}</strong>`;
            }
        } else {
            statusSuccess.style.display = 'none';
            statusPending.style.display = 'flex';
            // Update the pending/error message
            const statusText = statusPending.querySelector('div > strong');
            if (statusText) {
                statusText.textContent = message;
            }
        }
    }
    
    function enableRegistrationForm() {
        const form = document.getElementById('registerForm');
        if (form) {
            form.style.display = 'block';
            const inputs = form.querySelectorAll('input, select, button');
            inputs.forEach(input => input.disabled = false);
        }
    }
    
    function disableRegistrationForm() {
        const form = document.getElementById('registerForm');
        if (form) {
            form.style.display = 'none';
            const inputs = form.querySelectorAll('input, select, button');
            inputs.forEach(input => input.disabled = true);
        }
    }
    
    // Submit registration form
    window.submitRegisterForm = function() {
        const form = document.getElementById('registerForm');
        const statusDiv = document.getElementById('register-status');
        
        if (!form || !statusDiv) {
            console.error('Required form elements not found');
            return false;
        }
        
        const formData = new FormData(form);
        const data = {
            username: formData.get('username'),
            password: formData.get('password'),
            email: formData.get('email'),
            phoneNumber: formData.get('phoneNumber'),
            role: parseInt(formData.get('role'))
        };
        
        // Get auth token for admin verification
        const authHeader = getAuthorizationHeader();
        const headers = {
            'Content-Type': 'application/json'
        };
        
        if (authHeader) {
            headers['Authorization'] = authHeader;
        }
        
        statusDiv.innerHTML = '<div class="status-message status-message--info"><div class="loader" style="width: 18px; height: 18px; margin-right: 8px;"></div><span>Registering user...</span></div>';
        
        fetch('/api/auth/register', {
            method: 'POST',
            headers: headers,
            body: JSON.stringify(data)
        })
        .then(response => response.json())
        .then(result => {
            if (result.success) {
                statusDiv.innerHTML = '<div class="status-message status-message--success"><svg class="status-message__icon" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M22 11.08V12a10 10 0 1 1-5.93-9.14"/><polyline points="22 4 12 14.01 9 11.01"/></svg><span>User registered successfully!</span></div>';
                setTimeout(() => {
                    form.reset();
                    statusDiv.innerHTML = '';
                }, 3000);
            } else {
                statusDiv.innerHTML = `<div class="status-message status-message--error"><svg class="status-message__icon" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"/><line x1="15" y1="9" x2="9" y2="15"/><line x1="9" y1="9" x2="15" y2="15"/></svg><span>${result.message || 'Registration failed'}</span></div>`;
            }
        })
        .catch(error => {
            console.error('Registration error:', error);
            statusDiv.innerHTML = `<div class="status-message status-message--error"><svg class="status-message__icon" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"/><line x1="15" y1="9" x2="9" y2="15"/><line x1="9" y1="9" x2="15" y2="15"/></svg><span>Error: ${error.message || 'Unknown error'}</span></div>`;
        });
        
        return false;
    };
    
    // Initialize on page load
    document.addEventListener('DOMContentLoaded', function() {
        checkAdminStatus();
        
        // Allow form submission with Enter key
        const form = document.getElementById('registerForm');
        if (form) {
            form.addEventListener('submit', function(event) {
                event.preventDefault();
                submitRegisterForm();
            });
        }
    });
})();
