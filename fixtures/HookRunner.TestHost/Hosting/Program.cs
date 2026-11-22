using HookRunner.Application;
using HookRunner.Cli;
using HookRunner.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace HookRunner.TestHost;

/// <summary>
/// Exécutable consommateur déterministe utilisé par les tests de processus et de packages NuGet.
/// </summary>
internal static class Program
{
    /// <summary>
    /// Configure le mode de simulation, enregistre les hooks du consommateur et appelle l'hôte HookRunner.
    /// </summary>
    /// <param name="args">
    /// Arguments execute, arguments internes enfant, ou préfixe --test-mode avec mode et résultat simulé.
    /// </param>
    /// <returns>
    /// Une tâche donnant le code de l'hôte ; le mode invalid-result écrit son contenu brut puis renvoie 0.
    /// </returns>
    /// <remarks>
    /// Le scan Scrutor est limité à l'assembly contenant InspectHook. Les variables de mode sont héritées
    /// par l'enfant pour reproduire des erreurs de protocole, de composition ou d'exécution sans modèle réel.
    /// </remarks>
    private static async Task<int> Main(string[] args)
    {
        args = ConfigureTestMode(args);
        string? mode = Environment.GetEnvironmentVariable("HOOKRUNNER_TEST_MODE");
        if (args.Length == 3 && args[0] == ChildHookProtocol.Command && mode == "invalid-result")
        {
            await File.WriteAllTextAsync(args[2], Environment.GetEnvironmentVariable("HOOKRUNNER_TEST_RESULT"));
            return 0;
        }

        return await HookRunnerHost.RunAsync(args, services => ConfigureServices(services, mode));
    }

    private static string[] ConfigureTestMode(string[] args)
    {
        if (args.Length >= 3 && args[0] == "--test-mode")
        {
            Environment.SetEnvironmentVariable("HOOKRUNNER_TEST_MODE", args[1]);
            Environment.SetEnvironmentVariable("HOOKRUNNER_TEST_RESULT", args[2]);
            return args[3..];
        }

        return args;
    }

    private static void ConfigureServices(IServiceCollection services, string? mode)
    {
        services.AddSingleton<ConsumerDependency>();
        services.Scan(scan => scan
            .FromAssemblies(typeof(InspectHook).Assembly)
            .AddClasses(classes => classes.AssignableTo<HookStrategy>())
            .As<HookStrategy>()
            .WithTransientLifetime());
        if (mode == "duplicate")
        {
            services.AddTransient<HookStrategy, InspectHook>();
        }
    }
}
