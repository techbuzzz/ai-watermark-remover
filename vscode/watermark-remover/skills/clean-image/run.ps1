<#
.SYNOPSIS
  Wrapper for the `watermark-clean-image` skill on Windows PowerShell.

.DESCRIPTION
  Pipes an image path through `watermarkremover clean-image`. The
  cleaned image is written to the success stream (redirect to a file
  with [System.IO.File]::WriteAllBytes() or Set-Content -AsByteStream).

.PARAMETER Path
  Path to the image to clean. Required.

.PARAMETER MaskPath
  Optional mask PNG (white = inpaint, black = keep).

.PARAMETER OutputPath
  Optional output path; if omitted, the cleaned bytes are written to
  the success stream.

.PARAMETER NoDetect
  Skip the auto-detect step (only useful with -MaskPath).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Path,

    [string] $MaskPath,
    [string] $OutputPath,
    [switch] $NoDetect
)

$ErrorActionPreference = 'Stop'

$wr = Get-Command watermarkremover -ErrorAction SilentlyContinue
if (-not $wr) {
    Write-Error "'watermarkremover' not found on PATH."
}

$args = @('clean-image', '-i', $Path)
if ($MaskPath)    { $args += @('--mask', $MaskPath) }
if ($OutputPath)  { $args += @('-o',   $OutputPath) }
if ($NoDetect)    { $args += '--no-detect' }

& watermarkremover @args
