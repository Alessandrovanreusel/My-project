# Story 1.15, Task 11: run the fenced Recording Bench build and sample its process memory from OUTSIDE
# (Mono's Process.PrivateMemorySize64 reads 0 inside the player; loop lessons, 1-14).
#   powershell -ExecutionPolicy Bypass -File tools/verification/run_recording_bench.ps1 <exe> <outDir> [rounds]
param([string]$Exe, [string]$Out, [int]$Rounds = 3)
New-Item -ItemType Directory -Force $Out | Out-Null
$abs = (Resolve-Path $Out).Path
$p = Start-Process -FilePath $Exe -PassThru -ArgumentList @(
    '-recording-bench', '-bench-out', "`"$abs`"", '-bench-rounds', "$Rounds",
    '-screen-width', '1920', '-screen-height', '1080', '-screen-fullscreen', '0', '-logFile', "`"$abs\player.log`"")
'unix_ms,private_mb,working_set_mb' | Out-File -Encoding ascii "$abs\procmem.csv"
while (-not $p.HasExited) {
    try {
        $p.Refresh()
        $t = [DateTimeOffset]::Now.ToUnixTimeMilliseconds()
        ('{0},{1:0},{2:0}' -f $t, ($p.PrivateMemorySize64 / 1MB), ($p.WorkingSet64 / 1MB)) | Out-File -Append -Encoding ascii "$abs\procmem.csv"
    } catch {}
    Start-Sleep -Seconds 1
}
"exit code $($p.ExitCode)" | Out-File -Encoding ascii "$abs\exit.txt"
