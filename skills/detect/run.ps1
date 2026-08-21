<#
.SYNOPSIS
  Wrapper for the `watermark-detect` skill on Windows PowerShell.

.DESCRIPTION
  Reads text from the pipeline (or the file given by -Path) and pipes
  it through the matching `detect-*` CLI command. Always emits the
  JSON report on the success stream.

.PARAMETER Mode
  One of: text, markdown, image. Default: text.

.PARAMETER Path
  Path to a file. For text/markdown mode, content is read from disk.
  For image mode, this is the image to scan.
#>
[CmdletBinding()]
param(
    [ValidateSet('text', 'markdown', 'image')]
    [string] $Mode = 'text',

    [string] $Path
)

$ErrorActionPreference = 'Stop'

$wr = Get-Command watermarkremover -ErrorAction SilentlyContinue
if (-not $wr) {
    Write-Error "'watermarkremover' not found on PATH."
}

switch ($Mode) {
    'text' {
        if ($Path) {
            Get-Content -LiteralPath $Path -Raw -Encoding UTF8 |
                & watermarkremover detect-text --stdin
        } else {
            $input | & watermarkremover detect-text --stdin
        }
    }
    'markdown' {
        if ($Path) {
            Get-Content -LiteralPath $Path -Raw -Encoding UTF8 |
                & watermarkremover detect-markdown --stdin
        } else {
            $input | & watermarkremover detect-markdown --stdin
        }
    }
    'image' {
        & watermarkremover detect-watermark -i $Path
    }
}
