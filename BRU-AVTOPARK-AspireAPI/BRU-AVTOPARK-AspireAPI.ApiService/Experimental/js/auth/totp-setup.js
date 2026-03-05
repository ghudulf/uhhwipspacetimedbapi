// TOTP Setup Verification - Extracted from AuthController RenderTotpSetup
// Handles 6-digit code validation and form submission

(function() {
    'use strict';
    
    // Validate 6-digit code input
    function validateTotpCode(input) {
        const value = input.value.replace(/\D/g, ''); // Remove non-digits
        input.value = value.substring(0, 6); // Limit to 6 digits
        
        // Enable submit button only when 6 digits are entered
        const submitButton = document.querySelector('button[type="submit"]');
        if (submitButton) {
            submitButton.disabled = value.length !== 6;
        }
    }
    
    // Auto-focus on code input
    function setupCodeInput() {
        const codeInput = document.getElementById('code');
        if (codeInput) {
            codeInput.focus();
            
            // Add input validation
            codeInput.addEventListener('input', function() {
                validateTotpCode(this);
            });
            
            // Add paste handling
            codeInput.addEventListener('paste', function(e) {
                e.preventDefault();
                const pastedText = (e.clipboardData || window.clipboardData).getData('text');
                const digits = pastedText.replace(/\D/g, '').substring(0, 6);
                this.value = digits;
                validateTotpCode(this);
            });
        }
    }
    
    // Handle form submission
    function setupFormSubmission() {
        const form = document.querySelector('form');
        if (form) {
            form.addEventListener('submit', function(e) {
                const codeInput = document.getElementById('code');
                if (codeInput && codeInput.value.length !== 6) {
                    e.preventDefault();
                    alert('Please enter a 6-digit code');
                    return false;
                }
            });
        }
    }
    
    // Initialize on page load
    document.addEventListener('DOMContentLoaded', function() {
        setupCodeInput();
        setupFormSubmission();
    });
})();
