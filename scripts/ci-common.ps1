Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$script:RepositoryRoot = Split-Path -Parent $PSScriptRoot

function Invoke-Checked {
    param([string] $File, [string[]] $Arguments)
    & $File @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$File failed with exit code $LASTEXITCODE" }
}

function Get-Bash {
    $command = Get-Command bash -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }
    $gitBash = Join-Path $env:ProgramFiles 'Git/bin/bash.exe'
    if (Test-Path -LiteralPath $gitBash) { return $gitBash }
    throw 'Bash is required. On Windows use Git for Windows.'
}
