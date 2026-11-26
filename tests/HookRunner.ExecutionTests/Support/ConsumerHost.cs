using System.Diagnostics;
using HookRunner.Infrastructure;

namespace HookRunner.ExecutionTests;

/// <summary>Exécute une copie isolée du consommateur et capture ses flux avec un nettoyage garanti.</summary>
/// <param name="directory">Dossier de travail du scénario, recevant une copie du programme sous compiled-host.</param>
/// <param name="useAppHost">True utilise l'apphost natif ; false lance l'assembly avec dotnet.</param>
/// <param name="instrument">True conserve l'instrumentation des assemblies du programme de tests.</param>
internal sealed class ConsumerHost(string directory, bool useAppHost = false, bool instrument = true)
{
    private readonly ChildProcessCommand command = ConsumerProgram.CopyTo(Path.Combine(directory, "compiled-host"), useAppHost, instrument);

    /// <summary>Lance le consommateur isolé, fournit son entrée et capture sa sortie avec un délai de trente secondes.</summary>
    /// <param name="arguments">Arguments applicatifs ajoutés à la commande du consommateur compilé.</param>
    /// <param name="json">Événement natif envoyé sur stdin avant sa fermeture.</param>
    /// <param name="simulation">Mode de simulation à transmettre par l'environnement ; null conserve le mode hérité.</param>
    /// <returns>Une tâche donnant le PID, le code de sortie et les deux flux de sortie capturés.</returns>
    public async Task<HostResult> RunAsync(string[] arguments, string json, HostSimulation? simulation = null)
    {
        ProcessStartInfo start = CreateStartInfo(arguments, simulation);
        using Process process = Process.Start(start)!;
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        return await RunStartedAsync(process, json, timeout.Token);
    }

    /// <summary>Prépare les arguments, les flux redirigés et les éventuelles variables de simulation du consommateur.</summary>
    /// <param name="arguments">Arguments ajoutés après ceux du lanceur.</param>
    /// <param name="simulation">Mode et résultat brut facultatifs à placer dans l'environnement de l'enfant.</param>
    /// <returns>Les paramètres de lancement dans le dossier isolé du scénario.</returns>
    private ProcessStartInfo CreateStartInfo(string[] arguments, HostSimulation? simulation)
    {
        ProcessStartInfo start = new(command.FileName)
        {
            WorkingDirectory = directory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string argument in command.Arguments.Concat(arguments))
        {
            start.ArgumentList.Add(argument);
        }

        if (simulation is not null)
        {
            start.Environment["HOOKRUNNER_TEST_MODE"] = simulation.Mode;
            if (simulation.Result is not null)
            {
                start.Environment["HOOKRUNNER_TEST_RESULT"] = simulation.Result;
            }
        }

        return start;
    }

    /// <summary>Exécute la capture puis arrête et attend le processus s'il reste actif, même après une erreur.</summary>
    /// <param name="process">Consommateur déjà démarré.</param>
    /// <param name="json">Contenu à fournir sur son entrée standard.</param>
    /// <param name="cancellationToken">Échéance technique de l'exécution et de la capture.</param>
    /// <returns>Une tâche donnant le résultat capturé après nettoyage du processus.</returns>
    private static async Task<HostResult> RunStartedAsync(Process process, string json, CancellationToken cancellationToken)
    {
        try
        {
            return await CaptureAsync(process, json, cancellationToken);
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

    /// <summary>Lit les deux sorties en parallèle, écrit l'événement puis ferme stdin et attend la sortie.</summary>
    /// <param name="process">Consommateur dont les trois flux standard sont redirigés.</param>
    /// <param name="json">Événement écrit intégralement sur stdin.</param>
    /// <param name="cancellationToken">Annulation de l'écriture, des lectures et de l'attente.</param>
    /// <returns>Une tâche donnant l'identité du processus terminé, son code et ses sorties complètes.</returns>
    private static async Task<HostResult> CaptureAsync(Process process, string json, CancellationToken cancellationToken)
    {
        Task<string> output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> error = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.StandardInput.WriteAsync(json.AsMemory(), cancellationToken);
        process.StandardInput.Close();
        await process.WaitForExitAsync(cancellationToken);
        return new HostResult(process.Id, process.ExitCode, await output, await error);
    }
}
