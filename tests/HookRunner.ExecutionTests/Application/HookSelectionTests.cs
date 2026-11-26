using HookRunner.Application;

namespace HookRunner.ExecutionTests;

/// <summary>Contrat de sélection des hooks, observé sans préparation ni exécution.</summary>
public sealed class HookSelectionTests
{
    /// <summary>La recherche renvoie l'instance enregistrée, quelle que soit la casse demandée.</summary>
    /// <param name="requestedName">Nom demandé avec ou sans variation de casse.</param>
    [Theory]
    [InlineData("chosen")]
    [InlineData("CHOSEN")]
    public void SelectReturnsTheRegisteredInstanceIgnoringCase(string requestedName)
    {
        NamedHook chosen = new("chosen");

        HookStrategy selected = HookSelection.Select([new NamedHook("other"), chosen], requestedName);

        Assert.Same(chosen, selected);
    }

    /// <summary>Les espaces font partie du nom enregistré et du nom demandé.</summary>
    [Fact]
    public void SelectPreservesSpacesInHookNames()
    {
        NamedHook chosen = new(" chosen ");

        HookStrategy selected = HookSelection.Select([chosen], " CHOSEN ");

        Assert.Same(chosen, selected);
    }

    /// <summary>La recherche ne supprime les espaces ni de l'enregistrement ni de la demande.</summary>
    /// <param name="registeredName">Nom enregistré, avec ou sans espaces.</param>
    /// <param name="requestedName">Nom demandé différent par ses espaces.</param>
    [Theory]
    [InlineData("chosen", " chosen ")]
    [InlineData(" chosen ", "chosen")]
    public void SelectDoesNotTrimHookNames(string registeredName, string requestedName)
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            HookSelection.Select([new NamedHook(registeredName)], requestedName));

        Assert.Equal($"Unknown hook: '{requestedName}'.", exception.Message);
    }

    /// <summary>Un enregistrement invalide après le hook demandé empêche sa sélection.</summary>
    /// <param name="invalidName">Nom vide, composé d'espaces ou dupliqué sans distinction de casse.</param>
    /// <param name="diagnostic">Diagnostic attendu pour l'enregistrement invalide.</param>
    [Theory]
    [InlineData("", "Every hook must have a non-empty Name.")]
    [InlineData(" ", "Every hook must have a non-empty Name.")]
    [InlineData("chosen", "Duplicate hook Name: 'chosen'.")]
    [InlineData("CHOSEN", "Duplicate hook Name: 'CHOSEN'.")]
    public void InvalidRegistrationAfterTheRequestedHookPreventsSelection(string invalidName, string diagnostic)
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            HookSelection.Select([new NamedHook("chosen"), new NamedHook(invalidName)], "chosen"));

        Assert.Equal(diagnostic, exception.Message);
    }

    /// <summary>Un nom absent des enregistrements produit le diagnostic de hook inconnu.</summary>
    [Fact]
    public void SelectRejectsAnUnknownHook()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            HookSelection.Select([new NamedHook("chosen")], "missing"));

        Assert.Equal("Unknown hook: 'missing'.", exception.Message);
    }

    /// <summary>Une collection vide ne contient aucun hook sélectionnable.</summary>
    [Fact]
    public void SelectRejectsAnEmptyCollection()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            HookSelection.Select([], "chosen"));

        Assert.Equal("Unknown hook: 'chosen'.", exception.Message);
    }

    /// <summary>Une collection null est rejetée avec le nom du paramètre concerné.</summary>
    [Fact]
    public void SelectRejectsANullCollection()
    {
        ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() =>
            HookSelection.Select(null!, "chosen"));

        Assert.Equal("strategies", exception.ParamName);
    }

    /// <summary>Hook nommé qui détecte toute exécution accidentelle pendant la sélection.</summary>
    /// <param name="name">Nom enregistré du hook.</param>
    private sealed class NamedHook(string name) : HookStrategy
    {
        /// <summary>Nom utilisé pour la sélection.</summary>
        public override string Name => name;

        /// <summary>La sélection doit seulement retourner l'instance, sans exécuter le hook.</summary>
        /// <param name="invocation">Données de l'invocation.</param>
        /// <param name="options">Options préparées.</param>
        /// <param name="cancellationToken">Annulation de l'exécution.</param>
        /// <returns>Ne retourne jamais : toute exécution est une erreur de test.</returns>
        public override Task ExecuteAsync(InvocationData invocation, HookOptions options, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Selection must not execute a hook.");
    }
}
