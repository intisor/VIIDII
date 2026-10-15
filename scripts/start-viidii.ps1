# Requires a token. Checks, in order:
#   1. CLOUDFLARE_TUNNEL_TOKEN in the environment
#   2. token.txt beside this script
# Both are read at run time and never written to the repository.
#
# Get a token from the Cloudflare dashboard: Zero Trust > Networks > Tunnels >
# viidii > Configure > Install and run a connector, "token" command.
param(
    [int]$Port = 5199,
    [switch]$SkipBuild,
    [switch]$NoTunnel
)

$ErrorActionPreference = 'Stop'

$repo      = Split-Path -Parent $PSScriptRoot
$project   = Join-Path $repo 'VIIDII.csproj'
$logDir    = Join-Path $repo '.run'
$dbPath    = Join-Path $logDir 'viidii.db'

New-Item -ItemType Directory -Force -Path $logDir | Out-Null

function Write-Log($message) {
    $line = "[{0:HH:mm:ss}] {1}" -f (Get-Date), $message
    Write-Host $line
    Add-Content -Path (Join-Path $logDir 'supervisor.log') -Value $line
}

function Stop-Existing {
    # The app is launched as "dotnet VIIDII.dll", so the process is named dotnet,
    # not VIIDII. Matching on the name alone finds nothing and the old instance
    # keeps the port, so match on what is actually holding it.
    $listeners = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue |
                 Select-Object -ExpandProperty OwningProcess -Unique

    foreach ($pid_ in $listeners) {
        Write-Log "Port $Port is held by pid $pid_, stopping it"
        Stop-Process -Id $pid_ -Force -ErrorAction SilentlyContinue
    }

    $stale = Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" -ErrorAction SilentlyContinue |
             Where-Object { $_.CommandLine -like '*VIIDII.dll*' }

    foreach ($p in $stale) {
        Write-Log "Stopping orphaned VIIDII host (pid $($p.ProcessId))"
        Stop-Process -Id $p.ProcessId -Force -ErrorAction SilentlyContinue
    }

    $cf = Get-Process -Name 'cloudflared' -ErrorAction SilentlyContinue
    if ($cf) {
        Write-Log "Stopping existing cloudflared (pid $($cf.Id -join ', '))"
        $cf | Stop-Process -Force -ErrorAction SilentlyContinue
    }

    Start-Sleep -Seconds 2
}

function Get-TunnelToken {
    if ($env:CLOUDFLARE_TUNNEL_TOKEN) { return $env:CLOUDFLARE_TUNNEL_TOKEN }

    $file = Join-Path $PSScriptRoot 'token.txt'
    if (Test-Path $file) { return (Get-Content $file -Raw).Trim() }

    return $null
}

function Test-OriginHealthy {
    try {
        $r = Invoke-WebRequest -Uri "http://localhost:$Port/health" -UseBasicParsing -TimeoutSec 5
        return $r.StatusCode -eq 200
    } catch { return $false }
}

# ---------------------------------------------------------------------------

Stop-Existing

if (-not $SkipBuild) {
    Write-Log 'Building...'
    & dotnet build $project --nologo -v q 2>&1 |
        Select-String -Pattern 'error CS' | ForEach-Object { Write-Log $_ }
    if ($LASTEXITCODE -ne 0) { Write-Log 'Build failed. Exiting.'; exit 1 }
}

# The tunnel points at whatever is on this port, so a dead app is a 502. That is
# why the loop below restarts it instead of leaving the site broken.
$env:Database__Provider                        = 'Sqlite'
$env:ConnectionStrings__DefaultConnection      = "Data Source=$dbPath"
$env:ASPNETCORE_ENVIRONMENT                    = 'Development'
$env:Logging__LogLevel__Default                = 'Information'
$env:Logging__LogLevel__Microsoft              = 'Warning'
$env:GODEBUG                                   = 'netdns=go'

