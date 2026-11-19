#!/usr/bin/env bash
set -euo pipefail

repo_root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd -P)
validator="$repo_root/scripts/check-branch-name.sh"
name_count=0
push_count=0

expect_name() {
  local expected=$1 branch=$2 actual=0
  bash "$validator" "$branch" >/dev/null 2>&1 || actual=$?
  if [[ $actual -ne $expected ]]; then
    printf 'FAIL: branch %q returned %s; expected %s\n' "$branch" "$actual" "$expected" >&2
    exit 1
  fi
  name_count=$((name_count + 1))
}

for branch in main develop feat/add-config fix/handle-empty-file refactor/parser \
  test/parser docs/readme chore/cleanup ci/check-branches feat/123 fix/issue-42; do
  expect_name 0 "$branch"
done

for branch in '' feat/ feature/add-config perf/faster build/package hotfix/urgent \
  Feat/add-config feat/Add-config fix/élan fix/foo--bar feat/api/auth \
  fix/-leading fix/trailing- fix/has_underscore fix/has.dot 'fix/has space' \
  main/topic develop/topic dependabot/nuget/example MAIN release/v1 \
  $'feat/valid\nmain'; do
  expect_name 1 "$branch"
done

actual=0
bash "$validator" >/dev/null 2>&1 || actual=$?
[[ $actual -eq 2 ]] || { printf 'FAIL: missing argument must return 2\n' >&2; exit 1; }

# Use isolated local repositories: no network and no changes to the working repo.
temp_root=$(cd -- "${TMPDIR:-/tmp}" && pwd -P)
fixture=$(mktemp -d "$temp_root/hookrunner-branches.XXXXXXXX")
fixture=$(cd -- "$fixture" && pwd -P)
cleanup() {
  case "$fixture" in
    "$temp_root"/hookrunner-branches.*) rm -rf -- "$fixture" ;;
    *) printf 'Refusing to remove unexpected test path: %s\n' "$fixture" >&2 ;;
  esac
}
trap cleanup EXIT

git init --quiet --bare "$fixture/remote.git"
git init --quiet --initial-branch=main "$fixture/work"
cd -- "$fixture/work"
git config user.name 'Branch naming tests'
git config user.email 'branch-tests@example.invalid'
git config commit.gpgSign false
git config core.hooksPath .githooks
mkdir -p .githooks scripts
cp "$repo_root/.githooks/pre-push" .githooks/pre-push
cp "$repo_root/.githooks/pre-commit" .githooks/pre-commit
cp "$validator" scripts/check-branch-name.sh
chmod +x .githooks/pre-push
chmod +x .githooks/pre-commit
git -c core.hooksPath=/dev/null commit --quiet --allow-empty -m 'Test fixture'
git remote add origin "$fixture/remote.git"

expect_push() {
  local expected=$1 actual=0
  shift
  git push --quiet origin "$@" >"$fixture/push.log" 2>&1 || actual=$?
  if { [[ $expected == pass ]] && [[ $actual -ne 0 ]]; } || \
     { [[ $expected == fail ]] && [[ $actual -eq 0 ]]; }; then
    printf 'FAIL: push expected to %s: %s\n' "$expected" "$*" >&2
    cat "$fixture/push.log" >&2
    exit 1
  fi
  if [[ $expected == fail ]]; then
    grep -Eq 'Invalid branch name:|Direct updates to protected branch' "$fixture/push.log"
  fi
  push_count=$((push_count + 1))
}

expect_push fail HEAD:refs/heads/main
expect_push fail HEAD:refs/heads/develop
expect_push fail :refs/heads/main
expect_push fail :refs/heads/develop
expect_push pass HEAD:refs/heads/feat/add-config
expect_push fail HEAD:refs/heads/invalid-name
# Destination name is authoritative, even when the local branch is invalid.
git branch invalid-local
expect_push pass refs/heads/invalid-local:refs/heads/fix/valid-destination
git branch feat/add-config
expect_push fail refs/heads/feat/add-config:refs/heads/invalid-destination
# Mixed pushes must fail as a whole.
expect_push fail HEAD:refs/heads/docs/atomic HEAD:refs/heads/invalid-atomic
if git --git-dir="$fixture/remote.git" show-ref --verify --quiet refs/heads/docs/atomic; then
  printf 'FAIL: mixed push published a branch despite validation failure\n' >&2
  exit 1
fi
git tag arbitrary-tag
expect_push pass refs/tags/arbitrary-tag
# Seed a legacy name without the hook, then verify its deletion is allowed.
git -c core.hooksPath=/dev/null push --quiet origin HEAD:refs/heads/legacy-name
expect_push pass :refs/heads/legacy-name
expect_push pass :refs/tags/arbitrary-tag

commit_status=0
git commit --quiet --allow-empty -m 'Forbidden direct commit' >"$fixture/commit.log" 2>&1 || commit_status=$?
[[ $commit_status -ne 0 ]] || { printf 'FAIL: direct commit on main passed\n' >&2; exit 1; }
grep -q 'Direct commits to main are forbidden' "$fixture/commit.log"
git switch --quiet -c develop
commit_status=0
git commit --quiet --allow-empty -m 'Forbidden direct commit' >"$fixture/commit.log" 2>&1 || commit_status=$?
[[ $commit_status -ne 0 ]] || { printf 'FAIL: direct commit on develop passed\n' >&2; exit 1; }
grep -q 'Direct commits to develop are forbidden' "$fixture/commit.log"
git switch --quiet -c chore/allowed-commit
git commit --quiet --allow-empty -m 'Allowed working branch commit'

printf 'Passed: %s branch-name cases, argument validation, and %s local push scenarios.\n' \
  "$name_count" "$push_count"
