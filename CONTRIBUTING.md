# Contributing

All work targets `develop` through a pull request. Only this repository's
`develop` branch may target `main`. Both paths use merge commits; squash and
rebase merges are disabled. Direct commits, pushes, force pushes, and deletions
of `main` and `develop` are forbidden. Required checks must pass with the source
branch up to date with its target. No second-person approval is required.

## Branch names

Use `<prefix>/<description>`, with a prefix from `feat`, `fix`, `refactor`,
`test`, `docs`, `chore`, or `ci`. Descriptions use lowercase ASCII letters and
digits separated by single hyphens. Examples: `feat/add-config`, `fix/issue-42`.
`perf/` and `build/` are intentionally rejected.

The ordinary naming pattern is:

```regex
^(main|develop|(feat|fix|refactor|test|docs|chore|ci)/[a-z0-9]+(-[a-z0-9]+)*)$
```

`main` and `develop` are valid names for PR routing, not push destinations.
The PR checker additionally accepts `dependabot/nuget/...` and
`dependabot/github_actions/...` only when the head belongs to this repository
and the PR author is GitHub's `dependabot[bot]` account with type `Bot`.
Forks and human authors cannot obtain that exception by copying a branch name.
Other bots must use ordinary names until a specific exception is agreed.

## Local setup and checks

Install the exact stable SDK from `global.json`, Git, and PowerShell 7.
Git for Windows provides Bash; Linux and macOS also need Bash on PATH.
Enable the versioned hooks in each clone:

```sh
git config --local core.hooksPath .githooks
```

The hooks are tracked as executable files. If a filesystem does not preserve
that bit, run `chmod +x .githooks/pre-commit .githooks/pre-push` on Unix.
The commit hook rejects commits made on `main` or `develop`. The push hook
checks every destination, including explicit refspecs, and rejects updates or
deletions of either protected branch. Tags and deletion of working branches
remain outside the naming check. Hooks can be bypassed; server rules provide
the enforcement at merge time.

```powershell
./scripts/validate.ps1
./scripts/validate.ps1 -Task BuildTest -Locked
./scripts/validate.ps1 -Task Quality -Locked
./scripts/validate.ps1 -Task Security -Locked
```

Run these in PowerShell. On Windows PowerShell 5.1 with a restrictive script
execution policy, a process-only invocation is also supported:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/validate.ps1
```

This does not change machine policy or disable application control.
See [CI documentation](docs/ci.md) for reports, diagnostics, and tool maintenance.

## Working branches and promotions

```sh
git fetch origin
git switch -c feat/example origin/develop
# Make changes, run checks, and commit on this working branch.
git push -u origin feat/example
gh pr create --base develop --head feat/example
```

Promote with a PR from `develop` to `main`. After its merge, bring the promotion
commit back through a working branch and another PR:

```sh
git fetch origin
git switch -c chore/sync-main origin/develop
git merge origin/main
git push -u origin chore/sync-main
gh pr create --base develop --head chore/sync-main
```

Merge this synchronization PR with a merge commit. Reuse the branch name only
after its previous PR is merged and the old working branch has been removed.
Resolve conflicts on working branches and update PRs through that same route.

## GitHub configuration

The required checks are `Branch name` and `CI`, both from GitHub Actions.
`CI` succeeds only when policy, all three OS test jobs, quality, and security
succeed. Skipped, failed, or cancelled dependencies make it fail.

The proposed server rules are versioned in
[the ruleset JSON](.github/rulesets/branch-naming.json). Preview or apply them
after the implementation PR has successful checks:

```powershell
./scripts/configure-github.ps1
./scripts/configure-github.ps1 -Apply -PullRequest <number>
```

The script sets `develop` as default, permits merge commits only, requires PRs
and up-to-date checks on both branches, protects deletion and force pushes,
and enables Dependabot security alerts and fixes. It defines no bypass actors.
Administrative GitHub access is required. It reads back effective rules;
the presence of the JSON file alone never establishes an active protection.
