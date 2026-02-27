# Test script for registering an OpenID Connect client
# This script first logs in to get a valid JWT token, then uses it to register a client

$ErrorActionPreference = "Stop"

# Configuration
$baseUrl = "https://localhost:5001"
$loginUrl = "$baseUrl/api/auth/login"
$registerClientUrl = "$baseUrl/api/auth/connect/registerclient"

# Login credentials
$loginPayload = @{
    username = "admin"
    password = "admin123"
    skipTwoFactor = $false
} | ConvertTo-Json

Write-Host "Step 1: Logging in to get JWT token..." -ForegroundColor Cyan

try {
    $loginResponse = Invoke-RestMethod -Uri $loginUrl `
        -Method Post `
        -Body $loginPayload `
        -ContentType "application/json" `
        -SkipCertificateCheck
    
    if ($loginResponse.success -and $loginResponse.data.token) {
        $jwtToken = $loginResponse.data.token
        Write-Host "✓ Login successful!" -ForegroundColor Green
        Write-Host "Token: $($jwtToken.Substring(0, 50))..." -ForegroundColor Gray
    } else {
        Write-Host "✗ Login failed: $($loginResponse.message)" -ForegroundColor Red
        exit 1
    }
} catch {
    Write-Host "✗ Login request failed: $_" -ForegroundColor Red
    Write-Host "Response: $($_.Exception.Response)" -ForegroundColor Red
    exit 1
}

Write-Host "`nStep 2: Registering OpenID Connect client..." -ForegroundColor Cyan

# Client registration payload
$clientPayload = @{
    clientId = "avalonia-desktop-client"
    clientSecret = "your-secure-client-secret-here-change-in-production"
    displayName = "Avalonia Desktop Application"
    redirectUris = @(
        "http://localhost:5000/callback",
        "https://localhost:5001/callback"
    )
    postLogoutRedirectUris = @(
        "http://localhost:5000",
        "https://localhost:5001"
    )
    allowedScopes = @(
        "openid",
        "profile",
        "email",
        "offline_access",
        "api"
    )
    requireConsent = $false
} | ConvertTo-Json

Write-Host "Payload:" -ForegroundColor Gray
Write-Host $clientPayload -ForegroundColor Gray

try {
    # Create headers with Bearer token
    $headers = @{
        "Authorization" = "Bearer $jwtToken"
        "Content-Type" = "application/json"
    }
    
    Write-Host "`nSending request with Authorization header..." -ForegroundColor Yellow
    
    $registerResponse = Invoke-RestMethod -Uri $registerClientUrl `
        -Method Post `
        -Headers $headers `
        -Body $clientPayload `
        -SkipCertificateCheck
    
    Write-Host "✓ Client registered successfully!" -ForegroundColor Green
    Write-Host "`nResponse:" -ForegroundColor Cyan
    $registerResponse | ConvertTo-Json -Depth 10 | Write-Host
    
} catch {
    Write-Host "✗ Client registration failed!" -ForegroundColor Red
    Write-Host "Status Code: $($_.Exception.Response.StatusCode.value__)" -ForegroundColor Red
    Write-Host "Status Description: $($_.Exception.Response.StatusDescription)" -ForegroundColor Red
    
    if ($_.ErrorDetails.Message) {
        Write-Host "`nError Details:" -ForegroundColor Yellow
        try {
            $errorJson = $_.ErrorDetails.Message | ConvertFrom-Json
            $errorJson | ConvertTo-Json -Depth 10 | Write-Host
        } catch {
            Write-Host $_.ErrorDetails.Message
        }
    }
    
    Write-Host "`nFull Exception:" -ForegroundColor Yellow
    Write-Host $_.Exception.Message
    
    exit 1
}

Write-Host "`n✓ All steps completed successfully!" -ForegroundColor Green
