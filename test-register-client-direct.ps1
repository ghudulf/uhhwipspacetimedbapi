# Direct test by checking if the client was registered
# After you submit via the web form

$apiUrl = "https://localhost:5001/api/auth/connect/clients"

Write-Host "Checking registered OpenID Connect clients..." -ForegroundColor Cyan
Write-Host ""

try {
    # Get the login page first to get a token
    $loginPageUrl = "https://localhost:5001/api/auth/login"
    $loginPage = Invoke-WebRequest -Uri $loginPageUrl -Method Get -SkipCertificateCheck -SessionVariable session
    
    # Extract token from the page if present (you'll need to login first)
    Write-Host "Please login first at: $loginPageUrl" -ForegroundColor Yellow
    Write-Host "Then check clients at: $apiUrl" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Expected client ID: bru-avtopark-desktop-client" -ForegroundColor Cyan
}
catch {
    Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Red
}
