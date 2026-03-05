// QR Login Polling - Extracted from AuthController RenderQrLogin
// Handles QR code login status checking and auto-redirect

(function() {
    'use strict';
    
    function checkLoginStatus(deviceId) {
        fetch(`/api/auth/qr/direct/check?deviceId=${deviceId}`)
            .then(response => response.json())
            .then(data => {
                const statusDiv = document.getElementById('status');
                if (!statusDiv) return;
                
                if (data.success && data.data && data.data.token) {
                    statusDiv.innerHTML = '<p class="success-message">Login successful! Redirecting...</p>';
                    // Store token in localStorage
                    localStorage.setItem('auth_token', data.data.token);
                    setTimeout(() => {
                        window.location.href = `/api/auth/success?token=${data.data.token}`;
                    }, 1000);
                } else {
                    // Continue polling
                    setTimeout(() => checkLoginStatus(deviceId), 2000);
                }
            })
            .catch(error => {
                console.error('Error checking login status:', error);
                // Continue polling even on error
                setTimeout(() => checkLoginStatus(deviceId), 2000);
            });
    }
    
    // Extract device ID from QR code data or generate one
    function getDeviceId() {
        // Try to extract from URL or generate a new one
        const urlParams = new URLSearchParams(window.location.search);
        return urlParams.get('deviceId') || generateDeviceId();
    }
    
    function generateDeviceId() {
        return 'device_' + Math.random().toString(36).substring(2, 15) + Math.random().toString(36).substring(2, 15);
    }
    
    // Start polling when page loads
    document.addEventListener('DOMContentLoaded', function() {
        const deviceId = getDeviceId();
        checkLoginStatus(deviceId);
    });
})();
