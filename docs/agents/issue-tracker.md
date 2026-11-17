# Issue tracker: GitHub

Issues and specs live in maxime-aubry/HookRunner.Net.
Use the gh CLI from this repository.

## Conventions

Before the first issue creation in a session, verify the target with
`gh repo view --json nameWithOwner`. The expected repository is
`maxime-aubry/HookRunner.Net`. Resolve any mismatch before publishing.

- Create: `gh issue create --title "..." --body-file <path>`.
  For multiline bodies, write the exact text to a temporary UTF-8 file.
- Read: `gh issue view <number> --json number,title,body,labels,comments`.
- List: `gh issue list --state open --json number,title,body,labels,comments`.
  Apply label/state filters as needed; paginate when completeness matters.
- Comment: `gh issue comment <number> --body-file <path>`.
- Apply a label: `gh issue edit <number> --add-label "<label>"`.
- Remove a label: `gh issue edit <number> --remove-label "<label>"`.
- Close: post the explanation as a comment, then `gh issue close <number>`.
  Comment separately even when the issue is already closed.

## Parent issues and dependencies

- Create a child: `gh issue create --parent <parent> --title "..." --body-file <path>`.
- Link an existing child: `gh issue edit <parent> --add-sub-issue <child>`.
- Add a blocker: `gh issue edit <child> --add-blocked-by <blocker>`.
- Count open blockers:
  `gh api repos/maxime-aubry/HookRunner.Net/issues/<number> --jq .issue_dependencies_summary.blocked_by`.

Use native GitHub relationships when available. Otherwise, put
`Part of #<parent>` and `Blocked by: #<number>` at the top of the child
body, and add the child to a task list in the parent.
A task is unblocked when every blocker is closed.

## Pull requests as a triage surface

**PRs as a request surface: no.**

## Skill operations

When a skill says "publish to the issue tracker", create a GitHub issue.
When it says "fetch the relevant ticket", read the GitHub issue.

## Wayfinding operations

- Map: one issue labelled `wayfinder:map`, with Notes, Decisions-so-far,
  and Fog sections.
- Child tickets: link them to the map as sub-issues, or use the parent
  task-list fallback. Use `wayfinder:<type>` labels:
  research, prototype, grilling, or task.
- Frontier: consider open children in map order; choose the first
  with no open blocker and no assignee.
- Claim: `gh issue edit <number> --add-assignee "@me"`.
- Resolve: comment with the result, close the child, then append a
  summary and issue link to the map's Decisions-so-far.
