# Continuous integration

## Execution and reproduction

The `CI` workflow runs on PRs targeting `develop` or `main` and on manual
dispatch. There are no push triggers, path filters, deployments, or publication
steps. Superseded runs are cancelled. All actions use full commit SHAs and
`contents: read`; checkout does not persist credentials. PR strings pass through
JSON or environment variables, never through generated executable shell code.

`scripts/validate.ps1` runs the same commands locally. `BuildTest` restores the
solution, builds Release, discovers all test projects under `tests/`, runs
VSTest with TRX and Coverlet JSON/Cobertura output, generates HTML, then checks
each production assembly. `Quality` verifies formatting, local Markdown file
links, and workflows. `Security` queries NuGet and runs the free Gitleaks CLI
against Git history and the working tree with fully redacted reports.
Each task stops on a failed native command; no `continue-on-error` is used.
CI restores NuGet lock files in locked mode and caches packages by lock content.
After the coverage gate, `scripts/test-consumer-packages.ps1` packs the four
libraries and builds an independent console host with package references from
a checkout-local feed. It executes a Codex invocation and its child process
without an installed agent or model. This runs in every OS test job.

Full build/test/coverage runs on Ubuntu 24.04 x64, Windows Server 2025 x64 and
macOS 15 Arm64. These are representative supported systems, not evidence of
every OS version or CPU architecture. Formatting and security run once on Linux.
The exact SDK comes from `global.json`, not the runner's preinstalled default.

## Controls

| Control | Tool | Blocking | Local command / task |
|---|---|---|---|
| Release compilation, nullable and enabled analyzers | .NET SDK | Yes | `BuildTest` |
| Evaluated project and compiled assembly dependencies | xUnit + MSBuild inventory + metadata reader | Yes | `BuildTest` |
| Available tests and Git-hook integration scenarios | xUnit/VSTest, Bash, PowerShell | Yes | `BuildTest` |
| Lines and branches per production assembly | Coverlet + independent PDB gate | Yes when measurable | `BuildTest` |
| Formatting and unused imports | `dotnet format --verify-no-changes --severity info` | Yes | `Quality` |
| Workflow syntax and expressions | actionlint | Yes | `Quality` |
| Local documentation file links | CI helper | Yes | `Quality` |
| High/critical direct and transitive vulnerabilities | NuGet audit + JSON gate | Yes | `Security` |
| Low/moderate vulnerabilities and outdated packages | NuGet | Reported | `Security` |
| Secrets in history and working tree | Gitleaks CLI | Yes | `Security` |
| PR route, source identity, branch naming | PowerShell + Bash | Yes | `test-pr-policy.ps1` |

`Directory.Build.props` centralizes nullable, deterministic builds, warnings as
errors, recommended SDK analyzers, and transitive NuGet audit. Low/moderate
NuGet warnings are reported without being errors; high/critical and audit
transport failures remain errors. `Directory.Packages.props` owns package
versions. Implicit transitive project references are disabled to preserve the
agreed direct-reference graph. Evaluated build inventories also catch unused
references added by imports. Metadata tests catch forbidden compiled assembly
references. Neither tests nor analyzers prove all SOLID principles.

The four production libraries are `HookRunner.Domain`, `HookRunner.Application`,
`HookRunner.Infrastructure`, and `HookRunner.Cli`. Domain has no internal
references; Application references Domain; Infrastructure references Domain and
Application; Cli references Application and Infrastructure and remains a class
library. This graph is recorded in [ADR 0004](adr/0004-validated-layer-dependencies.md).

IDE0005 is checked by formatting at information severity: enforcing it during
compilation would require enabling XML documentation generation. Other enabled
compiler/analyzer warnings remain errors. No extra StyleCop package is needed
for the initial rules. Native analyzers catch selected correctness, resource,
async and security issues; they do not provide a comprehensive dead code,
duplication, method-size or complexity quality gate. These limits are explicit.

## Coverage policy and reports

Every production project under `src/` must have a built assembly and portable
PDB. Coverlet instruments production assemblies, excluding test and CI-tool
assemblies and SDK-generated `obj` source. There are no application-file
exclusions or application `ExcludeFromCodeCoverage` annotations.

The independent gate merges hits across test suites, requires every reported
line and branch to have at least one hit, and compares report lines against
visible PDB sequence points. A measurable assembly or source document missing
from reports fails. Percentages are not rounded to determine success, and a
global average cannot compensate for an uncovered assembly. Gate regression
tests cover missing assemblies/documents, independent branch failure, and
merging suite results. A missing or malformed collector report fails.

