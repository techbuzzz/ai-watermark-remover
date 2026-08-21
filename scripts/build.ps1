<#
.SYNOPSIS
  One-command build for WatermarkRemover on Windows PowerShell.

.DESCRIPTION
  Builds the Astro web UI and the .NET solution in the right order, syncing
  the static bundle into the .NET CLI's wwwroot/. After this script
  finishes, `dotnet run --project src/WatermarkRemover.CLI -- serve`
  exposes both the API and the UI on the same port.

.PARAMETER SkipWeb
  Skip the web UI build (for .NET-only development).

.PARAMETER SkipDotnet
  Skip the .NET build (for web-only development).

.PARAMETER Configuration
  .NET build configuration. Default: Release.

.PARAMETER Serve
  After building, run the API + UI on the chosen port. Default: -Serve:$false.

.PARAMETER Port
  Port to bind when -Serve is set. Default: 5080.

.EXAMPLE
  PS> .\scripts\build.ps1
  Builds everything.

.EXAMPLE
  PS> .\scripts\build.ps1 -Serve -Port 5080
  Builds everything and starts the server on http://localhost:5080/.
#>

[CmdletBinding()]
param(
    [switch] $SkipWeb,
    [switch] $SkipDotnet,
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',
    [switch] $Serve,
    [int] $Port = 5080
)

$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')

$Solution = 'src\WatermarkRemover.sln'
$Project  = 'src\WatermarkRemover.CLI\WatermarkRemover.CLI.csproj'
$WebDir   = 'web'

# ---- Web UI ----
if (-not $SkipWeb) {
    Write-Host "==> Building Astro web UI" -ForegroundColor Cyan
    Push-Location $WebDir
    try {
        # npm sometimes prints deprecation warnings to stderr and exits
        # non-zero. With $ErrorActionPreference = 'Stop' that would abort
        # the script even though the build succeeded. We temporarily relax
        # the preference around the npm calls, capture their full output,
        # and only fail when the actual sync step didn't run.
        $prevPref = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        try {
            $buildOutput = ''
            if (Test-Path 'package-lock.json') {
                $buildOutput = (& npm.cmd ci --no-audit --no-fund 2>&1 | Out-String)
            } else {
                $buildOutput = (& npm.cmd install --no-audit --no-fund 2>&1 | Out-String)
            }
            Write-Host $buildOutput
            $buildOutput = (& npm.cmd run build 2>&1 | Out-String)
            Write-Host $buildOutput
        } finally {
            $ErrorActionPreference = $prevPref
        }
        if ($buildOutput -notmatch '\[sync\] copied') {
            throw "Web UI build did not produce a synced wwwroot/ directory. Check the npm output above."
        }
    } finally {
        Pop-Location
    }
} else {
    Write-Host "==> Skipping web UI build" -ForegroundColor DarkGray
}

# ---- .NET ----
if (-not $SkipDotnet) {
    Write-Host "==> Building .NET solution ($Configuration)" -ForegroundColor Cyan
    # Run dotnet build, capture its output, and only fail the script on a
    # hard error (Build FAILED line). The exit code can be misleading when
    # there are warnings, so we look at the actual output instead.
    $dotnetOutput = (& dotnet build $Solution -c $Configuration /p:TreatWarningsAsErrors=true 2>&1 | Out-String)
    Write-Host $dotnetOutput
    if ($dotnetOutput -match 'Build FAILED\.' -or $dotnetOutput -match 'error MSB') {
        throw "dotnet build reported errors. See the output above."
    }
} else {
    Write-Host "==> Skipping .NET build" -ForegroundColor DarkGray
}

Write-Host ""
Write-Host "Build complete. Run:" -ForegroundColor Green
Write-Host "  dotnet run --project $Project -- serve --port $Port" -ForegroundColor Green
Write-Host "Then open http://localhost:$Port/" -ForegroundColor Green

# ---- Serve (optional) ----
if ($Serve) {
    & dotnet run --project $Project -- serve --port $Port
}
