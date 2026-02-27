# Test JWT token validity
$baseUrl = "https://localhost:5001"
$loginUrl = "$baseUrl/api/auth/login"

Write-Host "Testing JWT Token..." -ForegroundColor Cyan

# Login
$loginPayload = @{
    username = "admin"
    password = "admin"
} | ConvertTo-Json

$loginResult = Invoke-WebRequest -Uri $loginUrl `
    -Method Post `
    -ContentType "application/json" `
    -Body $loginPayload `
    -SkipCertificateCheck

$loginResponse = $loginResult.Content | ConvertFrom-Json
$token = $loginResponse.data.token

Write-Host "Token received: $($token.Substring(0, 50))..." -ForegroundColor Green

# Decode JWT to see claims
$tokenParts = $token.Split('.')
$payload = $tokenParts[1]

# Add padding if needed
while ($payload.Length % 4 -ne 0) {
    $payload += "="
}

$payloadBytes = [Convert]::FromBase64String($payload)
$payloadJson = [System.Text.Encoding]::UTF8.GetString($payloadBytes)
$claims = $payloadJson | ConvertFrom-Json

Write-Host "`nToken Claims:" -ForegroundColor Yellow
$claims | ConvertTo-Json -Depth 5 | Write-Host

# Test with an endpoint that uses JWT Bearer only
Write-Host "`nTesting with ApiAccess policy endpoint..." -ForegroundColor Cyan
try {
    $headers = @{
        "Authorization" = "Bearer $token"
    }
    
    $result = Invoke-WebRequest -Uri "$baseUrl/api/users" `
        -Method Get `
        -Headers $headers `
        -SkipCertificateCheck
    
    Write-Host "✓ JWT Bearer authentication works!" -ForegroundColor Green
}
catch {
    Write-Host "✗ JWT Bearer authentication failed: $($_.Exception.Message)" -ForegroundColor Red
}
