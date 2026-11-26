namespace HookRunner.Domain;

/// <summary>Identité stable d'un compteur, indépendante des événements et options.</summary>
/// <param name="SolutionRoot">Racine de solution à canonicaliser par le stockage.</param>
/// <param name="Agent">Agent déclencheur.</param>
/// <param name="SessionId">Session de l'agent.</param>
/// <param name="HookName">Nom du hook, comparé sans distinction de casse.</param>
public sealed record HookIdentity(string SolutionRoot, string Agent, string SessionId, string HookName)
{
    /// <summary>Normalise le nom du hook sans modifier la racine physique fournie par l'infrastructure.</summary>
    /// <returns>Identité au nom de hook insensible à la casse.</returns>
    public HookIdentity Normalize() => this with
    {
        HookName = HookName.ToUpperInvariant(),
    };
}