Domain currently contains no business code. A library without coverable
sequence points is explicitly `not-applicable`, never claimed as tested at
100%. Raw Coverlet output may display 100% for an empty module and emit a
no-hits warning; `gate.json` and `gate.md` provide the authoritative status.
The 100% line and 100% branch requirements apply automatically as measurable
code is added. No fake domain behavior or assertion-free tests are introduced.

Reports under ignored `artifacts/` include:

- `tests/`: TRX by test project.
- `coverage/`: per-suite Coverlet JSON/Cobertura, `gate.json`, `gate.md`, and
  HTML plus merged Cobertura/JSON summary under `html/`.
- `analysis/`: compiler/analyzer SARIF by project.
- `format.json`: structured formatting findings.
- `security/`: NuGet vulnerability/outdated JSON and redacted secret reports.
- `probes/`: logs proving that intentional failures are detected.

GitHub uploads reports even on failure, for seven days, and adds the coverage
gate table to the run summary. The `CI` aggregation job requires every dependency
to succeed. A failing test stops its test command; other OS jobs continue to
provide diagnostics. Fix the reported file or add a useful missing assertion;
never reduce thresholds or add exclusions to make a run green.

## Safety checks and maintenance

`scripts/test-ci-failures.ps1` uses a disposable copy to prove that a failed
test, uncovered branch, formatting error, analyzer violation, and forbidden
project reference fail the appropriate command. Unit tests also verify NuGet
severity policy and coverage report omissions. Fixtures are separate from the
four production libraries.

The architecture probes introduce an unused Cli-to-Domain
project reference and a compiled dependency on Cli, verifying both dependency
checks independently. A separate probe changes Cli to an executable to verify
that its class-library constraint is enforced.

For dependency changes, update central versions, run `dotnet restore` without
locked mode to regenerate locks, review lock diffs, then run full validation.
Dependabot groups compatible NuGet and Actions updates weekly and targets
`develop`. Major updates remain separate. Security fixes use the default
branch, which is also `develop`. All bot PRs run the same full CI.

For SDK updates, change `global.json`, update documentation if support changes,
regenerate locks, and validate all three OS jobs. Tool versions and SHA-256
archive hashes live in `.config/ci-tools.json`; ReportGenerator is pinned in
`.config/dotnet-tools.json`. Review official release checksums when updating.
Native tools are extracted only after their archive hash matches. Do not
commit downloaded SDKs, package caches, or generated reports.

## Free capabilities and deliberate limits

Verified on 2026-11-17: this repository is public. Standard hosted Actions
runners are free for public repositories; larger runners are paid and unused.
Free accounts include 500 MB artifact storage and 10 GB cache per repository.
Seven-day report retention limits consumption; inspect usage and set a zero
spending budget in account billing if enforcing an account-wide spending cap.
This workflow cannot certify billing settings or other repositories' usage.
Private Free repositories have a compute allowance (currently $12/month) and
do not receive the same free branch protections or CodeQL eligibility.
[Actions billing](https://docs.github.com/en/billing/concepts/product-billing/github-actions),
[protected branches](https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/managing-protected-branches/about-protected-branches).

CodeQL is free for public repositories, but its published compatibility matrix
currently documents .NET through version 10. Enable it as a required gate only
after validating .NET 11; SDK security rules are a limited initial alternative,
not an equivalent whole-program security analysis.
[CodeQL compatibility](https://codeql.github.com/docs/codeql-overview/supported-languages-and-frameworks/).

SonarQube Community Build requires running and maintaining a server and has
edition limitations for branch/PR analysis. It is disproportionate for this
serverless scaffold; SDK analyzers run directly in CI.
[SonarQube Community documentation](https://docs.sonarsource.com/sonarqube-community-build/).
Gitleaks uses its free CLI, avoiding any Action-specific licensing dependency.

NuGet packing, documentation-site builds, Pages deployment, integration services,
and benchmarks are not applicable to this empty internal-library milestone.
There is no distributed package, generated documentation site, external service
or critical business operation. Add meaningful integration tests when a service
appears. When a critical operation exists, use BenchmarkDotNet in a separate
manual/scheduled workflow, retain results, and avoid timing gates on noisy
shared runners. No public deployment or package publication is configured.

Server protection is distinct from workflow code. See
[contribution instructions](../CONTRIBUTING.md#github-configuration) and the
[promotion ADR](adr/0003-pull-request-promotion-flow.md) for configuration and
synchronization. If administrative access is unavailable, an administrator must
apply the versioned ruleset; do not report it as active until reading effective
server rules succeeds.
