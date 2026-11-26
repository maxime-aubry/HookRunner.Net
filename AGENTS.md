## Agent skills

### Issue tracker

Issues and specs live in GitHub Issues. Before tracker operations,
read `docs/agents/issue-tracker.md`.

### Triage labels

Use the five canonical triage labels. Before triaging,
read `docs/agents/triage-labels.md`.

### Domain docs

Single-context layout. Before exploring the codebase,
read `docs/agents/domain.md`.

## Coding style

Before changing or reviewing C# code, read [CODING_STANDARDS.md](CODING_STANDARDS.md)
for method responsibilities, exception handling, and the automated structure check.

Use explicit types for all C# variable declarations, including `foreach`
and `using` declarations. The `var` keyword is forbidden.

## Coding principles

- **SOLID:** Give each class one reason to change; extend behavior through
  composition and focused contracts. Preserve substitutability, keep interfaces
  specific to their consumers, and depend on abstractions at architectural
  boundaries.
- **Design patterns:** Prefer established patterns wherever they fit a concrete
  design problem. Choose the simplest applicable pattern and explain the problem
  it solves when introducing it; keep its abstractions proportional to current
  needs.
- **Clean Code:** Use domain vocabulary and intention-revealing names. Keep
  methods focused, control flow readable, and side effects explicit. Consolidate
  duplicated business rules and use comments to explain intent or constraints.
- **Clean Architecture:** Keep business rules and use cases independent of
  infrastructure and delivery mechanisms. Direct dependencies inward, define
  ports in the layer that consumes them, and implement adapters in outer layers.
  Wire concrete implementations at the composition root and respect existing
  architecture decisions.
