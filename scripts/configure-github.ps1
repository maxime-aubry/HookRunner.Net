param([switch] $Apply, [int] $PullRequest)
. "$PSScriptRoot/ci-common.ps1"
$repository = 'maxime-aubry/HookRunner.Net'
$actual = & gh repo view --json nameWithOwner --jq .nameWithOwner
if ($LASTEXITCODE -ne 0 -or $actual -cne $repository) { throw 'Unexpected repository or unavailable GitHub authentication.' }
$rulesetPath = Join-Path $RepositoryRoot '.github/rulesets/branch-naming.json'
if (-not $Apply) {
    Write-Output 'Preview: default branch develop; merge commits only; Dependabot security alerts and fixes enabled.'
    Get-Content -LiteralPath $rulesetPath
    return
}
if ($PullRequest -le 0) { throw 'Supply the validated implementation PR number with -PullRequest.' }
$checksText = & gh pr checks $PullRequest --json name,state
if ($LASTEXITCODE -ne 0) { throw 'The implementation PR must have passing checks before applying protections.' }
$checks = $checksText | ConvertFrom-Json
foreach ($required in @('Branch name', 'CI')) {
    if (-not @($checks | Where-Object { $_.name -ceq $required -and $_.state -ceq 'SUCCESS' }).Count) { throw "Missing successful required check: $required" }
}
$temporary = Join-Path ([IO.Path]::GetTempPath()) ([IO.Path]::GetRandomFileName() + '.json')
try {
    $settings = @{ default_branch = 'develop'; allow_merge_commit = $true; allow_squash_merge = $false; allow_rebase_merge = $false }
    [IO.File]::WriteAllText($temporary, ($settings | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
    Invoke-Checked 'gh' @('api', '--method', 'PATCH', "repos/$repository", '--input', $temporary, '--silent')
    $existingText = & gh api "repos/$repository/rulesets"
    if ($LASTEXITCODE -ne 0) { throw 'Cannot list repository rulesets.' }
    $existingRulesets = $existingText | ConvertFrom-Json
    $matching = @($existingRulesets | Where-Object { $_.name -in @('Branch naming', 'Pull requests and CI') })
    if ($matching.Count -gt 1) { throw 'Ambiguous ruleset selection.' }
    $endpoint = "repos/$repository/rulesets"
    $method = 'POST'
    if ($matching.Count -eq 1) { $endpoint += "/$($matching[0].id)"; $method = 'PUT' }
    Invoke-Checked 'gh' @('api', '--method', $method, $endpoint, '--input', $rulesetPath, '--silent')
    Invoke-Checked 'gh' @('api', '--method', 'PUT', "repos/$repository/vulnerability-alerts", '--silent')
    Invoke-Checked 'gh' @('api', '--method', 'PUT', "repos/$repository/automated-security-fixes", '--silent')
    Invoke-Checked 'gh' @('api', "repos/$repository", '--jq', '{default_branch,allow_merge_commit,allow_squash_merge,allow_rebase_merge,security_and_analysis}')
    Invoke-Checked 'gh' @('api', "repos/$repository/rulesets")
    Invoke-Checked 'gh' @('api', "repos/$repository/rules/branches/main")
    Invoke-Checked 'gh' @('api', "repos/$repository/rules/branches/develop")
} finally { Remove-Item -LiteralPath $temporary -Force -ErrorAction SilentlyContinue }
