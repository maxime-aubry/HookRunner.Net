namespace HookRunner.Infrastructure;

/// <summary>Sélectionne le renommage atomique qui conserve les lecteurs du document remplacé.</summary>
internal static class StateFilePublisher
{
    private static readonly Action<string, string> PublishFile = Select(OperatingSystem.IsWindows());

    /// <summary>Publie un document complet et fermé sur une destination du même volume.</summary>
    /// <param name="temporary">Fichier adjacent à renommer.</param>
    /// <param name="path">Destination protégée par le verrou de document.</param>
    public static void Publish(string temporary, string path) => PublishFile(temporary, path);

    /// <summary>Choisit l'adaptateur du système ; la sélection seule ne lance aucun appel natif.</summary>
    /// <param name="windows">True sélectionne les sémantiques de renommage POSIX de Windows.</param>
    /// <returns>L'opération de publication adaptée au système hôte.</returns>
    internal static Action<string, string> Select(bool windows)
    {
        if (windows)
        {
            return new WindowsStateFilePublisher().Publish;
        }

        return (temporary, path) => File.Move(temporary, path, overwrite: true);
    }
}
