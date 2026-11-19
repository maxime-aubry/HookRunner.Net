using HookRunner.Infrastructure;

namespace HookRunner.ExecutionTests;

internal static class ConsumerProgram
{
    public static ChildProcessCommand CopyTo(string directory, bool useAppHost)
    {
        string source = Path.GetDirectoryName(Command.Arguments[0])!;
        Directory.CreateDirectory(directory);
        foreach (string file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(directory, Path.GetFileName(file)), overwrite: true);
        }

        // Run the real supervisor and child with the assemblies under test,
        // including Coverlet instrumentation, instead of build-time copies.
        foreach (string assembly in new[] { "HookRunner.Application.dll", "HookRunner.Infrastructure.dll", "HookRunner.Cli.dll" })
        {
            File.Copy(Path.Combine(AppContext.BaseDirectory, assembly), Path.Combine(directory, assembly), overwrite: true);
        }

        return useAppHost
            ? new ChildProcessCommand(Path.Combine(directory, OperatingSystem.IsWindows() ? "HookRunner.TestHost.exe" : "HookRunner.TestHost"), [])
            : new ChildProcessCommand("dotnet", [Path.Combine(directory, "HookRunner.TestHost.dll")]);
    }

    public static ChildProcessCommand Command
    {
        get
        {
            DirectoryInfo? repository = new(AppContext.BaseDirectory);
            while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "HookRunner.slnx")))
            {
                repository = repository.Parent;
            }

            string configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
            string host = Path.Combine(repository!.FullName, "fixtures", "HookRunner.TestHost", "bin", configuration, "net11.0", "HookRunner.TestHost.dll");
            return new ChildProcessCommand("dotnet", [host]);
        }
    }
}
