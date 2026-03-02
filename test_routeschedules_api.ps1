# Test RouteSchedules API with proper authentication
$baseUrl = "http://localhost:5000"

Write-Host "=== Testing RouteSchedules API ===" -ForegroundColor Cyan

# Step 1: Login to get JWT token
Write-Host "`n1. Logging in as admin..." -ForegroundColor Yellow
$loginBody = @{
    username = "admin"
    password = "admin"
} | ConvertTo-Json

try {
    $loginResponse = Invoke-RestMethod -Uri "$baseUrl/api/Auth/login" -Method Post -Body $loginBody -ContentType "application/json"
    $token = $loginResponse.data.token
    
    if (-not $token) {
        Write-Host "✗ Login failed: No token in response" -ForegroundColor Red
        Write-Host "Response: $($loginResponse | ConvertTo-Json -Depth 5)" -ForegroundColor Red
        exit 1
    }
    
    Write-Host "✓ Login successful! Token received." -ForegroundColor Green
    $tokenPreview = if ($token.Length -gt 50) { $token.Substring(0, 50) + "..." } else { $token }
    Write-Host "Token (first 50 chars): $tokenPreview" -ForegroundColor Gray
} catch {
    Write-Host "✗ Login failed: $_" -ForegroundColor Red
    Write-Host "Error details: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

# Step 2: Get RouteSchedules with pagination
Write-Host "`n2. Fetching RouteSchedules (first page, 10 items)..." -ForegroundColor Yellow
$headers = @{
    "Authorization" = "Bearer $token"
}

try {
    $schedulesResponse = Invoke-RestMethod -Uri "$baseUrl/api/RouteSchedules?page=1&pageSize=10" -Method Get -Headers $headers
    Write-Host "✓ RouteSchedules API call successful!" -ForegroundColor Green
    
    # Check if response has $values (ReferenceHandler.Preserve format)
    if ($schedulesResponse.'$values') {
        $schedules = $schedulesResponse.'$values'
        Write-Host "Response uses ReferenceHandler.Preserve format with `$values wrapper" -ForegroundColor Cyan
    } else {
        $schedules = $schedulesResponse
    }
    
    Write-Host "`nTotal schedules returned: $($schedules.Count)" -ForegroundColor Cyan
    
    if ($schedules.Count -eq 0) {
        Write-Host "⚠ WARNING: No schedules found in database!" -ForegroundColor Yellow
    } else {
        Write-Host "`nFirst schedule details:" -ForegroundColor Cyan
        $firstSchedule = $schedules[0]
        Write-Host "  ScheduleId: $($firstSchedule.scheduleId)" -ForegroundColor White
        Write-Host "  RouteId: $($firstSchedule.routeId)" -ForegroundColor White
        Write-Host "  StartPoint: $($firstSchedule.startPoint)" -ForegroundColor White
        Write-Host "  EndPoint: $($firstSchedule.endPoint)" -ForegroundColor White
        Write-Host "  DepartureTime: $($firstSchedule.departureTime)" -ForegroundColor White
        Write-Host "  ArrivalTime: $($firstSchedule.arrivalTime)" -ForegroundColor White
        Write-Host "  Price: $($firstSchedule.price)" -ForegroundColor White
        Write-Host "  AvailableSeats: $($firstSchedule.availableSeats)" -ForegroundColor White
        Write-Host "  IsActive: $($firstSchedule.isActive)" -ForegroundColor White
        
        Write-Host "`nFull JSON of first schedule:" -ForegroundColor Cyan
        $firstSchedule | ConvertTo-Json -Depth 10
    }
    
} catch {
    Write-Host "✗ RouteSchedules API call failed: $_" -ForegroundColor Red
    Write-Host "Error details: $($_.Exception.Message)" -ForegroundColor Red
    if ($_.Exception.Response) {
        $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
        $responseBody = $reader.ReadToEnd()
        Write-Host "Response body: $responseBody" -ForegroundColor Red
    }
    exit 1
}

# Step 3: Get Routes to test search
Write-Host "`n3. Fetching Routes for search test..." -ForegroundColor Yellow
try {
    $routesResponse = Invoke-RestMethod -Uri "$baseUrl/api/Routes" -Method Get -Headers $headers
    
    if ($routesResponse.'$values') {
        $routes = $routesResponse.'$values'
    } else {
        $routes = $routesResponse
    }
    
    Write-Host "✓ Found $($routes.Count) routes" -ForegroundColor Green
    
    if ($routes.Count -gt 0) {
        $firstRoute = $routes[0]
        Write-Host "`n4. Testing RouteSchedules search with RouteId=$($firstRoute.routeId)..." -ForegroundColor Yellow
        
        $searchUrl = "$baseUrl/api/RouteSchedules/search?routeId=$($firstRoute.routeId)"
        $searchResponse = Invoke-RestMethod -Uri $searchUrl -Method Get -Headers $headers
        
        if ($searchResponse.'$values') {
            $searchSchedules = $searchResponse.'$values'
        } else {
            $searchSchedules = $searchResponse
        }
        
        Write-Host "✓ Search returned $($searchSchedules.Count) schedules for route $($firstRoute.routeId)" -ForegroundColor Green
    }
    
} catch {
    Write-Host "✗ Routes/Search test failed: $_" -ForegroundColor Red
}

Write-Host "`n=== Test Complete ===" -ForegroundColor Cyan
