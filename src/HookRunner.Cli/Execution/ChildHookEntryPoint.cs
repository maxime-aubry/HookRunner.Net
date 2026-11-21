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
            string requestJson = await File.ReadAllTextAsync(requestPath, cancellationToken).ConfigureAwait(false);
            IsolatedHookRequest request = JsonSerializer.Deserialize<IsolatedHookRequest>(requestJson, RequestJsonOptions)
                ?? throw new InvalidDataException("Invalid hook child request.");
            HookStrategy strategy = HookSelection.Select(provider.GetServices<HookStrategy>(), request.HookName);
            await strategy.ExecuteAsync(request.Invocation, request.Options, cancellationToken).ConfigureAwait(false);
            return new ChildHookResult(ChildHookProtocol.Version, HookExecutionStatus.Success, null);
        }
        catch (Exception exception)
        {
            return new ChildHookResult(ChildHookProtocol.Version, HookExecutionStatus.TechnicalFailure, exception.Message);
        }
    }
}
