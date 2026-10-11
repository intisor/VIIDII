# Stops everything, rebuilds, and starts the supervisor again.
#
# The supervisor holds the built DLL open, so it has to be stopped before a build
# can replace it. Doing that by hand is easy to forget, and the build fails with a
# file lock that looks nothing like the cause.
param([int]$Port = 5199)

$repo = Split-Path -Parent $PSScriptRoot

& powershell -NoProfile -ExecutionPolicy Bypass `
    -File (Join-Path $PSScriptRoot 'stop-viidii.ps1') -Port $Port

Write-Host 'Building...'
& dotnet build (Join-Path $repo 'VIIDII.csproj') --nologo -v q

if ($LASTEXITCODE -ne 0) {
    Write-Host 'Build failed. Not starting.'
    exit 1
}

Write-Host 'Starting supervisor...'
Start-Process powershell `
    -ArgumentList '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File',
                  (Join-Path $PSScriptRoot 'start-viidii.ps1'), '-Port', $Port, '-SkipBuild' `
    -WindowStyle Hidden

Write-Host 'Started. Follow it with:'
Write-Host '  Get-Content .run\supervisor.log -Wait -Tail 20'
Write-Host '  Get-Content .run\app.err -Wait -Tail 20'