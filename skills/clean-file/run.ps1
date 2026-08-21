<#
.SYNOPSIS
  Wrapper for the `watermark-clean-file` skill on Windows PowerShell.

.DESCRIPTION
  Pipes a file path through `watermarkremover clean-file`. The cleaned
  bytes are written to the success stream (redirect to a file with
  Out-File / Set-Content / [System.IO.File]::WriteAllBytes()).

.PARAMETER Path
  Path to the file to clean. Required.

.PARAMETER OutputPath
  Optional output path; if omitted, the cleaned bytes are written to
  the success stream.

.PARAMETER Inspect
  Call `inspect-file` instead and print the JSON report.

.PARAMETER StripIcc
  Also drop the ICC colour profile (off by default).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Path,

    [string] $OutputPath,
    [switch] $Inspect,
    [switch] $StripIcc
)

$ErrorActionPreference = 'Stop'

$wr = Get-Command watermarkremover -ErrorAction SilentlyContinue
if (-not $wr) {
    Write-Error "'watermarkremover' not found on PATH."
}

if ($Inspect) {
    & watermarkremover inspect-file -Path $Path
    return
}

$args = @('clean-file', '-i', $Path)
if ($OutputPath) { $args += @('-o', $OutputPath) }
if ($StripIcc)   { $args += '--strip-icc' }

if ($OutputPath) {
    & watermarkremover @args
} else {
    & watermarkremover @args
}
