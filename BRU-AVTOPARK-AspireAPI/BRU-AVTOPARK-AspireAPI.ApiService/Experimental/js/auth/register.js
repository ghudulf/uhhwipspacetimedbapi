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
        const statusDiv = document.getElementById('adminStatus');
        if (!statusDiv) return;
        
        if (isAdmin) {
            statusDiv.innerHTML = `<div class="success-message">${message}</div>`;
        } else {
            statusDiv.innerHTML = `<div class="error-message">${message}</div>`;
        }
    }
    
    function enableRegistrationForm() {
        const form = document.getElementById('registerForm');
        if (form) {
            const inputs = form.querySelectorAll('input, select, button');
            inputs.forEach(input => input.disabled = false);
        }
    }
    
    function disableRegistrationForm() {
        const form = document.getElementById('registerForm');
        if (form) {
            const inputs = form.querySelectorAll('input, select, button');
            inputs.forEach(input => input.disabled = true);
        }
    }
    
    // Submit registration form
    window.submitRegisterForm = function() {
        const form = document.getElementById('registerForm');
        const statusDiv = document.getElementById('registerStatus');
        
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
        
        statusDiv.innerHTML = '<div class="text-center"><div class="loader"></div><p>Registering user...</p></div>';
        
        fetch('/api/auth/register', {
            method: 'POST',
            headers: headers,
            body: JSON.stringify(data)
        })
        .then(response => response.json())
        .then(result => {
            if (result.success) {
                statusDiv.innerHTML = '<div class="success-message">User registered successfully!</div>';
                setTimeout(() => {
                    form.reset();
                    statusDiv.innerHTML = '';
                }, 3000);
            } else {
                statusDiv.innerHTML = `<div class="error-message">${result.message || 'Registration failed'}</div>`;
            }
        })
        .catch(error => {
            console.error('Registration error:', error);
            statusDiv.innerHTML = `<div class="error-message">Error: ${error.message || 'Unknown error'}</div>`;
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
