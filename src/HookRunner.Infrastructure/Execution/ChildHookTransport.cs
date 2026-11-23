using System.Text.Json;
using HookRunner.Application;

namespace HookRunner.Infrastructure;

/// <summary>Possède les fichiers temporaires et valide les messages du protocole enfant.</summary>
internal sealed class ChildHookTransport : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("hookrunner-").FullName;

    public string RequestPath => Path.Combine(directory, "request.json");

    public string ResultPath => Path.Combine(directory, "result.json");

    /// <summary>Sérialise l'invocation préparée dans le fichier de requête temporaire.</summary>
    /// <param name="request">Nom du hook, données d'invocation et options destinés à l'enfant.</param>
    /// <param name="cancellationToken">Annulation de l'écriture du fichier.</param>
    /// <returns>Une tâche terminée lorsque la requête JSON est écrite.</returns>
    public Task WriteRequestAsync(IsolatedHookRequest request, CancellationToken cancellationToken)
        => File.WriteAllTextAsync(RequestPath, JsonSerializer.Serialize(request), cancellationToken);

    /// <summary>Vérifie la sortie normale de l'enfant puis lit et valide son résultat JSON.</summary>
    /// <param name="exitCode">Code de sortie du processus enfant terminé.</param>
    /// <param name="cancellationToken">Annulation de la lecture du fichier de résultat.</param>
    /// <returns>Une tâche donnant le résultat applicatif indépendant du protocole enfant.</returns>
    /// <exception cref="InvalidDataException">La sortie est anormale, le résultat manque ou son contrat est invalide.</exception>
    /// <exception cref="JsonException">Le résultat ne peut pas être désérialisé.</exception>
    public async Task<HookExecutionResult> ReadResultAsync(int exitCode, CancellationToken cancellationToken)
    {
        if (exitCode != 0 || !File.Exists(ResultPath))
        {
            throw new InvalidDataException($"Hook child terminated unexpectedly (exit {exitCode}).");
        }

        string json = await File.ReadAllTextAsync(ResultPath, cancellationToken).ConfigureAwait(false);
        return ParseResult(json);
    }

    /// <summary>Valide la version, le statut et le diagnostic de l'enveloppe avant d'en extraire le résultat.</summary>
    /// <param name="json">Contenu du fichier de résultat produit par l'enfant.</param>
    /// <returns>Le statut et le diagnostic reçus, sans l'enveloppe de transport.</returns>
    /// <exception cref="InvalidDataException">L'enveloppe est null, sa version ou son statut est inconnu, ou un échec n'a pas de diagnostic.</exception>
    /// <exception cref="JsonException">Le JSON est illisible ou le champ obligatoire Status est absent.</exception>
    private static HookExecutionResult ParseResult(string json)
    {
        ChildHookResult? result = JsonSerializer.Deserialize<ChildHookResult>(json);
        if (result is null || result.Version != ChildHookProtocol.Version
            || !Enum.IsDefined(result.Status)
            || (result.Status != HookExecutionStatus.Success && string.IsNullOrWhiteSpace(result.Diagnostic)))
        {
            throw new InvalidDataException("Invalid hook child result.");
        }

        return new HookExecutionResult(result.Status, result.Diagnostic);
    }

    /// <summary>Supprime le dossier temporaire et tous les fichiers du transport.</summary>
    /// <remarks>Les erreurs du système de fichiers se propagent à l'appelant.</remarks>
    public void Dispose() => Directory.Delete(directory, recursive: true);
}
