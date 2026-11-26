# Coding standards

## Method responsibilities

Each method has one responsibility that can be described by an intention-revealing
name. Keep statements at a consistent level of abstraction: an orchestration method
names its steps, while focused helpers implement their details. Extract independent
validation, resource ownership, persistence, or error translation when they obscure
the method's purpose.

During review, assess responsibilities and control flow rather than imposing an
arbitrary line limit. Length, deep nesting, and several unrelated phases are signals
to inspect, not automatic violations. Report the responsibilities being mixed and
the useful extraction; avoid splitting cohesive code into forwarding methods.

## Control flow and debugging

Prefer explicit `if`/`else` branches over ternary conditional expressions (`?:`).
Put branch-specific operations on separate statements so a debugger can stop on
each branch and intermediate values can be inspected. Apply this especially to
nested conditions, method calls, and expressions that combine several operations.

Reserve ternaries for simple value selections where separate branches would not
meaningfully improve breakpoint placement or inspection. During review, assess
debuggability rather than brevity; this preference is a judgement call, not a
blanket syntax ban.

## Exception handling

Catch an exception only to recover from it, add useful context, or translate it at
an application or infrastructure boundary. Let other exceptions propagate to the
boundary that owns their handling. A general catch is appropriate for boundary
translation or cleanup followed by rethrow; justify that responsibility explicitly.

Every catch must preserve an observable outcome: a recovered operation, a meaningful
failure result or diagnostic, or a propagated exception. Empty catches and success
defaults that conceal an unhandled failure violate this standard. Logging alone
does not make continuing with an incorrect success result acceptable. Preserve the
original stack when rethrowing; retain the original exception when adding context.

Distinguish expected recovery from a hidden failure. Creating an absent state file,
retrying a recognized sharing conflict, and translating a confirmed timeout into
its documented result are explicit recovery policies. Review their exception type,
filter, cancellation behavior, and returned outcome. Preserve resource lifetimes
and which operations a handler covers when extracting methods.

## Automated try/catch structure

`HR0001`, implemented by
[TryCatchCheck](tools/HookRunner.CI/Quality/Code/TryCatchCheck.cs), requires a
try statement, including its catch and finally clauses, to be the sole statement
in its function body. Multiple catches, filters, and finally blocks are allowed.
This applies equally to try/catch, try/finally, and try/catch/finally.
Extract preparation, loops, and
subsequent work into appropriately named functions instead of placing them outside
the interception or widening its scope indiscriminately.

The check covers C# methods, constructors, operators, accessors, local functions,
and block-bodied lambdas or anonymous methods. Nested functions are checked
independently. A top-level try/catch/finally must be extracted into a function.

The check parses C# syntax rather than matching text, so comments and strings do
not count as code. It scans repository C# files outside `bin`, `obj`, `artifacts`,
`.git`, and `.nuget`, and does not follow directory links. Syntax errors fail the
check. It does not prove single responsibility, appropriate recovery, or absence
of silently concealed failures; reviewers must apply the criteria above.
Parsing covers no predefined symbols, `TRACE` (Release), and `DEBUG` plus `TRACE`
(Debug). Code enabled only by other project-specific symbols needs review or an
extension of the check's configurations when those symbols are introduced.

Run the check through `scripts/validate.ps1 -Task Quality -Locked`, also used by
the CI Quality job. See [CI documentation](docs/ci.md) for execution and diagnostics.
