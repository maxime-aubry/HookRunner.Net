# Promote changes through develop with merge commits

Changes enter develop through pull requests from working branches, including explicitly permitted Dependabot branches. Only the develop branch from this repository may be proposed to main; direct commits and direct pushes to either main or develop are forbidden. Both kinds of pull request use merge commits, preserving their commit ancestry instead of using squash or rebase merges, and require an up-to-date source branch and successful required checks.

Use develop as the repository's default branch so that Dependabot security update pull requests follow the same route as other changes, rather than targeting main directly. This default also means the repository opens on development work rather than the last promotion. These are accepted configuration requirements, not a claim that the corresponding GitHub settings are already active.

After each promotion to main, manually create a chore/sync-main working branch from develop, integrate main into that branch, and merge it into develop through a pull request. This returns the promotion's merge commit to develop so that the next promotion can satisfy the up-to-date branch requirement without allowing direct updates to either protected branch. The initial scaffold documents this procedure rather than automating repository writes.
