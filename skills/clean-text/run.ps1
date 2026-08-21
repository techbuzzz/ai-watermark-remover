<#
.SYNOPSIS
  Wrapper for the `watermark-clean-text` skill on Windows PowerShell.

.DESCRIPTION
  Reads text from the pipeline (or the first positional argument) and
  pipes it through `watermarkremover clean-text`. The cleaned text is
  written to the success output stream; the cleanup report goes to the
  error stream.

.PARAMETER Text
  Optional text to clean. If omitted, the function reads from the
  pipeline (Get-Content ... | Invoke-WatermarkCleanText).

.PARAMETER Statistical
  Enable Layer B (synonym rewrite). Default: $false.

.PARAMETER Vendors
  Enable Layer C (vendor-specific rewrites). Default: $false.

.PARAMETER NoUnicode
  Skip Layer A (Unicode hygiene). Default: $false.

.PARAMETER Json
  Emit the full {cleaned, removed[]} JSON instead of just the cleaned
  text. Default: $false.

.EXAMPLE
  PS> 'Hello World' | Invoke-WatermarkCleanText

.EXAMPLE
  PS> Invoke-WatermarkCleanText -Text 'Hello World' -Statistical
#>
[CmdletBinding()]
param(
    [Parameter(ValueFromPipeline = $true, Position = 0)]
    [string] $Text,

    [switch] $Statistical,
    [switch] $Vendors,
    [switch] $NoUnicode,
    [switch] $Json
)

$ErrorActionPreference = 'Stop'

$wr = Get-Command watermarkremover -ErrorAction SilentlyContinue
if (-not $wr) {
    Write-Error "'watermarkremover' not found on PATH. Install: https://github.com/techbuzzz/ai-watermark-remover"
}

$args = @('clean-text', '--stdin')
if ($Statistical) { $args += '--statistical' }
if ($Vendors)     { $args += '--vendors' }
if ($NoUnicode)   { $args += '--no-unicode' }
if ($Json)        { $args += '--json' }

if ($Text) {
    $Text | & watermarkremover @args
} else {
    $input | & watermarkremover @args
}
