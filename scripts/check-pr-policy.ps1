param([string] $EventPath = $env:GITHUB_EVENT_PATH, [string] $Repository = $env:GITHUB_REPOSITORY)
. "$PSScriptRoot/ci-common.ps1"
if (-not $EventPath -or -not $Repository) { throw 'PR event path and repository identity are required.' }
$eventData = Get-Content -Raw -LiteralPath $EventPath | ConvertFrom-Json
$pr = $eventData.pull_request
if ($pr.base.repo.full_name -cne $Repository) { throw 'Unexpected PR base repository.' }
$sameRepository = $pr.head.repo -and $pr.head.repo.full_name -ceq $Repository
if ($pr.base.ref -ceq 'main') {
    if (-not $sameRepository -or $pr.head.ref -cne 'develop') { throw 'Only this repository develop branch may target main.' }
} elseif ($pr.base.ref -ceq 'develop') {
    if ($pr.head.ref -cin @('main', 'develop')) { throw 'Use a working branch to target develop.' }
} else { throw 'Only main and develop are supported PR targets.' }
$arguments = @('scripts/check-branch-name.sh')
if ($sameRepository -and $pr.user.login -ceq 'dependabot[bot]' -and $pr.user.type -ceq 'Bot') {
    $arguments += '--dependabot'
}
$arguments += $pr.head.ref
Push-Location $RepositoryRoot
try { Invoke-Checked (Get-Bash) $arguments }
finally { Pop-Location }
Write-Output "PR route and source branch accepted: $($pr.head.ref) -> $($pr.base.ref)"
