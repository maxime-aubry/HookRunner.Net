using HookRunner.Application;

namespace HookRunner.ExecutionTests;

/// <summary>Stratégie de test mémorisant ses entrées et permettant de simuler un échec.</summary>
internal sealed class ConsumerHook : HookStrategy
{
    /// <summary>Nom du hook ou du projet cible représenté par ce type.</summary>
    public override string Name => "hook";
    /// <summary>Indique si le hook de test doit renvoyer une tâche en échec.</summary>
    public bool Fails { get; init; }
    /// <summary>Indique si la stratégie de test a effectivement été appelée.</summary>
    public bool Executed { get; private set; }
    /// <summary>Données neutres de l'événement déclencheur, avec le JSON original.</summary>
    public InvocationData? Invocation { get; private set; }
    /// <summary>Options préparées pour cette invocation du hook.</summary>
    public HookOptions? Options { get; private set; }

    /// <summary>Mémorise les données reçues puis simule un succès ou un échec selon Fails.</summary>
    /// <param name="invocation">Données neutres de l'événement déclencheur, avec le JSON original.</param>
    /// <param name="options">Options préparées pour cette invocation du hook.</param>
    /// <param name="cancellationToken">
    /// Token d'annulation transmis aux opérations qui le prennent en charge.
    /// </param>
    /// <returns>Une tâche terminée ou en échec avec le message consumer failure.</returns>
    /// <remarks>
    /// Aucun processus n'est lancé et le token n'est pas observé par ce double déterministe.
    /// </remarks>
    public override Task ExecuteAsync(InvocationData invocation, HookOptions options, CancellationToken cancellationToken)
    {
        Executed = true;
        Invocation = invocation;
        Options = options;
        return Fails ? Task.FromException(new InvalidOperationException("consumer failure")) : Task.CompletedTask;
    }
}
