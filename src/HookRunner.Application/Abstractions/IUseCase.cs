namespace HookRunner.Application;

/// <summary>Contrat commun d'un cas d'usage recevant une requête et produisant une réponse typées.</summary>
/// <typeparam name="TRequest">Type de la requête acceptée.</typeparam>
/// <typeparam name="TResponse">Type de la réponse produite.</typeparam>
public interface IUseCase<in TRequest, TResponse>
    where TRequest : IRequest
    where TResponse : IResponse
{
    /// <summary>Traite une requête applicative typée et construit sa réponse.</summary>
    /// <param name="request">Requête contenant les données nécessaires au cas d'usage.</param>
    /// <param name="cancellationToken">
    /// Token d'annulation transmis aux opérations qui le prennent en charge.
    /// </param>
    /// <returns>Une tâche donnant la réponse du cas d'usage.</returns>
    /// <remarks>Chaque implémentation définit ses validations et la propagation de ses erreurs.</remarks>
    Task<TResponse> ExecuteAsync(TRequest request, CancellationToken cancellationToken);
}
