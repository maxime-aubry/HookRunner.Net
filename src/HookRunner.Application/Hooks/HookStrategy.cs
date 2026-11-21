namespace HookRunner.Application;

/// <summary>Contrat de base des hooks concrets définis et enregistrés par le projet consommateur.</summary>
public abstract class HookStrategy
{
    /// <summary>
    /// Nom obligatoire et unique du hook, comparé sans distinction de casse lors de la sélection.
    /// </summary>
    public abstract string Name { get; }

    /// <summary>Exécute le traitement concret du hook dans le processus enfant.</summary>
    /// <param name="invocation">Données neutres de l'événement déclencheur, avec le JSON original.</param>
    /// <param name="options">Options préparées pour cette invocation du hook.</param>
    /// <param name="cancellationToken">
    /// Token d'annulation transmis aux opérations qui le prennent en charge.
    /// </param>
    /// <returns>Une tâche dont la terminaison normale représente le succès du hook.</returns>
    /// <remarks>
    /// Les services réutilisables sont injectés au constructeur du hook. Les options sont propres à cet appel.
    /// L'implémentation doit transmettre le token à ses opérations annulables ; une exception sera actuellement
    /// traduite en échec technique par le point d'entrée enfant.
    /// </remarks>
    public abstract Task ExecuteAsync(InvocationData invocation, HookOptions options, CancellationToken cancellationToken);
}
