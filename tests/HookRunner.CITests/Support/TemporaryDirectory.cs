namespace HookRunner.CITests;

internal sealed class TemporaryDirectory : IDisposable
{
    public string Root { get; } = Directory.CreateTempSubdirectory("hookrunner-ci-tests-").FullName;

    public string Write(string relativePath, string content)
    {
        string path = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    public void Dispose() => Directory.Delete(Root, recursive: true);
}
