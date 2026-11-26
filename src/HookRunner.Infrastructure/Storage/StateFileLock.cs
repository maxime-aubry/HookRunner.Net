namespace HookRunner.Infrastructure;

/// <summary>Verrou de fichier interprocessus, libéré automatiquement à la fin du processus.</summary>
internal static class StateFileLock
{
    /// <summary>Attend exclusivement un fichier de verrouillage, en respectant le budget global.</summary>
    /// <param name="path">Chemin stable du verrou à ouvrir sans partage.</param>
    /// <param name="cancellationToken">Annulation de l'attente entre tentatives.</param>
    /// <returns>Handle à conserver jusqu'à la fin de la section protégée.</returns>
    /// <remarks>Seules les erreurs de partage sont retentées ; les autres erreurs d'accès remontent.</remarks>
    public static async Task<FileStream> AcquireAsync(string path, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FileStream? handle = TryAcquire(path);
            if (handle is not null)
            {
                return handle;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20), cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Tente une acquisition et traduit uniquement un conflit de partage en absence de verrou.</summary>
    /// <param name="path">Fichier stable à ouvrir exclusivement.</param>
    /// <returns>Le handle acquis, ou null si un autre processus conserve le verrou.</returns>
    /// <remarks>Les autres erreurs d'accès remontent ; l'appelant décide de l'attente et de son annulation.</remarks>
    private static FileStream? TryAcquire(string path)
    {
        try
        {
            // Le fichier reste en place : le supprimer permettrait de verrouiller deux fichiers distincts.
            return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException exception) when ((exception.HResult & 0xFFFF) is 11 or 32 or 33)
        {
            return null;
        }
    }
}
