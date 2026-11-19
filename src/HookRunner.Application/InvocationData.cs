namespace HookRunner.Application;

public sealed record InvocationData(
    string Agent,
    string EventName,
    string SessionId,
    string? Project,
    string WorkingDirectory,
    string OriginalJson);
