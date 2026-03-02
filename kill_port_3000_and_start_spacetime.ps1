# Kill process using port 3000 and start SpacetimeDB
Write-Host "Checking port 3000..." -ForegroundColor Cyan

$processes = Get-NetTCPConnection -LocalPort 3000 -ErrorAction SilentlyContinue | 
    Select-Object -ExpandProperty OwningProcess -Unique

if ($processes) {
    foreach ($processId in $processes) {
        $proc = Get-Process -Id $processId -ErrorAction SilentlyContinue
        if ($proc) {
            Write-Host "Killing process: $($proc.ProcessName) (PID: $processId)" -ForegroundColor Yellow
            Stop-Process -Id $processId -Force
            Start-Sleep -Seconds 2
        }
    }
    Write-Host "✓ Port 3000 cleared" -ForegroundColor Green
} else {
    Write-Host "✓ Port 3000 is free" -ForegroundColor Green
}

 