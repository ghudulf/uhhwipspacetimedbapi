// WebAuthn Registration - Extracted from AuthController RenderWebAuthnRegistration
// Handles security key and biometric registration

// Helper function to convert ArrayBuffer to Base64 string
function arrayBufferToBase64(buffer) {
    const bytes = new Uint8Array(buffer);
    let binary = '';
    for (let i = 0; i < bytes.byteLength; i++) {
        binary += String.fromCharCode(bytes[i]);
    }
    return btoa(binary);
}

async function registerWebAuthn() {
    try {
        const registerButton = document.getElementById('registerButton');
        const loader = document.getElementById('loader');
        const status = document.getElementById('status');
        const optionsElement = document.getElementById('options');
        
        if (!registerButton || !loader || !status || !optionsElement) {
            console.error('Required elements not found');
            return;
        }
        
        registerButton.disabled = true;
        loader.classList.remove('hidden');
        status.innerHTML = '<p>Please follow your browser\'s instructions to register your security key...</p>';
        
        const options = JSON.parse(optionsElement.dataset.options);
        const credential = await navigator.credentials.create({
            publicKey: options.publicKey
        });
        
        // Prepare the credential response for the server
        const credentialResponse = {
            id: credential.id,
            rawId: arrayBufferToBase64(credential.rawId),
            type: credential.type,
            response: {
                attestationObject: arrayBufferToBase64(credential.response.attestationObject),
                clientDataJSON: arrayBufferToBase64(credential.response.clientDataJSON)
            }
        };
        
        // Send the credential to the server
        const response = await fetch('/api/auth/webauthn/register/complete', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ attestationResponse: credentialResponse })
        });
        
        if (response.ok) {
            status.innerHTML = '<p class="success-message">Security key registered successfully!</p>';
            setTimeout(() => {
                window.location.href = '/api/auth/profile';
            }, 1500);
        } else {
            const error = await response.json();
            throw new Error(error.message || 'Registration failed');
        }
    } catch (error) {
        console.error('WebAuthn registration failed:', error);
        const status = document.getElementById('status');
        if (status) {
            status.innerHTML = `<p class="error-message">Failed to register security key: ${error.message || error}</p>`;
        }
    } finally {
        const registerButton = document.getElementById('registerButton');
        const loader = document.getElementById('loader');
        if (registerButton) registerButton.disabled = false;
        if (loader) loader.classList.add('hidden');
    }
}
