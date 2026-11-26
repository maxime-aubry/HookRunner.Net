using HookRunner.Domain;

namespace HookRunner.Application;

/// <summary>Ouvre l'état d'une identité et le conserve pendant toute l'invocation.</summary>
public interface IHookStateStore
{
    /// <summary>Crée l'état absent ou refuse un état illisible, sans le remplacer.</summary>
    /// <param name="stateFilePath">Chemin absolu du fichier partagé.</param>
    /// <param name="identity">Identité à posséder exclusivement jusqu'à libération de la session.</param>
    /// <param name="cancellationToken">Budget d'attente et d'ouverture de l'état.</param>
    /// <returns>Session exclusive donnant le compteur courant.</returns>
    /// <remarks>Les erreurs d'accès ou de format remontent au contexte ; aucune récupération silencieuse.</remarks>
    Task<IHookStateSession> OpenAsync(string stateFilePath, HookIdentity identity, CancellationToken cancellationToken);
}
