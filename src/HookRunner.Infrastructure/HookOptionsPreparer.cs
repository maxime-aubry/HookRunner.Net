using HookRunner.Application;

namespace HookRunner.Infrastructure;

public sealed class HookOptionsPreparer(int defaultTimeoutSeconds, string stateFilePath) : IHookOptionsPreparer
{
    public HookOptions Prepare(RunHookArgs args, InvocationData invocation)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(invocation);
        int timeout = args.TimeoutSeconds ?? defaultTimeoutSeconds;
        if (timeout <= 0)
        {
            throw new InvalidDataException("TimeoutSeconds must be a positive integer.");
        }

        if (string.IsNullOrWhiteSpace(stateFilePath))
        {
            throw new InvalidDataException("HookRunner:StateFilePath is required.");
        }

        if (args.Projects.Count != 0)
        {
            throw new NotSupportedException("Project selection is not available yet.");
        }

        DirectoryInfo? directory = new(invocation.WorkingDirectory);
        while (directory is not null)
        {
            if (directory.EnumerateFiles().Any(file =>
                file.Extension.Equals(".sln", StringComparison.OrdinalIgnoreCase)
                || file.Extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase)))
            {
                string root = directory.FullName;
                return new HookOptions(timeout, root, root, [], Path.GetFullPath(stateFilePath, root));
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("No solution found above the agent working directory.");
    }
}
