<#
.SYNOPSIS
  Wrapper for the `watermark-clean-markdown` skill on Windows PowerShell.

.DESCRIPTION
  Reads Markdown from the pipeline (or the -Path / -InputObject
  parameter) and pipes it through `watermarkremover clean-markdown
  --strip-all`. Writes the cleaned Markdown to the success stream.

.PARAMETER Path
  Path to a .md file. If omitted, reads from the pipeline.

.PARAMETER NoStripAll
  Don't pass --strip-all (use defaults from config.yaml).

.PARAMETER Json
  Emit the full {cleaned, transformsApplied[]} JSON.
#>
[CmdletBinding()]
param(
    [string] $Path,
    [switch] $NoStripAll,
    [switch] $Json
)

$ErrorActionPreference = 'Stop'

$wr = Get-Command watermarkremover -ErrorAction SilentlyContinue
if (-not $wr) {
    Write-Error "'watermarkremover' not found on PATH."
}

$args = @('clean-markdown')
if (-not $NoStripAll) { $args += '--strip-all' }
if ($Json)            { $args += '--json' }

if ($Path) {
    Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | & watermarkremover @args
} else {
    $input | & watermarkremover @args
}
