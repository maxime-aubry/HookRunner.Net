. "$PSScriptRoot/ci-common.ps1"
$fixture = Join-Path ([IO.Path]::GetTempPath()) ([IO.Path]::GetRandomFileName() + '.json')
$count = 0
try {
    $cases = @(
        @('feat/example', 'develop', 'owner/repo', 'user', 'User', $true),
        @('feat/example', 'develop', 'fork/repo', 'user', 'User', $true),
        @('develop', 'main', 'owner/repo', 'user', 'User', $true),
        @('develop', 'main', 'fork/repo', 'user', 'User', $false),
        @('feat/example', 'main', 'owner/repo', 'user', 'User', $false),
        @('main', 'develop', 'owner/repo', 'user', 'User', $false),
        @('dependabot/nuget/develop/example-1.0', 'develop', 'owner/repo', 'dependabot[bot]', 'Bot', $true),
        @('dependabot/github_actions/develop/actions-group', 'develop', 'owner/repo', 'dependabot[bot]', 'Bot', $true),
        @('dependabot/nuget/example', 'develop', 'owner/repo', 'user', 'User', $false),
        @('dependabot/nuget/example', 'develop', 'fork/repo', 'dependabot[bot]', 'Bot', $false),
        @('dependabot/nuget/example', 'main', 'owner/repo', 'dependabot[bot]', 'Bot', $false),
        @('perf/faster', 'develop', 'owner/repo', 'user', 'User', $false),
        @('build/package', 'develop', 'owner/repo', 'user', 'User', $false)
    )
    foreach ($case in $cases) {
        @{ pull_request = @{
            head = @{ ref = $case[0]; repo = @{ full_name = $case[2] } }
            base = @{ ref = $case[1]; repo = @{ full_name = 'owner/repo' } }
            user = @{ login = $case[3]; type = $case[4] }
        } } | ConvertTo-Json -Depth 6 | Set-Content -Encoding UTF8 -LiteralPath $fixture
        $passed = $true
        try { & "$PSScriptRoot/check-pr-policy.ps1" -EventPath $fixture -Repository 'owner/repo' *> $null }
        catch { $passed = $false }
        if ($passed -ne $case[5]) { throw "Unexpected PR policy result for $($case[0]) -> $($case[1])" }
        $count++
    }
    Write-Output "Passed $count PR policy scenarios."
} finally { Remove-Item -LiteralPath $fixture -Force -ErrorAction SilentlyContinue }
