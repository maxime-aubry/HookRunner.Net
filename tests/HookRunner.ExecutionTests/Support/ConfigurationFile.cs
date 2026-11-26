namespace HookRunner.ExecutionTests;

/// <summary>Sauvegarde temporaire de la configuration du programme de tests.</summary>
internal sealed class ConfigurationFile : IDisposable
{
    private readonly string path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
    private readonly string? previous;

    /// <summary>
    /// Sauvegarde l'éventuelle configuration du programme de tests puis installe un contenu temporaire.
    /// </summary>
    /// <param name="json">Contenu à écrire dans appsettings.json à côté du programme compilé.</param>
    /// <remarks>
    /// Le fichier existant sera restauré lors de Dispose ; les tests utilisant cette ressource sont
    /// sérialisés.
    /// </remarks>
    public ConfigurationFile(string json)
    {
        previous = File.Exists(path) ? File.ReadAllText(path) : null;
        File.WriteAllText(path, json);
    }

    /// <summary>
    /// Restaure le fichier de configuration précédent, ou supprime le fichier temporaire s'il n'existait pas.
    /// </summary>
    public void Dispose()
    {
        if (previous is null)
        {
            File.Delete(path);
        }
        else
        {
            File.WriteAllText(path, previous);
        }
    }
}
