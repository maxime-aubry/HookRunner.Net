using System.Text.Json;
using HookRunner.Application;

namespace HookRunner.Infrastructure;

/// <summary>Adaptateur du protocole JSON natif Codex vers les données applicatives neutres.</summary>
public static class CodexEventAdapter
{
    /// <summary>Valide l'événement JSON Codex et le transforme en données neutres d'invocation.</summary>
    /// <param name="json">
    /// Texte natif complet reçu sur stdin ; conservé sans modification dans OriginalJson.
    /// </param>
    /// <returns>Une invocation codex avec événement, session, dossier d'origine et projet à null.</returns>
    /// <exception cref="System.Text.Json.JsonException">L'entrée n'est pas un document JSON lisible.</exception>
    /// <exception cref="InvalidDataException">
    /// session_id, hook_event_name ou cwd n'est pas une chaîne exploitable, ou cwd n'est pas un dossier absolu
    /// existant.
    /// </exception>
    public static InvocationData Parse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        string session = RequiredString(root, "session_id");
        string eventName = RequiredString(root, "hook_event_name");
        string directory = RequiredString(root, "cwd");
        if (!Path.IsPathFullyQualified(directory) || !Directory.Exists(directory))
        {
            throw new InvalidDataException("Codex cwd must be an existing absolute directory.");
        }

        return new InvocationData("codex", eventName, session, null, directory, json);
    }

    /// <summary>
    /// Extrait une propriété obligatoire sous forme de chaîne non vide et non composée uniquement d'espaces.
    /// </summary>
    /// <param name="root">Objet JSON racine de l'événement.</param>
    /// <param name="name">Nom exact de la propriété Codex attendue.</param>
    /// <returns>La chaîne d'origine sans normalisation.</returns>
    /// <exception cref="InvalidDataException">
    /// La racine n'est pas un objet, la propriété manque ou sa valeur n'est pas une chaîne exploitable.
    /// </exception>
    private static string RequiredString(JsonElement root, string name)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty(name, out JsonElement value)
            || value.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new InvalidDataException($"Codex {name} must be a non-empty string.");
        }

        return value.GetString()!;
    }
}
