using System.Diagnostics;
using HookRunner.CI;

namespace HookRunner.CITests;

/// <summary>Lance le programme CI réel et garantit la fin du processus de test.</summary>
internal static class CiProgram
{
    /// <summary>Lance l'outil CI compilé avec un délai maximal de trente secondes lié à l'annulation du test.</summary>
    /// <param name="arguments">Commande et arguments transmis séparément au programme CI.</param>
    /// <returns>Une tâche donnant le code de sortie et le texte complet de stderr.</returns>
    public static async Task<(int ExitCode, string Error)> RunAsync(string[] arguments)
    {
        ProcessStartInfo start = new("dotnet")
        {
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add(typeof(CoverageGate).Assembly.Location);
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(start)!;
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        return await WaitForResultAsync(process, timeout.Token);
    }

    /// <summary>Capture stderr et attend la sortie, puis arrête l'arbre du processus s'il est encore actif.</summary>
    /// <param name="process">Processus CI déjà démarré avec stderr redirigé.</param>
    /// <param name="cancellationToken">Annulation ou échéance technique du test.</param>
    /// <returns>Une tâche donnant le code de sortie et les diagnostics collectés.</returns>
    private static async Task<(int ExitCode, string Error)> WaitForResultAsync(Process process, CancellationToken cancellationToken)
    {
        try
        {
            Task<string> error = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            return (process.ExitCode, await error);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }
        }
    }
}
