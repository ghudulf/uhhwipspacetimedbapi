# Test script for registering an OpenID Connect client with a new client ID
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
        Write-Host ""
    } else {
        Write-Host "✗ Login failed - no token in response" -ForegroundColor Red
        Write-Host "Response: $($loginResponse | ConvertTo-Json)" -ForegroundColor Red
        exit 1
    }
} catch {
    Write-Host "✗ Login failed!" -ForegroundColor Red
    Write-Host "Error: $_" -ForegroundColor Red
    exit 1
}

# Client registration payload
$clientPayload = @{
    clientId = "test-client-$(Get-Date -Format 'yyyyMMddHHmmss')"
    clientSecret = "test-secret-change-in-production"
    displayName = "Test Client Application"
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
}

Write-Host "Step 2: Registering OpenID Connect client..." -ForegroundColor Cyan
Write-Host "Payload:" -ForegroundColor Gray
$clientPayload | ConvertTo-Json -Depth 10
Write-Host ""

try {
    Write-Host "Sending request with Authorization header..." -ForegroundColor Gray
    $registerResponse = Invoke-RestMethod -Uri $registerClientUrl `
        -Method Post `
        -Headers @{
            "Authorization" = "Bearer $jwtToken"
            "Content-Type" = "application/json"
        } `
        -Body ($clientPayload | ConvertTo-Json -Depth 10) `
        -SkipCertificateCheck
    
    Write-Host "✓ Client registered successfully!" -ForegroundColor Green
    Write-Host "Response:" -ForegroundColor Gray
    $registerResponse | ConvertTo-Json -Depth 10
    Write-Host ""
} catch {
    Write-Host "✗ Client registration failed!" -ForegroundColor Red
    Write-Host "Status Code: $($_.Exception.Response.StatusCode.value__)" -ForegroundColor Red
    Write-Host "Status Description: $($_.Exception.Response.StatusDescription)" -ForegroundColor Red
    
    if ($_.ErrorDetails.Message) {
        Write-Host "`nError Details:" -ForegroundColor Yellow
        $_.ErrorDetails.Message | ConvertFrom-Json | ConvertTo-Json -Depth 10
    }
    
    Write-Host "`nFull Exception:" -ForegroundColor Yellow
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}

Write-Host "✓ All steps completed successfully!" -ForegroundColor Green
