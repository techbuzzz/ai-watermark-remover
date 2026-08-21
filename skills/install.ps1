<#
.SYNOPSIS
  Install the WatermarkRemover skills into a target agent's skills
  directory on Windows PowerShell.

.DESCRIPTION
  Mirrors skills/install.sh. The directory resolution rules are the
  same as the C# unit-tested SkillsInstallerTargetResolver
  (src/WatermarkRemover.CLI/Infrastructure/SkillsInstallerTargetResolver.cs).
  Keep the two in sync.

.PARAMETER Agent
  One of: auto, claude (alias: claude-code), opencode,
  minimax (alias: minimax-code), cursor, continue, generic.
  Default: auto.

.PARAMETER Target
  Override the resolved target directory.

.PARAMETER DryRun
  Print what would happen without touching the filesystem.

.PARAMETER List
  List the known agent names and exit.

.EXAMPLE
  PS> .\install.ps1 -Agent opencode

.EXAMPLE
  PS> .\install.ps1 -DryRun
#>
param(
    [ValidateSet('auto', 'claude', 'claude-code', 'opencode', 'minimax', 'minimax-code', 'cursor', 'continue', 'generic')]
    [string] $Agent = 'auto',

    [string] $Target,

    [switch] $DryRun,

    [switch] $List
)

$ErrorActionPreference = 'Stop'

# $PSScriptRoot is the directory the script lives in. Set automatically
# by PowerShell on script invocation; no Split-Path gymnastics needed.
$ScriptDir = $PSScriptRoot
$SkillSubdir = 'watermarkremover'

if ($List) {
    @('auto', 'claude', 'claude-code', 'opencode', 'minimax', 'minimax-code', 'cursor', 'continue', 'generic') |
        ForEach-Object { Write-Output $_ }
    return
}

function Get-HomeDir {
    if ($env:HOME)        { return $env:HOME }
    if ($env:USERPROFILE) { return $env:USERPROFILE }
    return $null
}

function Resolve-CanonicalAgent {
    param([string] $Name)
    switch ($Name.ToLowerInvariant()) {
        ''                          { 'auto' }
        { $_ -in @('auto') }        { 'auto' }
        { $_ -in @('claude', 'claude-code') }    { 'claude' }
        { $_ -in @('opencode') }    { 'opencode' }
        { $_ -in @('minimax', 'minimax-code', 'minimaxcode') } { 'minimax' }
        { $_ -in @('cursor') }      { 'cursor' }
        { $_ -in @('continue') }    { 'continue' }
        { $_ -in @('generic') }     { 'generic' }
        default {
            throw "Unknown agent '$Name'. Known: auto, claude, claude-code, opencode, minimax, minimax-code, cursor, continue, generic."
        }
    }
}

function Resolve-TargetDir {
    param([string] $AgentName, [string] $Override)

    $canonical = $AgentName
    if ($canonical -eq 'auto' -and $env:WATERMARKREMOVER_SKILLS_AGENT) {
        $canonical = Resolve-CanonicalAgent -Name $env:WATERMARKREMOVER_SKILLS_AGENT
    }
    if ($canonical -eq 'auto') {
        if (Test-Path -LiteralPath (Join-Path (Get-Location) '.opencode') -PathType Container) {
            $canonical = 'opencode'
        } elseif (Test-Path -LiteralPath (Join-Path (Get-Location) '.claude') -PathType Container) {
            $canonical = 'claude'
        } elseif (Test-Path -LiteralPath (Join-Path (Get-Location) '.minimax') -PathType Container) {
            $canonical = 'minimax'
        } else {
            $canonical = 'generic'
        }
    }

    if ($Override) { return $Override }

    $homeDir = Get-HomeDir

    switch ($canonical) {
        'claude' {
            if ($env:WATERMARKREMOVER_SKILLS_CLAUDE_DIR)  { return $env:WATERMARKREMOVER_SKILLS_CLAUDE_DIR }
            if (-not $homeDir) { throw "Cannot resolve a home-relative claude target: HOME and USERPROFILE are both empty." }
            return (Join-Path $homeDir '.claude/skills')
        }
        'opencode' {
            if ($env:WATERMARKREMOVER_SKILLS_OPENCODE_DIR) { return $env:WATERMARKREMOVER_SKILLS_OPENCODE_DIR }
            return (Join-Path (Get-Location) '.opencode/skills')
        }
        'minimax' {
            if ($env:WATERMARKREMOVER_SKILLS_MINIMAX_DIR) { return $env:WATERMARKREMOVER_SKILLS_MINIMAX_DIR }
            if (-not $homeDir) { throw "Cannot resolve a home-relative minimax target: HOME and USERPROFILE are both empty." }
            return (Join-Path $homeDir '.minimax/skills')
        }
        'cursor' {
            if (-not $homeDir) { throw "Cannot resolve a home-relative cursor target: HOME and USERPROFILE are both empty." }
            return (Join-Path $homeDir '.cursor/skills')
        }
        'continue' {
            if (-not $homeDir) { throw "Cannot resolve a home-relative continue target: HOME and USERPROFILE are both empty." }
            return (Join-Path $homeDir '.continue/skills')
        }
        'generic' {
            if ($env:WATERMARKREMOVER_SKILLS_GENERIC_DIR) { return $env:WATERMARKREMOVER_SKILLS_GENERIC_DIR }
            if ($homeDir) { return (Join-Path $homeDir '.config/watermarkremover/skills') }
            return (Join-Path (Get-Location) 'skills')
        }
    }
}

$canonical = Resolve-CanonicalAgent -Name $Agent
$targetRoot = Resolve-TargetDir -AgentName $canonical -Override $Target
$target = Join-Path $targetRoot $SkillSubdir

Write-Host "==> agent      = $canonical"
Write-Host "==> target     = $target"
if ($DryRun) { Write-Host "==> dry-run    = yes" }

$skillDirs = @()
Get-ChildItem -LiteralPath $ScriptDir -Directory |
    Where-Object { $_.Name -notin @('install.sh', 'install.ps1') -and (Test-Path -LiteralPath (Join-Path $_.FullName 'SKILL.md') -PathType Leaf) } |
    ForEach-Object { $skillDirs += $_.Name }

if ($skillDirs.Count -eq 0) {
    throw "no skill folders with SKILL.md found under $ScriptDir"
}

Write-Host "==> skills     = $($skillDirs -join ', ')"

if (-not $DryRun) {
    New-Item -ItemType Directory -Force -Path $target | Out-Null
}

foreach ($name in $skillDirs) {
    $src = Join-Path $ScriptDir $name
    $dst = Join-Path $target $name
    if ($DryRun) {
        Write-Host "==> would copy: $src -> $dst"
    } else {
        if (Test-Path -LiteralPath $dst) {
            Write-Warning "overwriting existing skill at $dst"
            Remove-Item -LiteralPath $dst -Recurse -Force
        }
        Copy-Item -LiteralPath $src -Destination $dst -Recurse -Force
        Write-Host "==> copied     : $name"
    }
}

if ($DryRun) {
    Write-Host "==> done. (dry-run) no files were written"
} else {
    Write-Host "==> done. skills installed to $target"
}
