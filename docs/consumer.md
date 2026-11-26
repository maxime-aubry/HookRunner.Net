# Executing a consumer hook from Codex

Install the stable .NET 11 SDK specified in `global.json`. Your executable
references the `HookRunner.Cli` NuGet package, Scrutor, and
`Microsoft.Extensions.DependencyInjection`. HookRunner's other packages are
transitive dependencies. The repository ships libraries; the executable and
concrete hooks belong to your project.

Use the same entry point for both supervisor and child. For a console project
named `Hooks`, `Program.cs` can contain:

```csharp
using HookRunner.Application;
using HookRunner.Cli;
using Microsoft.Extensions.DependencyInjection;
using MyCompany.Hooks;

return await HookRunnerHost.RunAsync(args, services =>
{
    services.Scan(scan => scan
        .FromAssemblies(typeof(MyHook).Assembly)
        .AddClasses(classes => classes.AssignableTo<HookStrategy>())
        .As<HookStrategy>()
        .WithTransientLifetime());
});
```

Choose the assemblies explicitly; avoid scanning all loaded assemblies or
application dependencies. `MyHook` is your own public, concrete class deriving
from `HookStrategy`, with a nonempty, unique `Name` and this execution contract:

```csharp
public override Task ExecuteAsync(
    InvocationData invocation,
    HookOptions options,
    CancellationToken cancellationToken)
```

Register constructor dependencies in the same callback. It runs in both
processes, so register reproducible services and keep invocation work in
`ExecuteAsync`. Instances and services are never serialized between processes.
Throw `HookRunner.Domain.HookValidationException` to request business corrections. Other unhandled exceptions are technical failures with diagnostic and exit `2`.

Copy your single `appsettings.json` beside the compiled executable:

```json
{
  "HookRunner": {
    "TimeoutSeconds": 120,
    "MaxBlockingRequests": 3,
    "StateFilePath": "Hooks/state.json"
  }
}
```

```xml
<ItemGroup>
  <None Update="appsettings.json" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

The file is loaded from `AppContext.BaseDirectory`, independently of the launch
directory and the solution under inspection. `StateFilePath` is required and
resolved relative to the solution root. Rooted paths and paths that normalize
outside that root are preparation errors. A missing timeout defaults to 120
seconds; `--timeout` overrides it with a positive integer.

`MaxBlockingRequests` defaults to three and accepts non-negative integers.
Override a hook with `HookRunner:Hooks:my_hook:MaxBlockingRequests`; names are
case insensitive. Zero allows the hook to run without emitting business blocks.
`StateFilePath` remains global and cannot be overridden per hook.

The first three correction requests return `2`; subsequent requests return `0`
with a limit trace. The hook still runs at the limit. A normal completion resets
the counter; technical failures and timeouts preserve it. A `0` exit alone never
means business success. Informational events consume requests as well.

Counters persist in one JSON state file, created automatically if absent. The
identity combines the physical solution root, triggering agent, session, and
case insensitive hook name. Events, projects, and timeout do not change it;
separate worktree directories remain distinct. Unreadable or corrupt state
produces a diagnostic and `2` without starting the hook or replacing the state.
Updates use a temporary file in the same directory, flush it, and atomically
rename it. Adjacent `.lock` files contain no counters and remain in place to
provide stable interprocess locks: one per identity for the complete invocation,
and one short lock for merging updates to the shared document. Keep state and
its lock files out of source control. The configuration is never updated.

For a hook whose `Name` is `my_hook`, launch the consumer from source with:

```text
dotnet run --project ./Hooks -- execute my_hook --agent codex
```

The arguments after `--` belong to the consumer. For an already compiled host,
use `dotnet /absolute/path/to/Hooks.dll execute my_hook --agent codex` in your
native Codex hook command. Codex supplies the event object on stdin, including
`session_id`, `hook_event_name`, and an existing absolute `cwd`. See
[Codex hooks](https://learn.chatgpt.com/docs/hooks) for native event wiring.

`InvocationData` exposes agent, event, session, working directory, optional
project information, and the original JSON unchanged. Codex has no common
project field, so `Project` is null. Additional fields remain in `OriginalJson`.

The first ascending directory from the event's `cwd` containing a `.sln` or
`.slnx` is the solution root. With no project selection, `HookOptions.Projects`
is empty and `DefaultWorkingDirectory` is that root. The hook name is selected
case insensitively from all registered strategies; empty, duplicate, and unknown
names are preparation failures. Preparation failures emit stderr and `2`
without executing a hook. A successful invocation emits a trace on stderr
and returns `0`; hook stdout is drained separately from the internal result.

The default child command reopens the current .NET executable or entry assembly.
Hosts needing another launcher can supply a `ChildProcessCommand` to
`HookRunnerHost.RunAsync`, with the executable and its fixed arguments. Both
entry modes must call the same composition callback. HookRunner appends its
internal child arguments; these are separate from native Codex commands.

The global timeout defaults to 120 seconds, is configurable through
`HookRunner:TimeoutSeconds`, and can be overridden with `--timeout <seconds>`.
Only strictly positive integers up to `2147483647` are accepted. One budget
covers waiting for the identity lock, child startup and all hook execution. At the deadline, HookRunner
forcibly stops the child process tree even if the hook ignores cancellation,
then allows up to five seconds to confirm local termination. Confirmed timeout
emits a trace and returns `0`; failed confirmation or external cancellation
emits a diagnostic and returns `2`. Late hook results are ignored. Forced stop
does not guarantee hook cleanup or roll back file changes.

This milestone implements the Codex execution path (#6), global timeout (#7), and persistent correction counters (#8).
The following capabilities remain in their own tickets:
further concurrency scenarios (#9),
project selection (#10), Claude events (#11), local commands (#12), and prompts
(#13–#14). `--projects` is rejected until project selection is implemented.

`fixtures/HookRunner.TestHost` is a deterministic test consumer, not a production
hook library. After a Release build, run
`./scripts/test-consumer-packages.ps1` to pack all four libraries and build an
independent consumer against the resulting local NuGet feed. CI runs this check
on Windows, Linux, and macOS, alongside the normal build, tests, and coverage gate.
