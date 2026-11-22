using HookRunner.Infrastructure;

namespace HookRunner.ExecutionTests;

/// <summary>Utilitaires de localisation et de copie du programme consommateur de test.</summary>
internal static class ConsumerProgram
{
    /// <summary>Copie le consommateur compilé et les assemblies sous test dans un dossier isolé.</summary>
    /// <param name="directory">
    /// Dossier de destination créé si nécessaire ; les fichiers existants sont remplacés.
    /// </param>
    /// <param name="useAppHost">
    /// True pour appeler l'apphost natif ; false pour utiliser dotnet avec le DLL.
    /// </param>
    /// <returns>La commande de lancement du consommateur copié.</returns>
    /// <remarks>
    /// Les assemblies HookRunner du dossier de tests remplacent celles de compilation afin de préserver
    /// l'instrumentation Coverlet.
    /// </remarks>
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

    /// <summary>Commande dotnet du consommateur compilé, localisé depuis la racine du dépôt.</summary>
    public static ChildProcessCommand Command
    {
        get
        {
            string repository = RepositoryRoot();
            string configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
            string framework = new DirectoryInfo(AppContext.BaseDirectory).Name;
            string host = Path.Combine(repository, "fixtures", "HookRunner.TestHost", "bin", configuration, framework, "HookRunner.TestHost.dll");
            return new ChildProcessCommand("dotnet", [host]);
        }
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "HookRunner.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Cannot locate HookRunner.slnx");
    }
}
