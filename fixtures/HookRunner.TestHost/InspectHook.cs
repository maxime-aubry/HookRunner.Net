using System.Text.Json;
using HookRunner.Application;

namespace HookRunner.TestHost;

public sealed class ConsumerDependency
{
    public string Value { get; } = "consumer-service";
}

public sealed class InspectHook(ConsumerDependency dependency) : HookStrategy
{
    public override string Name => "inspect";

    public override async Task ExecuteAsync(InvocationData invocation, HookOptions options, CancellationToken cancellationToken)
    {
        using JsonDocument document = JsonDocument.Parse(invocation.OriginalJson);
        string output = document.RootElement.GetProperty("observation_path").GetString()!;
        if (Environment.GetEnvironmentVariable("HOOKRUNNER_TEST_MODE") == "wait")
        {
            string ready = output + ".ready";
            await File.WriteAllTextAsync(ready, Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture), cancellationToken);
            File.Move(ready, output);
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        if (Environment.GetEnvironmentVariable("HOOKRUNNER_TEST_MODE") == "unexpected-exit")
        {
            Environment.Exit(23);
        }

        if (Environment.GetEnvironmentVariable("HOOKRUNNER_TEST_MODE") == "technical-failure")
        {
            throw new InvalidOperationException("Consumer technical failure.");
        }

        string json = JsonSerializer.Serialize(new Observation(Environment.ProcessId, dependency.Value, invocation, options));
        await File.WriteAllTextAsync(output, json, cancellationToken);
        Console.WriteLine("A consumer hook can write arbitrary stdout.");
    }
}

public sealed record Observation(int ProcessId, string Dependency, InvocationData Invocation, HookOptions Options);
