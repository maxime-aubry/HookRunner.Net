using HookRunner.Application;
using HookRunner.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace HookRunner.Cli;

/// <summary>
/// API d'hébergement appelée par l'exécutable consommateur dans les modes superviseur et enfant.
/// </summary>
public static class HookRunnerHost
{
    /// <summary>
    /// Compose les services du consommateur et route l'appel vers le superviseur ou le point d'entrée enfant.
    /// </summary>
    /// <param name="args">Arguments applicatifs execute ou arguments du protocole interne enfant.</param>
    /// <param name="configureServices">
    /// Enregistre les stratégies et services du consommateur ; le callback est réutilisé dans chaque processus.
    /// </param>
    /// <param name="childProcess">
    /// Commande de relancement du consommateur ; null utilise le processus courant et son assembly d'entrée.
    /// </param>
    /// <param name="cancellationToken">
    /// Token d'annulation transmis aux opérations qui le prennent en charge.
    /// </param>
    /// <returns>
    /// Une tâche donnant le code du superviseur ou du transport enfant ; toute exception est diagnostiquée sur
    /// stderr et donne 2.
    /// </returns>
    /// <remarks>
    /// Le conteneur est libéré de manière asynchrone après le traitement. Une erreur de composition, préparation,
    /// exécution ou libération est interceptée à cette frontière. Dans l'enfant, le statut réel du hook est écrit
    /// dans le fichier de résultat.
    /// </remarks>
    public static async Task<int> RunAsync(
        string[] args,
        Action<IServiceCollection> configureServices,
        ChildProcessCommand? childProcess = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await RunComposedAsync(args, configureServices, childProcess, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"HookRunner: {exception.Message}");
            return 2;
        }
    }

    /// <summary>Valide les entrées et possède le conteneur pendant toute l'exécution.</summary>
    /// <param name="args">Commande utilisateur ou arguments du protocole enfant.</param>
    /// <param name="configureServices">Enregistrements propres au consommateur.</param>
    /// <param name="childProcess">Commande de relancement ; null utilise le processus courant.</param>
    /// <param name="cancellationToken">Annulation transmise au point d'entrée choisi.</param>
    /// <returns>Une tâche donnant le code de sortie après libération asynchrone du conteneur.</returns>
    /// <exception cref="ArgumentNullException">Les arguments ou le callback d'enregistrement sont null.</exception>
    /// <remarks>Les erreurs de traitement ou de libération remontent à la frontière publique de l'hôte.</remarks>
    private static async Task<int> RunComposedAsync(
        string[] args,
        Action<IServiceCollection> configureServices,
        ChildProcessCommand? childProcess,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(configureServices);
        await using ServiceProvider provider = BuildServiceProvider(configureServices, childProcess);
        return await RunEntryPointAsync(args, provider, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Choisit le point d'entrée du superviseur ou de l'enfant selon le premier argument.</summary>
    /// <param name="args">Commande utilisateur ou arguments du protocole enfant.</param>
    /// <param name="provider">Conteneur appartenant à l'appelant et partagé avec le point d'entrée choisi.</param>
    /// <param name="cancellationToken">Annulation transmise au point d'entrée choisi.</param>
    /// <returns>Une tâche donnant le code de sortie du point d'entrée choisi.</returns>
    /// <remarks>Les erreurs se propagent à la frontière publique sans libérer le conteneur ici.</remarks>
    private static async Task<int> RunEntryPointAsync(
        string[] args,
        IServiceProvider provider,
        CancellationToken cancellationToken)
    {
        if (args.Length == 0 || args[0] != ChildHookProtocol.Command)
        {
            return await RunSupervisorAsync(args, provider, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            return await ChildHookEntryPoint.RunAsync(args, provider, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Enregistre l'orchestration, l'exécuteur isolé et les services fournis par le consommateur.
    /// </summary>
    /// <param name="configureServices">
    /// Callback exécuté après les enregistrements par défaut pour permettre leur remplacement.
    /// </param>
    /// <param name="childProcess">Commande enfant facultative ; l'absence utilise le processus courant.</param>
    /// <returns>Un nouveau conteneur dont l'appelant doit assurer la libération asynchrone.</returns>
    /// <remarks>
    /// La configuration est chargée seulement lors de la résolution du préparateur d'options. Le mode enfant peut
    /// ainsi fonctionner sans relire appsettings.json.
    /// </remarks>
    private static ServiceProvider BuildServiceProvider(
        Action<IServiceCollection> configureServices,
        ChildProcessCommand? childProcess)
    {
        ServiceCollection services = new();
        services.AddSingleton<IIsolatedHookExecutor>(new ProcessHookExecutor(childProcess ?? ChildProcessCommand.ForCurrentProcess()));
        services.AddSingleton<IHookStateStore, FileHookStateStore>();
        services.AddTransient(provider => new Context(provider.GetRequiredService<IIsolatedHookExecutor>(), Console.Error,
            provider.GetRequiredService<IHookStateStore>()));
        services.AddTransient<RunHookUseCase>();
        services.AddSingleton<IHookOptionsPreparer>(_ =>
        {
            HookRunnerConfiguration configuration = HookRunnerConfigurationLoader.Load(AppContext.BaseDirectory);
            return new HookOptionsPreparer(configuration.TimeoutSeconds, configuration.StateFilePath,
                configuration.MaxBlockingRequests, configuration.HookLimits);
        });
        configureServices(services);
        return services.BuildServiceProvider();
    }

    /// <summary>Orchestre la préparation de la requête et l'exécution du cas d'usage du superviseur.</summary>
    /// <param name="args">Arguments de la commande execute.</param>
    /// <param name="provider">Services du superviseur permettant de résoudre le cas d'usage.</param>
    /// <param name="cancellationToken">
    /// Token d'annulation transmis aux opérations qui le prennent en charge.
    /// </param>
    /// <returns>Une tâche donnant le code de sortie de la réponse applicative.</returns>
    /// <remarks>
    /// Les erreurs d'arguments, d'entrée native ou de préparation se propagent à l'hôte avant toute exécution du
    /// hook.
    /// </remarks>
    private static async Task<int> RunSupervisorAsync(string[] args, IServiceProvider provider, CancellationToken cancellationToken)
    {
        RunHookRequest request = await ReadRunHookRequestAsync(args, cancellationToken).ConfigureAwait(false);
        RunHookUseCase useCase = provider.GetRequiredService<RunHookUseCase>();
        RunHookResponse response = await useCase.ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
        return response.ExitCode;
    }

    /// <summary>Construit la requête à partir de la commande et de l'événement Codex lu sur stdin.</summary>
    /// <param name="args">Arguments de la commande execute à analyser avant de lire stdin.</param>
    /// <param name="cancellationToken">Annulation transmise à la lecture de stdin.</param>
    /// <returns>Une tâche donnant la requête contenant les arguments et les données d'invocation.</returns>
    /// <remarks>Consomme stdin ; les erreurs d'arguments, de lecture ou d'événement se propagent à l'hôte.</remarks>
    private static async Task<RunHookRequest> ReadRunHookRequestAsync(string[] args, CancellationToken cancellationToken)
    {
        RunHookArgs parsedArguments = RunHookArgumentsParser.Parse(args);
        string eventJson = await Console.In.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        InvocationData invocation = CodexEventAdapter.Parse(eventJson);
        return new RunHookRequest(parsedArguments, invocation);
    }
}
