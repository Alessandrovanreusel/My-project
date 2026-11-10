param([string]$Out)
'unix_ms,private_mb,working_set_mb,process_count' | Out-File -Encoding ascii $Out
Start-Sleep -Milliseconds 1500
while ($all = @(Get-Process -Name 'My project' -ErrorAction SilentlyContinue)) {
  # A previous run can still be exiting when the next starts: sample the newest process only.
  $p = $all | Sort-Object StartTime -Descending | Select-Object -First 1
  $t = [DateTimeOffset]::Now.ToUnixTimeMilliseconds()
  ('{0},{1:0},{2:0},{3}' -f $t, ($p.PrivateMemorySize64 / 1MB), ($p.WorkingSet64 / 1MB), $all.Count) | Out-File -Append -Encoding ascii $Out
  Start-Sleep -Seconds 1
}
