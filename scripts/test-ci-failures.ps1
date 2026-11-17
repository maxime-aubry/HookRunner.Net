. "$PSScriptRoot/ci-common.ps1"
$sourceRoot = $RepositoryRoot
$temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$fixture = Join-Path $temporaryRoot ('hookrunner-ci-probes-' + [Guid]::NewGuid().ToString('N'))
$reports = Join-Path $sourceRoot 'artifacts/probes'
New-Item -ItemType Directory -Force -Path $fixture, $reports | Out-Null
$results = [Collections.Generic.List[object]]::new()
$dotnet = (Get-Command dotnet -ErrorAction Stop).Source

function Expect-Failure {
    param([string] $Name, [string[]] $CommandArguments, [string] $Evidence)
    $log = Join-Path $reports "$Name.log"
    # Native stderr is diagnostic output; only the explicit exit code decides success.
    $savedPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    & $dotnet @CommandArguments *> $log
    $code = $LASTEXITCODE
    $ErrorActionPreference = $savedPreference
    $text = Get-Content -Raw -LiteralPath $log
    if ($code -eq 0 -or $text -notmatch [Regex]::Escape($Evidence)) { throw "Failure probe $Name did not fail for the intended reason. See $log" }
    $results.Add(@{ scenario = $Name; exitCode = $code; evidence = $Evidence; status = 'passed' })
    Write-Output "Verified intentional failure: $Name"
}

try {
    Push-Location $sourceRoot
    $paths = & git ls-files --cached --others --exclude-standard
    if ($LASTEXITCODE -ne 0) { throw 'Cannot inventory source files for isolated probes.' }
    foreach ($relative in $paths) {
        if ($relative.StartsWith('prompts/')) { continue }
        if (-not (Test-Path -LiteralPath (Join-Path $sourceRoot $relative) -PathType Leaf)) { continue }
        $destination = Join-Path $fixture $relative
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination) | Out-Null
        Copy-Item -LiteralPath (Join-Path $sourceRoot $relative) -Destination $destination
    }
    Pop-Location
    Push-Location $fixture
    Invoke-Checked $dotnet @('restore', 'HookRunner.slnx', '--locked-mode')
    Invoke-Checked $dotnet @('build', 'HookRunner.slnx', '-c', 'Release', '--no-restore')
    $arch = 'tests/HookRunner.ArchitectureTests/HookRunner.ArchitectureTests.csproj'
    $probe = 'tests/HookRunner.ArchitectureTests/FailureProbe.cs'
    [IO.File]::WriteAllText((Join-Path $fixture $probe), @'
namespace HookRunner.ArchitectureTests;

public sealed class FailureProbe
{
    [Fact]
    public void Fails() => Assert.Fail("Intentional CI failure probe");
}
'@)
    Expect-Failure 'test' @('test', $arch, '-c', 'Release', '--no-restore') 'Intentional CI failure probe'
    Remove-Item -LiteralPath $probe

    [IO.File]::WriteAllText((Join-Path $fixture $probe), "namespace HookRunner.ArchitectureTests;`npublic sealed class FailureProbe { public int Value => 1; }")
    Expect-Failure 'format' @('format', 'whitespace', 'HookRunner.slnx', '--no-restore', '--verify-no-changes') 'FailureProbe.cs'
    Remove-Item -LiteralPath $probe

    $coreProbe = Join-Path $fixture 'src/HookRunner.Core/FailureProbe.cs'
    [IO.File]::WriteAllText($coreProbe, 'namespace HookRunner.Core; public static class FailureProbe { public static void Fails() { try { throw new System.InvalidOperationException(); } catch (System.Exception exception) { throw exception; } } }')
    Expect-Failure 'analyzer' @('build', 'src/HookRunner.Core/HookRunner.Core.csproj', '-c', 'Release', '--no-restore') 'CA2200'
    Remove-Item -LiteralPath $coreProbe

    $infraPath = Join-Path $fixture 'src/HookRunner.Infrastructure/HookRunner.Infrastructure.csproj'
    $original = [IO.File]::ReadAllText($infraPath)
    [IO.File]::WriteAllText($infraPath, $original.Replace('</ItemGroup>', '<ProjectReference Include="../HookRunner.Application/HookRunner.Application.csproj" /></ItemGroup>'))
    Invoke-Checked $dotnet @('restore', 'HookRunner.slnx')
    Expect-Failure 'architecture' @('test', $arch, '-c', 'Release', '--no-restore') 'EvaluatedProjectReferencesMatchThePolicy'
    [IO.File]::WriteAllText($infraPath, $original)
    Invoke-Checked $dotnet @('restore', 'HookRunner.slnx')

    [IO.File]::WriteAllText($coreProbe, @'
namespace HookRunner.Core;

public static class FailureProbe
{
    public static int Choose(bool value) => value ? 1 : 2;
}
'@)
    [IO.File]::WriteAllText((Join-Path $fixture $probe), @'
namespace HookRunner.ArchitectureTests;

public sealed class FailureProbe
{
    [Fact]
    public void CoversOneBranch() => Assert.Equal(1, Core.FailureProbe.Choose(true));
}
'@)
    $coverage = Join-Path $fixture 'artifacts/coverage/probe/'
    New-Item -ItemType Directory -Force -Path $coverage | Out-Null
    Invoke-Checked $dotnet @('test', $arch, '-c', 'Release', '--no-restore', '-p:CollectCoverage=true',
        '-p:CoverletOutputFormat=json', "-p:CoverletOutput=$coverage", '-p:Include=[HookRunner.Core]*', '-p:IncludeTestAssembly=false')
    Expect-Failure 'coverage-branch' @('tools/HookRunner.CI/bin/Release/net11.0/HookRunner.CI.dll', 'coverage', $fixture) 'failed'
    $gate = Get-Content -Raw -LiteralPath (Join-Path $fixture 'artifacts/coverage/gate.json') | ConvertFrom-Json
    $core = $gate | Where-Object { $_.Assembly -ceq 'HookRunner.Core' }
    if ($core.Status -cne 'failed' -or $core.UncoveredLines.Count -ne 0 -or $core.UncoveredBranches.Count -eq 0) { throw 'Expected full line coverage but missing branch coverage.' }
    $results | ConvertTo-Json -Depth 5 | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $reports 'summary.json')
} finally {
    Set-Location $sourceRoot
    $resolved = [IO.Path]::GetFullPath($fixture)
    if ([IO.Path]::GetDirectoryName($resolved) -ne $temporaryRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) -or
        -not [IO.Path]::GetFileName($resolved).StartsWith('hookrunner-ci-probes-')) { throw 'Refusing unexpected fixture cleanup path.' }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
