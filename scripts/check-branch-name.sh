#!/usr/bin/env bash
set -euo pipefail
export LC_ALL=C

allow_dependabot=false
if [[ $# -eq 2 && $1 == --dependabot ]]; then
  allow_dependabot=true
  shift
fi
if [[ $# -ne 1 ]]; then
  printf 'Usage: bash scripts/check-branch-name.sh <branch-name>\n' >&2
  exit 2
fi

# Only the PR policy checker may grant this after verifying repository and author.
if [[ $allow_dependabot == true && $1 =~ ^dependabot/(nuget|github_actions)/[a-zA-Z0-9._/-]+$ && $1 != *..* ]]; then
  exit 0
fi

pattern='^(main|develop|(feat|fix|refactor|test|docs|chore|ci)/[a-z0-9]+(-[a-z0-9]+)*)$'

if [[ $1 =~ $pattern ]]; then
  exit 0
fi

printf 'Invalid branch name: %s\n' "$1" >&2
printf '%s\n' \
  'Expected: <prefix>/<description> (example: feat/add-config).' \
  'Prefixes: feat, fix, refactor, test, docs, chore, ci.' \
  'Description: lowercase ASCII letters and digits, separated by single hyphens.' \
  'Only main and develop are exempt. Rename the branch with git branch -m <valid-name>.' >&2
exit 1
