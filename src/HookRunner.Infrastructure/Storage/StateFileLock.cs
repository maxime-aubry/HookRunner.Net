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
            try
            {
                // Le fichier reste en place : le supprimer permettrait de verrouiller deux fichiers distincts.
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException exception) when ((exception.HResult & 0xFFFF) is 11 or 32 or 33)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(20), cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
