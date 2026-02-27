# Delete OpenID Connect Client Script
# This script deletes an existing OpenID Connect client

$ErrorActionPreference = "Stop"

# Configuration
$baseUrl = "https://localhost:5001"
$clientId = "bru-avtopark-desktop-client"

# Login credentials
$loginPayload = @{
    username = "admin"
    password = "admin123"
    skipTwoFactor = $false
} | ConvertTo-Json

Write-Host "Step 1: Logging in to get JWT token..." -ForegroundColor Cyan
try {
    $loginResponse = Invoke-RestMethod -Uri "$baseUrl/api/auth/login" `
        -Method Post `
        -ContentType "application/json" `
        -Body $loginPayload `
        -SkipCertificateCheck
    
    $token = $loginResponse.data.token
    Write-Host "✓ Login successful!" -ForegroundColor Green
    Write-Host "Token: $($token.Substring(0, 50))..." -ForegroundColor Gray
    Write-Host ""
} catch {
    Write-Host "✗ Login failed!" -ForegroundColor Red
    Write-Host "Error: $_" -ForegroundColor Red
    exit 1
}

# Delete the client
Write-Host "Step 2: Deleting OpenID Connect client '$clientId'..." -ForegroundColor Cyan
try {
    $deleteResponse = Invoke-RestMethod -Uri "$baseUrl/api/auth/connect/delete-client/$clientId" `
        -Method Delete `
        -Headers @{
            "Authorization" = "Bearer $token"
            "Content-Type" = "application/json"
        } `
        -SkipCertificateCheck
    
    Write-Host "✓ Client deleted successfully!" -ForegroundColor Green
    Write-Host "Response:" -ForegroundColor Gray
    $deleteResponse | ConvertTo-Json -Depth 10
    Write-Host ""
} catch {
    Write-Host "✗ Client deletion failed!" -ForegroundColor Red
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
