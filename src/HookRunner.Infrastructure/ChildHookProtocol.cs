using System.Text.Json.Serialization;
using HookRunner.Application;

namespace HookRunner.Infrastructure;

public sealed record ChildHookResult(int Version, [property: JsonRequired] HookExecutionStatus Status, string? Diagnostic);

public static class ChildHookProtocol
{
    public const string Command = "--hookrunner-child";
    public const int Version = 1;
}
