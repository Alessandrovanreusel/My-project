# Start the unattended loop in its own window. Usage, from the project folder:
#   powershell -ExecutionPolicy Bypass -File loop\start.ps1            # run until done or blocked
#   powershell -ExecutionPolicy Bypass -File loop\start.ps1 -Only 1-15-record-a-clip
param([string]$Only = "", [int]$MaxUnits = 0)

$root = Split-Path -Parent $PSScriptRoot
$runtime = Join-Path $PSScriptRoot "runtime"
New-Item -ItemType Directory -Force -Path (Join-Path $runtime "logs") | Out-Null

$running = Get-CimInstance Win32_Process -Filter "Name = 'python.exe'" |
    Where-Object { $_.CommandLine -match 'loop[\\/]loop\.py' -and $_.CommandLine -notmatch '--dry-run|--smoke' }
if ($running) { Write-Host "The loop is already running (pid $($running.ProcessId -join ', '))."; exit 1 }

$loopArgs = @("loop\loop.py")
if ($Only) { $loopArgs += @("--only", $Only) }
if ($MaxUnits -gt 0) { $loopArgs += @("--max-units", "$MaxUnits") }

$cmd = "Set-Location -LiteralPath '$root'; `$host.UI.RawUI.WindowTitle = 'Game loop'; python $($loopArgs -join ' '); " +
       "Write-Host ''; Write-Host 'Loop exited. Report: loop\runtime\reports  Queue: loop\runtime\human-queue.md'; Read-Host 'Press Enter to close'"
Start-Process powershell -ArgumentList @("-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", $cmd) -WorkingDirectory $root
Write-Host "Loop started in a new window titled 'Game loop'."
Write-Host "Stop after the current phase:  powershell -File loop\stop.ps1"
Write-Host "Stop right now:                powershell -File loop\stop.ps1 -Now"
