using HookRunner.Application;
using HookRunner.Infrastructure;

namespace HookRunner.ExecutionTests;

public sealed class InvocationTests
{
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

    [Theory]
    [InlineData("")]
    [InlineData("not JSON")]
    public void CodexRejectsUnreadableInput(string json)
    {
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => CodexEventAdapter.Parse(json));
    }

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
