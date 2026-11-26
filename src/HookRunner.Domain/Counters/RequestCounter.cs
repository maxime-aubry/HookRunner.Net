namespace HookRunner.Domain;

/// <summary>Compte les demandes de correction depuis le dernier succès métier.</summary>
public sealed class RequestCounter
{
    /// <summary>Restaure un compteur persistant non négatif.</summary>
    /// <param name="count">Nombre de demandes déjà consommées.</param>
    /// <exception cref="ArgumentOutOfRangeException">Le compteur est négatif.</exception>
    public RequestCounter(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        Count = count;
    }

    /// <summary>Nombre de demandes déjà consommées.</summary>
    public int Count { get; private set; }

    /// <summary>Consomme une demande sous le plafond et indique si elle doit bloquer.</summary>
    /// <param name="limit">Plafond configuré, zéro désactivant les demandes bloquantes.</param>
    /// <returns>True si la demande consomme une unité ; false si le plafond est déjà atteint.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Le plafond est négatif.</exception>
    public bool RequestCorrection(int limit)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(limit);
        if (Count >= limit)
        {
            return false;
        }

        Count++;
        return true;
    }
}
