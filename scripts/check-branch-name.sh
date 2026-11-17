#!/usr/bin/env bash
set -euo pipefail
export LC_ALL=C

if [[ $# -ne 1 ]]; then
  printf 'Usage: bash scripts/check-branch-name.sh <branch-name>\n' >&2
  exit 2
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
