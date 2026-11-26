using System.Text.Json;
using HookRunner.Application;
using HookRunner.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace HookRunner.Cli;

/// <summary>Point d'entrée interne exécutant directement une stratégie dans le processus enfant.</summary>
internal static class ChildHookEntryPoint
{
    private static readonly JsonSerializerOptions RequestJsonOptions = new()
    {
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
    };

    /// <summary>Lit les chemins du protocole enfant, exécute le hook et écrit le résultat JSON.</summary>
    /// <param name="args">
    /// Exactement trois arguments : marqueur enfant, chemin de requête et chemin de résultat.
    /// </param>
    /// <param name="provider">Services composés avec les mêmes enregistrements que le superviseur.</param>
    /// <param name="cancellationToken">
    /// Token d'annulation transmis aux opérations qui le prennent en charge.
    /// </param>
    /// <returns>Une tâche donnant 0 une fois le résultat écrit, même si le statut du hook est un échec.</returns>
    /// <remarks>
    /// Les erreurs d'écriture et l'annulation pendant l'écriture remontent à l'hôte, qui produit un diagnostic et
    /// le code 2.
    /// </remarks>
    /// <exception cref="ArgumentException">Le nombre d'arguments est différent de trois.</exception>
    public static async Task<int> RunAsync(string[] args, IServiceProvider provider, CancellationToken cancellationToken)
    {
        if (args.Length != 3)
        {
            throw new ArgumentException("Invalid hook child arguments.");
        }

        string requestPath = args[1];
        string resultPath = args[2];
        ChildHookResult result = await ExecuteHookAsync(requestPath, provider, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(resultPath, JsonSerializer.Serialize(result), cancellationToken).ConfigureAwait(false);
        return 0;
    }

    /// <summary>
    /// Désérialise une invocation préparée, résout la stratégie et l'exécute directement dans l'enfant.
    /// </summary>
    /// <param name="requestPath">
    /// Fichier JSON contenant le nom canonique, l'invocation et les options préparées.
    /// </param>
    /// <param name="provider">Conteneur permettant de résoudre les stratégies du consommateur.</param>
    /// <param name="cancellationToken">
    /// Token d'annulation transmis aux opérations qui le prennent en charge.
    /// </param>
    /// <returns>
    /// Une tâche donnant un résultat versionné de succès ou d'échec technique avec le message de l'exception.
    /// </returns>
    /// <remarks>
    /// Ne lit ni l'entrée native ni la configuration et ne relance ni le cas d'usage ni le contexte. Toute
    /// exception de lecture, sélection ou exécution, annulation incluse, est convertie en résultat technique.
    /// </remarks>
    private static async Task<ChildHookResult> ExecuteHookAsync(
        string requestPath,
        IServiceProvider provider,
        CancellationToken cancellationToken)
    {
        try
        {
            await ExecuteRequestAsync(requestPath, provider, cancellationToken).ConfigureAwait(false);
            return new ChildHookResult(ChildHookProtocol.Version, HookExecutionStatus.Success, null);
        }
        catch (Exception exception)
        {
            HookExecutionResult failure = HookExecutionResult.FromException(exception);
            return new ChildHookResult(ChildHookProtocol.Version, failure.Status, failure.Diagnostic);
        }
    }

    /// <summary>Lit la requête préparée et exécute la stratégie résolue dans le conteneur enfant.</summary>
    /// <param name="requestPath">Chemin du fichier JSON transmis par le superviseur.</param>
    /// <param name="provider">Conteneur des stratégies et dépendances du consommateur.</param>
    /// <param name="cancellationToken">Annulation transmise à la lecture et à la stratégie.</param>
    /// <returns>Une tâche terminée lorsque le traitement du hook est achevé.</returns>
    /// <remarks>Les erreurs de lecture, de sélection et d'exécution remontent à la construction du résultat enfant.</remarks>
    private static async Task ExecuteRequestAsync(string requestPath, IServiceProvider provider, CancellationToken cancellationToken)
    {
        IsolatedHookRequest request = await ReadRequestAsync(requestPath, cancellationToken).ConfigureAwait(false);
        HookStrategy strategy = HookSelection.Select(provider.GetServices<HookStrategy>(), request.HookName);
        await strategy.ExecuteAsync(request.Invocation, request.Options, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Désérialise la requête en exigeant les paramètres obligatoires et les valeurs non nullables.</summary>
    /// <param name="requestPath">Chemin du fichier JSON de l'invocation préparée.</param>
    /// <param name="cancellationToken">Annulation de la lecture du fichier.</param>
    /// <returns>Une tâche donnant la requête désérialisée sans recalculer ses options.</returns>
    /// <exception cref="InvalidDataException">Le document JSON représente une valeur null.</exception>
    /// <exception cref="JsonException">Le JSON est mal formé ou ne respecte pas le contrat de la requête.</exception>
    private static async Task<IsolatedHookRequest> ReadRequestAsync(string requestPath, CancellationToken cancellationToken)
    {
        string json = await File.ReadAllTextAsync(requestPath, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<IsolatedHookRequest>(json, RequestJsonOptions)
            ?? throw new InvalidDataException("Invalid hook child request.");
    }
}
