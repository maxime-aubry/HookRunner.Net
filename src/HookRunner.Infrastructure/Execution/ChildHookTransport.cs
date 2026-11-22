using System.Text.Json;
using HookRunner.Application;

namespace HookRunner.Infrastructure;

/// <summary>Possède les fichiers temporaires et valide les messages du protocole enfant.</summary>
internal sealed class ChildHookTransport : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("hookrunner-").FullName;

    public string RequestPath => Path.Combine(directory, "request.json");

    public string ResultPath => Path.Combine(directory, "result.json");

    public Task WriteRequestAsync(IsolatedHookRequest request, CancellationToken cancellationToken)
        => File.WriteAllTextAsync(RequestPath, JsonSerializer.Serialize(request), cancellationToken);

    public async Task<HookExecutionResult> ReadResultAsync(int exitCode, CancellationToken cancellationToken)
    {
        if (exitCode != 0 || !File.Exists(ResultPath))
        {
            throw new InvalidDataException($"Hook child terminated unexpectedly (exit {exitCode}).");
        }

        string json = await File.ReadAllTextAsync(ResultPath, cancellationToken).ConfigureAwait(false);
        return ParseResult(json);
    }

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

    public void Dispose() => Directory.Delete(directory, recursive: true);
}
