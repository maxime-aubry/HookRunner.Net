param(
    [ValidateSet('All', 'BuildTest', 'Quality', 'Security')] [string] $Task = 'All',
    [switch] $Locked,
    [switch] $SkipRestore
)
. "$PSScriptRoot/ci-common.ps1"
Push-Location $RepositoryRoot
try {
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = 'true'
    $env:DOTNET_NOLOGO = 'true'
    $artifacts = Join-Path $RepositoryRoot 'artifacts'
    if ($Task -in @('All', 'BuildTest')) {
        foreach ($directory in @('coverage', 'tests', 'inventory')) {
            $target = [IO.Path]::GetFullPath((Join-Path $artifacts $directory))
            if ([IO.Path]::GetDirectoryName($target) -ne [IO.Path]::GetFullPath($artifacts)) { throw 'Unexpected report cleanup target.' }
            if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Recurse -Force }
        }
    }
    New-Item -ItemType Directory -Force -Path "$artifacts/coverage", "$artifacts/tests", "$artifacts/security" | Out-Null
    $dotnet = (Get-Command dotnet -ErrorAction Stop).Source
    $sdk = Get-Content -Raw -LiteralPath 'global.json' | ConvertFrom-Json
    $actualSdk = & $dotnet --version
    if ($LASTEXITCODE -ne 0 -or $actualSdk -cne $sdk.sdk.version) { throw 'Install the exact stable SDK specified by global.json.' }
    if (-not $SkipRestore) {
        $restore = @('restore', 'HookRunner.slnx')
        if ($Locked) { $restore += '--locked-mode' }
        Invoke-Checked $dotnet $restore
    }
    $buildProject = if ($Task -in @('All', 'BuildTest')) { 'HookRunner.slnx' } else { 'tools/HookRunner.CI/HookRunner.CI.csproj' }
    Invoke-Checked $dotnet @('build', $buildProject, '-c', 'Release', '--no-restore')
    $helper = Join-Path $RepositoryRoot 'tools/HookRunner.CI/bin/Release/net11.0/HookRunner.CI.dll'

    if ($Task -in @('All', 'BuildTest')) {
        Invoke-Checked (Get-Bash) @('scripts/test-branch-names.sh')
        & "$PSScriptRoot/test-pr-policy.ps1"
        # Every new test project is discovered automatically. Merge coverage across suites in the gate.
        $tests = Get-ChildItem -LiteralPath 'tests' -Recurse -Filter '*.csproj'
        if (@($tests).Count -eq 0) { throw 'No test projects discovered.' }
        foreach ($test in $tests) {
            $name = $test.BaseName
            $coverage = "$artifacts/coverage/$name/"
            New-Item -ItemType Directory -Force -Path $coverage | Out-Null
            Invoke-Checked $dotnet @('test', $test.FullName, '-c', 'Release', '--no-build', '--no-restore',
                '--logger', "trx;LogFileName=$name.trx", '--results-directory', "$artifacts/tests/$name",
                '-p:CollectCoverage=true', '-p:CoverletOutputFormat=json%2ccobertura', "-p:CoverletOutput=$coverage",
                '-p:Include=[HookRunner.*]*', '-p:Exclude=[HookRunner.*Tests]*%2c[HookRunner.CI]*',
                '-p:ExcludeByFile=**/obj/**/*.cs', '-p:IncludeTestAssembly=false')
            [xml] $trx = Get-Content -Raw -LiteralPath "$artifacts/tests/$name/$name.trx"
            if ([int] $trx.TestRun.ResultSummary.Counters.total -le 0) { throw "No tests executed for $name" }
        }
        # Produce both human and machine reports even when the coverage gate subsequently fails.
        Invoke-Checked $dotnet @('tool', 'restore')
        Invoke-Checked $dotnet @('tool', 'run', 'reportgenerator', "-reports:$artifacts/coverage/**/coverage.cobertura.xml",
            "-targetdir:$artifacts/coverage/html", '-reporttypes:Html;Cobertura;JsonSummary')
        Invoke-Checked $dotnet @($helper, 'coverage', $RepositoryRoot)
    }
    if ($Task -in @('All', 'Quality')) {
        Invoke-Checked $dotnet @($helper, 'try-catch', $RepositoryRoot)
        Invoke-Checked $dotnet @('format', 'HookRunner.slnx', '--no-restore', '--verify-no-changes', '--severity', 'info', '--report', "$artifacts/format.json")
        Invoke-Checked $dotnet @($helper, 'docs', $RepositoryRoot)
        & "$PSScriptRoot/install-ci-tools.ps1" -Tools actionlint
        $suffix = if ($env:OS -eq 'Windows_NT') { '.exe' } else { '' }
        Invoke-Checked "$artifacts/tooling/actionlint/actionlint$suffix" @('-color')
    }
    if ($Task -in @('All', 'Security')) {
        $vulnerabilities = & $dotnet package list --project HookRunner.slnx --vulnerable --include-transitive --format json --no-restore
        if ($LASTEXITCODE -ne 0) { throw 'NuGet vulnerability query failed.' }
        $vulnerabilities | Set-Content -Encoding UTF8 -LiteralPath "$artifacts/security/nuget-vulnerabilities.json"
        $outdated = & $dotnet package list --project HookRunner.slnx --outdated --include-transitive --format json --no-restore
        if ($LASTEXITCODE -ne 0) { throw 'NuGet outdated package query failed.' }
        $outdated | Set-Content -Encoding UTF8 -LiteralPath "$artifacts/security/nuget-outdated.json"
        Invoke-Checked $dotnet @($helper, 'audit', "$artifacts/security/nuget-vulnerabilities.json")
        & "$PSScriptRoot/install-ci-tools.ps1" -Tools gitleaks
        $suffix = if ($env:OS -eq 'Windows_NT') { '.exe' } else { '' }
        $gitleaks = "$artifacts/tooling/gitleaks/gitleaks$suffix"
        Invoke-Checked $gitleaks @('git', '--log-opts=--all', '--redact=100', '--no-banner', '--report-format=json', "--report-path=$artifacts/security/secrets-history.json", '.')
        Invoke-Checked $gitleaks @('dir', '--redact=100', '--no-banner', '--report-format=json', "--report-path=$artifacts/security/secrets-worktree.json", '.')
    }
} finally { Pop-Location }
