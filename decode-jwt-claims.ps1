# Decode JWT token claims to see what's in there
$ErrorActionPreference = "Stop"

$baseUrl = "https://localhost:5001"
$loginUrl = "$baseUrl/api/auth/login"

$loginPayload = @{
    username = "admin"
    password = "admin123"
    skipTwoFactor = $false
} | ConvertTo-Json

Write-Host "Logging in to get JWT token..." -ForegroundColor Cyan

try {
    $loginResponse = Invoke-RestMethod -Uri $loginUrl `
        -Method Post `
        -Body $loginPayload `
        -ContentType "application/json" `
        -SkipCertificateCheck
    
    if ($loginResponse.success -and $loginResponse.data.token) {
        $jwtToken = $loginResponse.data.token
        Write-Host "✓ Login successful!" -ForegroundColor Green
        
        # Decode JWT payload (second part between dots)
        $parts = $jwtToken.Split('.')
        $payload = $parts[1]
        
        # Add padding if needed
        while ($payload.Length % 4 -ne 0) {
            $payload += "="
        }
        
        # Decode from base64url
        $payload = $payload.Replace('-', '+').Replace('_', '/')
        $payloadBytes = [Convert]::FromBase64String($payload)
        $payloadJson = [System.Text.Encoding]::UTF8.GetString($payloadBytes)
        
        Write-Host "`nJWT Claims:" -ForegroundColor Cyan
        $claims = $payloadJson | ConvertFrom-Json
        $claims | ConvertTo-Json -Depth 10 | Write-Host
        
    } else {
        Write-Host "✗ Login failed: $($loginResponse.message)" -ForegroundColor Red
        exit 1
    }
} catch {
    Write-Host "✗ Request failed: $_" -ForegroundColor Red
    exit 1
}
