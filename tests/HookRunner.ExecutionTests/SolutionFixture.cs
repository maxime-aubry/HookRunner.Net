using HookRunner.Application;

namespace HookRunner.ExecutionTests;

internal sealed class SolutionFixture : IDisposable
{
    public SolutionFixture()
    {
        Root = Directory.CreateTempSubdirectory("hookrunner-tests-").FullName;
        Directory.CreateDirectory(Path.Combine(Root, "agent", "nested"));
        File.WriteAllText(Path.Combine(Root, "Example.slnx"), "<Solution />");
    }

    public string Root { get; }

    public InvocationData Invocation => new("codex", "Stop", "session-42", null,
        Path.Combine(Root, "agent", "nested"), "{\"opaque\":true}");

    public void Dispose() => Directory.Delete(Root, true);
}
