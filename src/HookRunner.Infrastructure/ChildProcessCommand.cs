using System.Reflection;

namespace HookRunner.Infrastructure;

public sealed record ChildProcessCommand(string FileName, IReadOnlyList<string> Arguments)
{
    public static ChildProcessCommand ForCurrentProcess()
    {
        string executable = Environment.ProcessPath!;
        string[] arguments = Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            ? [Assembly.GetEntryAssembly()!.Location]
            : [];
        return new ChildProcessCommand(executable, arguments);
    }
}
