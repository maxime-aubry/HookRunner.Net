using System.Globalization;

namespace HookRunner.TestHost;

/// <summary>Simule les modes de panne du consommateur indépendamment de l'observation du hook.</summary>
internal static class HookFailureSimulation
{
    public static async Task ApplyAsync(string? mode, string output, CancellationToken cancellationToken)
    {
        switch (mode)
        {
            case "wait":
            case "uncooperative-wait":
                await WaitAsync(mode, output, cancellationToken);
                break;
            case "unexpected-exit":
                Environment.Exit(23);
                break;
            case "technical-failure":
                throw new InvalidOperationException("Consumer technical failure.");
        }
    }

    private static async Task WaitAsync(string mode, string output, CancellationToken cancellationToken)
    {
        string ready = output + ".ready";
        await File.WriteAllTextAsync(ready, Environment.ProcessId.ToString(CultureInfo.InvariantCulture), cancellationToken);
        File.Move(ready, output);
        if (mode == "uncooperative-wait")
        {
            Thread.Sleep(Timeout.Infinite);
        }

        await Task.Delay(Timeout.Infinite, cancellationToken);
    }
}
