using HookRunner.Application;
using HookRunner.Infrastructure;

namespace HookRunner.ExecutionTests;

/// <summary>Scénarios de validation et de conservation des événements natifs Codex.</summary>
public sealed class InvocationTests
{
    /// <summary>Vérifie le refus des champs natifs absents, mal typés, vides ou d'un dossier non absolu.</summary>
    /// <param name="json">Document JSON invalide du scénario.</param>
    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{\"session_id\":null}")]
    [InlineData("{\"session_id\":42}")]
    [InlineData("{\"session_id\":\" \"}")]
    [InlineData("{\"session_id\":\"s\",\"hook_event_name\":\"Stop\",\"cwd\":\"relative\"}")]
    public void CodexRejectsUnusableSessionOrWorkingDirectory(string json)
    {
        Assert.Throws<InvalidDataException>(() => CodexEventAdapter.Parse(json));
    }

    /// <summary>
    /// Vérifie qu'une entrée ne formant pas un document JSON provoque une erreur de désérialisation.
    /// </summary>
    /// <param name="json">Texte vide ou non JSON fourni à l'adaptateur.</param>
    [Theory]
    [InlineData("")]
    [InlineData("not JSON")]
    public void CodexRejectsUnreadableInput(string json)
    {
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => CodexEventAdapter.Parse(json));
    }

    /// <summary>
    /// Vérifie la conversion en invocation neutre et la conservation exacte du JSON, champs opaques inclus.
    /// </summary>
    [Fact]
    public void CodexPreservesTheOriginalEventInNeutralData()
    {
        string directory = Path.GetTempPath();
        string json = System.Text.Json.JsonSerializer.Serialize(new
        {
            session_id = "session-42",
            hook_event_name = "Stop",
            cwd = directory,
            extra = "opaque",
        });

        InvocationData invocation = CodexEventAdapter.Parse(json);

        Assert.Equal("session-42", invocation.SessionId);
        Assert.Equal("Stop", invocation.EventName);
        Assert.Equal(directory, invocation.WorkingDirectory);
        Assert.Equal(json, invocation.OriginalJson);
        Assert.Equal("codex", invocation.Agent);
        Assert.Null(invocation.Project);
    }
}
