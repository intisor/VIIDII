# Stops the supervisor and everything it started.
#
# The app runs as "dotnet VIIDII.dll", so it is matched by what holds the port
# and by its command line rather than by process name. Stopping a process called
# "VIIDII" finds nothing, which is why the site kept coming back after people
# thought they had killed it.
param([int]$Port = 5199)

$ErrorActionPreference = 'SilentlyContinue'

$supervisors = Get-CimInstance Win32_Process -Filter "Name='powershell.exe'" |
    Where-Object { $_.CommandLine -like '*start-viidii.ps1*' }

foreach ($p in $supervisors) {
    Write-Host "Stopping supervisor (pid $($p.ProcessId))"
    Stop-Process -Id $p.ProcessId -Force
}

$listeners = Get-NetTCPConnection -LocalPort $Port -State Listen |
             Select-Object -ExpandProperty OwningProcess -Unique

foreach ($pid_ in $listeners) {
    Write-Host "Stopping app holding port $Port (pid $pid_)"
    Stop-Process -Id $pid_ -Force
}

$hosts = Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" |
         Where-Object { $_.CommandLine -like '*VIIDII.dll*' }

foreach ($p in $hosts) {
    Write-Host "Stopping VIIDII host (pid $($p.ProcessId))"
    Stop-Process -Id $p.ProcessId -Force
}

$cf = Get-Process -Name 'cloudflared'
if ($cf) {
    Write-Host "Stopping cloudflared (pid $($cf.Id -join ', '))"
    $cf | Stop-Process -Force
}

Write-Host 'Stopped.'