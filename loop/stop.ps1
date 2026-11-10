# Stop the unattended loop.
#   powershell -File loop\stop.ps1        finish the current phase, then stop cleanly (recommended)
#   powershell -File loop\stop.ps1 -Now   kill the current session immediately; its phase restarts next time
param([switch]$Now)

$runtime = Join-Path $PSScriptRoot "runtime"
New-Item -ItemType Directory -Force -Path $runtime | Out-Null
Set-Content -LiteralPath (Join-Path $runtime "STOP") -Value (Get-Date -Format s) -Encoding utf8
Write-Host "Stop requested. The loop stops after the current phase (it checks between phases)."

if ($Now) {
    $pidFile = Join-Path $runtime "current.pid"
    if (Test-Path $pidFile) {
        $childPid = Get-Content $pidFile
        taskkill /PID $childPid /T /F | Out-Null
        Write-Host "Killed the running session (pid $childPid)."
    }
    Get-CimInstance Win32_Process -Filter "Name = 'python.exe'" |
        Where-Object { $_.CommandLine -match 'loop[\\/]loop\.py' } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force; Write-Host "Stopped the orchestrator (pid $($_.ProcessId))." }
    Write-Host "Any half-finished work is committed on its loop/<story> branch, or will be on the next start."
}
