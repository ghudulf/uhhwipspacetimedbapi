# Verify JWT token has kid in header
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
        
        # Decode JWT header (first part before first dot)
        $parts = $jwtToken.Split('.')
        $header = $parts[0]
        
        # Add padding if needed
        while ($header.Length % 4 -ne 0) {
            $header += "="
        }
        
        # Decode from base64url
        $header = $header.Replace('-', '+').Replace('_', '/')
        $headerBytes = [Convert]::FromBase64String($header)
        $headerJson = [System.Text.Encoding]::UTF8.GetString($headerBytes)
        
        Write-Host "`nJWT Header:" -ForegroundColor Cyan
        $headerObj = $headerJson | ConvertFrom-Json
        $headerObj | ConvertTo-Json | Write-Host
        
        if ($headerObj.kid) {
            Write-Host "`n✓ JWT token has 'kid' in header: $($headerObj.kid)" -ForegroundColor Green
        } else {
            Write-Host "`n✗ JWT token is missing 'kid' in header!" -ForegroundColor Red
        }
        
    } else {
        Write-Host "✗ Login failed: $($loginResponse.message)" -ForegroundColor Red
        exit 1
    }
} catch {
    Write-Host "✗ Request failed: $_" -ForegroundColor Red
    exit 1
}
