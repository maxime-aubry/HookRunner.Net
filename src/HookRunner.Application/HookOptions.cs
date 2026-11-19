namespace HookRunner.Application;

public sealed record TargetProject(string Name, string ProjectFilePath, string DirectoryPath);

public sealed record HookOptions(
    int TimeoutSeconds,
    string SolutionRoot,
    string DefaultWorkingDirectory,
    IReadOnlyList<TargetProject> Projects,
    string StateFilePath);

public interface IHookOptionsPreparer
{
    HookOptions Prepare(RunHookArgs args, InvocationData invocation);
}
