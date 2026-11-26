namespace HookRunner.Application;

/// <summary>Accès au compteur d'une invocation ; la libération termine sa possession exclusive.</summary>
public interface IHookStateSession : IAsyncDisposable
{
    /// <summary>Nombre de demandes depuis le dernier succès.</summary>
    int Count { get; }

    /// <summary>Écrit atomiquement le compteur en préservant les autres identités.</summary>
    /// <param name="count">Nouvelle valeur non négative du compteur.</param>
    /// <param name="cancellationToken">Annulation avant validation de l'écriture.</param>
    /// <returns>Une tâche terminée après persistance.</returns>
    Task SaveAsync(int count, CancellationToken cancellationToken);
}
