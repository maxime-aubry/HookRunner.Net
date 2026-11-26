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
    public void Fails()
    {
        int value = 0;
        try { _ = value; }
        catch { throw; }
    }
}
'@)
    Expect-Failure 'try-catch' @('tools/HookRunner.CI/bin/Release/net11.0/HookRunner.CI.dll', 'try-catch', $fixture) 'HR0001'
    Remove-Item -LiteralPath $probe

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

    $domainProbe = Join-Path $fixture 'src/HookRunner.Domain/FailureProbe.cs'
    [IO.File]::WriteAllText($domainProbe, @'
namespace HookRunner.Domain;

/// <summary>Provides an intentional analyzer violation.</summary>
public static class FailureProbe
{
    /// <summary>Rethrows an exception in a way that loses its original stack trace.</summary>
    public static void Fails()
    {
        try
        {
            throw new System.InvalidOperationException();
        }
        catch (System.Exception exception)
        {
            throw exception;
        }
    }
}
'@)
    Expect-Failure 'analyzer' @('build', 'src/HookRunner.Domain/HookRunner.Domain.csproj', '-c', 'Release', '--no-restore') 'CA2200'
    Remove-Item -LiteralPath $domainProbe

    $cliPath = Join-Path $fixture 'src/HookRunner.Cli/HookRunner.Cli.csproj'
    $originalCli = [IO.File]::ReadAllText($cliPath)
    [IO.File]::WriteAllText($cliPath, $originalCli.Replace('</ItemGroup>', '<ProjectReference Include="../HookRunner.Domain/HookRunner.Domain.csproj" /></ItemGroup>'))
    Invoke-Checked $dotnet @('restore', 'HookRunner.slnx')
    Expect-Failure 'architecture' @('test', $arch, '-c', 'Release', '--no-restore') 'EvaluatedProjectReferencesMatchThePolicy'
    [IO.File]::WriteAllText($cliPath, $originalCli)
    Invoke-Checked $dotnet @('restore', 'HookRunner.slnx')

    # Inject a binary reference only: the evaluated project-reference graph stays valid.
    $cliProbe = Join-Path $fixture 'src/HookRunner.Cli/FailureProbe.cs'
    [IO.File]::WriteAllText($cliProbe, @'
namespace HookRunner.Cli;

/// <summary>Exposes the intentional binary dependency.</summary>
public static class FailureProbe
{
    /// <summary>Gets the dependency marker.</summary>
    public static int Value => 1;
}
'@)
    Invoke-Checked $dotnet @('build', 'src/HookRunner.Cli/HookRunner.Cli.csproj', '-c', 'Release', '--no-restore')
    $infraPath = Join-Path $fixture 'src/HookRunner.Infrastructure/HookRunner.Infrastructure.csproj'
    $originalInfra = [IO.File]::ReadAllText($infraPath)
    $infraProbe = Join-Path $fixture 'src/HookRunner.Infrastructure/FailureProbe.cs'
    [IO.File]::WriteAllText($infraProbe, @'
namespace HookRunner.Infrastructure;

/// <summary>Introduces the forbidden compiled dependency on Cli.</summary>
public static class FailureProbe
{
    /// <summary>Gets the marker from the forbidden dependency.</summary>
    public static int Value => Cli.FailureProbe.Value;
}
'@)
    [IO.File]::WriteAllText($infraPath, $originalInfra.Replace('</ItemGroup>', '<Reference Include="HookRunner.Cli"><HintPath>../HookRunner.Cli/bin/Release/net11.0/HookRunner.Cli.dll</HintPath></Reference></ItemGroup>'))
    Invoke-Checked $dotnet @('test', $arch, '-c', 'Release', '--no-restore', '--filter', 'FullyQualifiedName~EvaluatedProjectReferencesMatchThePolicy')
    Expect-Failure 'architecture-compiled' @('test', $arch, '-c', 'Release', '--no-build', '--no-restore', '--filter', 'FullyQualifiedName~CompiledAssembliesHaveNoForbiddenLayerDependencies') 'CompiledAssembliesHaveNoForbiddenLayerDependencies'
    [IO.File]::WriteAllText($infraPath, $originalInfra)
    Remove-Item -LiteralPath $infraProbe, $cliProbe

    [IO.File]::WriteAllText($cliPath, $originalCli.Replace('<OutputType>Library</OutputType>', '<OutputType>Exe</OutputType>'))
    [IO.File]::WriteAllText($cliProbe, 'namespace HookRunner.Cli; internal static class FailureProbe { private static void Main() { } }')
    Expect-Failure 'cli-library' @('test', $arch, '-c', 'Release', '--no-restore', '--filter', 'FullyQualifiedName~CliRemainsAClassLibrary') 'CliRemainsAClassLibrary'
    [IO.File]::WriteAllText($cliPath, $originalCli)
    Remove-Item -LiteralPath $cliProbe

    [IO.File]::WriteAllText($domainProbe, @'
namespace HookRunner.Domain;

/// <summary>Provides a branch that the probe deliberately leaves uncovered.</summary>
public static class FailureProbe
{
    /// <summary>Chooses a value using the branch under test.</summary>
    /// <param name="value">The branch selector.</param>
    /// <returns>One for true, otherwise two.</returns>
    public static int Choose(bool value) => value ? 1 : 2;
}
'@)
    [IO.File]::WriteAllText((Join-Path $fixture $probe), @'
namespace HookRunner.ArchitectureTests;

public sealed class FailureProbe
{
    [Fact]
    public void CoversOneBranch() => Assert.Equal(1, Domain.FailureProbe.Choose(true));
}
'@)
    $coverage = Join-Path $fixture 'artifacts/coverage/probe/'
    New-Item -ItemType Directory -Force -Path $coverage | Out-Null
    Invoke-Checked $dotnet @('test', $arch, '-c', 'Release', '--no-restore', '-p:CollectCoverage=true',
        '-p:CoverletOutputFormat=json', "-p:CoverletOutput=$coverage", '-p:Include=[HookRunner.Domain]*', '-p:IncludeTestAssembly=false')
    Expect-Failure 'coverage-branch' @('tools/HookRunner.CI/bin/Release/net11.0/HookRunner.CI.dll', 'coverage', $fixture) 'failed'
    $gate = Get-Content -Raw -LiteralPath (Join-Path $fixture 'artifacts/coverage/gate.json') | ConvertFrom-Json
    $domain = $gate | Where-Object { $_.Assembly -ceq 'HookRunner.Domain' }
    if ($domain.Status -cne 'failed' -or $domain.UncoveredLines.Count -ne 0 -or $domain.UncoveredBranches.Count -eq 0) { throw 'Expected full line coverage but missing branch coverage.' }
    $results | ConvertTo-Json -Depth 5 | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $reports 'summary.json')
} finally {
    Set-Location $sourceRoot
    $resolved = [IO.Path]::GetFullPath($fixture)
    if ([IO.Path]::GetDirectoryName($resolved) -ne $temporaryRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) -or
        -not [IO.Path]::GetFileName($resolved).StartsWith('hookrunner-ci-probes-')) { throw 'Refusing unexpected fixture cleanup path.' }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
# Reached only after every intentional failure was verified and cleanup completed.
$global:LASTEXITCODE = 0