$dll = Join-Path $repo 'bin\Debug\net10.0\VIIDII.dll'
if (-not (Test-Path $dll)) { Write-Log "Missing $dll. Run without -SkipBuild."; exit 1 }

Write-Log "Starting app on port $Port"
$app = Start-Process dotnet -ArgumentList @($dll, '--urls', "http://localhost:$Port") `
    -WorkingDirectory $repo -PassThru -WindowStyle Hidden `
    -RedirectStandardOutput (Join-Path $logDir 'app.log') `
    -RedirectStandardError  (Join-Path $logDir 'app.err')

$tunnel = $null
if (-not $NoTunnel) {
    $token = Get-TunnelToken
    if ($token) {
        $cf = (Get-Command cloudflared -ErrorAction SilentlyContinue).Source
        if (-not $cf) { $cf = "${env:ProgramFiles(x86)}\cloudflared\cloudflared.exe" }

        Write-Log 'Starting tunnel connector'
        $tunnel = Start-Process $cf -ArgumentList @('tunnel', '--no-autoupdate', 'run', '--token', $token) `
            -PassThru -WindowStyle Hidden `
            -RedirectStandardOutput (Join-Path $logDir 'cloudflared.log') `
            -RedirectStandardError  (Join-Path $logDir 'cloudflared.err')
    } else {
        Write-Log 'No tunnel token found, running locally only. Set CLOUDFLARE_TUNNEL_TOKEN to enable.'
    }
}

Write-Log 'Supervising. Ctrl+C to stop.'

# Everything below runs on a timer and must never be allowed to end the loop.
# With ErrorActionPreference set to Stop, one failed Start-Process, or one moment
# where the port is not yet free, took the whole supervisor down and the site stayed
# broken until someone noticed. That is the failure this script exists to prevent,
# so it has to survive its own failures.
$failures = 0
while ($true) {
    Start-Sleep -Seconds 5

    try {
        # The app. Checked by HTTP rather than by process, because a process can be
        # alive while it has stopped serving, which is exactly the state that produced
        # a 502 that never recovered.
        if ($app.HasExited -or -not (Test-OriginHealthy)) {
            $failures++

            if (-not $app.HasExited) {
                Write-Log "App alive but not answering on port $Port, restarting."
                $app | Stop-Process -Force -ErrorAction SilentlyContinue
            } else {
                Write-Log "App exited with code $($app.ExitCode), restarting."
            }

            # Give the old socket time to clear, otherwise the replacement fails to
            # bind and the next attempt inherits the same problem.
            Start-Sleep -Seconds 3

            # Back off if it keeps dying, so a bad build does not spin forever.
            if ($failures -gt 3) {
                Write-Log "Failed $failures times, waiting 15s before trying again."
                Start-Sleep -Seconds 15
            }

            $app = Start-Process dotnet -ArgumentList @($dll, '--urls', "http://localhost:$Port") `
                -WorkingDirectory $repo -PassThru -WindowStyle Hidden `
                -RedirectStandardOutput (Join-Path $logDir 'app.log') `
                -RedirectStandardError  (Join-Path $logDir 'app.err')

            if (Test-OriginHealthy) {
                Write-Log 'Recovered.'
                $failures = 0
            }
        } elseif ($failures -gt 0) {
            $failures = 0
        }

        # The tunnel. cloudflared reconnects on its own for network blips; this only
        # catches the process being gone altogether.
        if ($tunnel -and $tunnel.HasExited) {
            Write-Log 'Tunnel connector exited, restarting.'
            $tunnel = Start-Process $cf -ArgumentList @('tunnel', '--no-autoupdate', 'run', '--token', $token) `
                -PassThru -WindowStyle Hidden `
                -RedirectStandardOutput (Join-Path $logDir 'cloudflared.log') `
                -RedirectStandardError  (Join-Path $logDir 'cloudflared.err')
        }
    }
    catch {
        # Log and carry on. The next tick will try again.
        Write-Log "Supervisor tick failed: $($_.Exception.Message)"
        Start-Sleep -Seconds 5
    }
}