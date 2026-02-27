# Test OpenID Connect Client Registration with proper JWT Bearer authentication
# This script logs in, gets a JWT token, and registers a client via JSON API

$baseUrl = "https://localhost:5001"
$loginUrl = "$baseUrl/api/auth/login"
$registerUrl = "$baseUrl/api/auth/connect/registerclient"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "OpenID Connect Client Registration Test" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Step 1: Login to get JWT token
Write-Host "Step 1: Authenticating..." -ForegroundColor Yellow

$loginPayload = @{
    username = "admin"
    password = "admin"
} | ConvertTo-Json

try {
    $loginResult = Invoke-WebRequest -Uri $loginUrl `
        -Method Post `
        -ContentType "application/json" `
        -Body $loginPayload `
        -SkipCertificateCheck
    
    $loginResponse = $loginResult.Content | ConvertFrom-Json
    
    # Token is nested in data.token
    $token = $loginResponse.data.token
    
    if ([string]::IsNullOrEmpty($token)) {
        Write-Host "✗ Failed to get token from login response" -ForegroundColor Red
        Write-Host "Response:" -ForegroundColor Yellow
        $loginResponse | ConvertTo-Json -Depth 5 | Write-Host
        exit 1
    }
    
    Write-Host "✓ Authentication successful!" -ForegroundColor Green
    Write-Host "  Token: $($token.Substring(0, 50))..." -ForegroundColor Gray
    Write-Host ""
}
catch {
    Write-Host "✗ Login failed!" -ForegroundColor Red
    Write-Host "  Error: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

# Step 2: Read client registration data
Write-Host "Step 2: Loading client configuration..." -ForegroundColor Yellow

$clientData = Get-Content "register_client.json" | ConvertFrom-Json

Write-Host "✓ Configuration loaded" -ForegroundColor Green
Write-Host "  Client ID: $($clientData.client_id)" -ForegroundColor Gray
Write-Host "  Display Name: $($clientData.display_name)" -ForegroundColor Gray
Write-Host "  Redirect URIs: $($clientData.redirect_uris.Count)" -ForegroundColor Gray
Write-Host "  Scopes: $($clientData.allowed_scopes.Count)" -ForegroundColor Gray
Write-Host ""

# Step 3: Prepare registration request
Write-Host "Step 3: Registering client..." -ForegroundColor Yellow

$registrationPayload = @{
    clientId = $clientData.client_id
    clientSecret = $clientData.client_secret
    displayName = $clientData.display_name
    redirectUris = $clientData.redirect_uris
    postLogoutRedirectUris = $clientData.post_logout_redirect_uris
    allowedScopes = $clientData.allowed_scopes
    requireConsent = ($clientData.consent_type -eq "explicit")
} | ConvertTo-Json -Depth 10

Write-Host "  Request URL: $registerUrl" -ForegroundColor Gray
Write-Host "  Payload:" -ForegroundColor Gray
$registrationPayload | Write-Host -ForegroundColor DarkGray
Write-Host ""

try {
    # Create headers with Bearer token
    $headers = @{
        "Authorization" = "Bearer $token"
        "Content-Type" = "application/json"
        "Accept" = "application/json"
    }
    
    Write-Host "  Sending request with Bearer token..." -ForegroundColor Gray
    
    # Use Invoke-WebRequest to get full response details
    $result = Invoke-WebRequest -Uri $registerUrl `
        -Method Post `
        -Headers $headers `
        -Body $registrationPayload `
        -SkipCertificateCheck
    
    $response = $result.Content | ConvertFrom-Json
    
    Write-Host ""
    Write-Host "✓ Client registered successfully!" -ForegroundColor Green
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "Registration Response:" -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan
    $response | ConvertTo-Json -Depth 10 | Write-Host -ForegroundColor Green
    Write-Host ""
}
catch {
    Write-Host ""
    Write-Host "✗ Client registration failed!" -ForegroundColor Red
    Write-Host ""
    
    $statusCode = $_.Exception.Response.StatusCode.value__
    Write-Host "  Status Code: $statusCode" -ForegroundColor Red
    
    if ($statusCode -eq 401) {
        Write-Host "  Issue: Unauthorized" -ForegroundColor Yellow
        Write-Host "  - Token may be invalid or expired" -ForegroundColor Yellow
        Write-Host "  - User may not have Administrator role" -ForegroundColor Yellow
        Write-Host "  - JWT Bearer authentication may not be configured for this endpoint" -ForegroundColor Yellow
    }
    elseif ($statusCode -eq 403) {
        Write-Host "  Issue: Forbidden - User does not have Administrator role" -ForegroundColor Yellow
    }
    elseif ($statusCode -eq 400) {
        Write-Host "  Issue: Bad Request - Check payload format" -ForegroundColor Yellow
    }
    
    Write-Host ""
    Write-Host "  Error Message: $($_.Exception.Message)" -ForegroundColor Red
    
    # Try to read error response body
    if ($_.Exception.Response) {
        try {
            $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
            $responseBody = $reader.ReadToEnd()
            $reader.Close()
            
            if ($responseBody) {
                Write-Host ""
                Write-Host "  Response Body:" -ForegroundColor Yellow
                try {
                    $errorObj = $responseBody | ConvertFrom-Json
                    $errorObj | ConvertTo-Json -Depth 10 | Write-Host -ForegroundColor Red
                }
                catch {
                    Write-Host $responseBody -ForegroundColor Red
                }
            }
        }
        catch {
            # Ignore errors reading response
        }
    }
    
    Write-Host ""
    exit 1
}

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Test completed successfully!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
