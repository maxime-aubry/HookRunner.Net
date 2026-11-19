using System.Text.Json;
using HookRunner.Application;

namespace HookRunner.Infrastructure;

public static class CodexEventAdapter
{
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
