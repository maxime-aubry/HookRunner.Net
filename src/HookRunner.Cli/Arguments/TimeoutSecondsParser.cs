using System.Globalization;

namespace HookRunner.Cli;

/// <summary>Validation partagée des timeouts fournis par la commande ou la configuration.</summary>
internal static class TimeoutSecondsParser
{
    /// <summary>Convertit un timeout composé uniquement de chiffres en un entier strictement positif.</summary>
    /// <param name="value">
    /// Nombre de secondes écrit en culture invariante, sans signe ni séparateur décimal.
    /// </param>
    /// <returns>Le timeout positif représentable par Int32.</returns>
    /// <exception cref="ArgumentException">
    /// La valeur est vide, mal formée, nulle, négative ou hors capacité.
    /// </exception>
    public static int Parse(string value)
    {
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int timeoutSeconds) || timeoutSeconds <= 0)
        {
            throw new ArgumentException("TimeoutSeconds must be a positive integer.");
        }

        return timeoutSeconds;
    }
}
