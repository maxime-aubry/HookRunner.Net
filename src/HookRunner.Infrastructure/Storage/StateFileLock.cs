namespace HookRunner.Infrastructure;

/// <summary>Verrou de fichier interprocessus, libéré automatiquement à la fin du processus.</summary>
internal static class StateFileLock
{
    /// <summary>Attend exclusivement un fichier de verrouillage, en respectant le budget global.</summary>
    /// <param name="path">Chemin stable du verrou à ouvrir sans partage.</param>
    /// <param name="cancellationToken">Annulation de l'attente entre tentatives.</param>
    /// <returns>Handle à conserver jusqu'à la fin de la section protégée.</returns>
    /// <remarks>Seules les erreurs de partage sont retentées ; les autres erreurs d'accès remontent.</remarks>
    public static Task<FileStream> AcquireAsync(string path, CancellationToken cancellationToken)
        => AcquireAsync(path,
            lockPath => new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None), cancellationToken);

    /// <summary>Attend le verrou en permettant de rejouer les erreurs natives de l'ouverture.</summary>
    /// <param name="path">Chemin stable du verrou.</param>
    /// <param name="open">Ouverture exclusive du fichier, ou simulation de cette frontière native.</param>
    /// <param name="cancellationToken">Annulation du budget d'attente.</param>
    /// <returns>Handle acquis dont l'appelant devient propriétaire.</returns>
    internal static async Task<FileStream> AcquireAsync(string path, Func<string, FileStream> open, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FileStream? handle = TryAcquire(path, open);
            if (handle is not null)
            {
                return handle;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20), cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Tente une acquisition et traduit uniquement un conflit de partage en absence de verrou.</summary>
    /// <param name="path">Fichier stable à ouvrir exclusivement.</param>
    /// <param name="open">Ouverture exclusive à tenter.</param>
    /// <returns>Le handle acquis, ou null si un autre processus conserve le verrou.</returns>
    /// <remarks>Les autres erreurs d'accès remontent ; l'appelant décide de l'attente et de son annulation.</remarks>
    private static FileStream? TryAcquire(string path, Func<string, FileStream> open)
    {
        try
        {
            // Le fichier reste en place : le supprimer permettrait de verrouiller deux fichiers distincts.
            return open(path);
        }
        // EWOULDBLOCK vaut 11 sous Linux et 35 sous macOS ; Windows utilise 32 ou 33.
        catch (IOException exception) when ((exception.HResult & 0xFFFF) is 11 or 32 or 33 or 35)
        {
            return null;
        }
    }
}
