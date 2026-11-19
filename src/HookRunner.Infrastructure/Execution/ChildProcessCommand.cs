using System.Reflection;

namespace HookRunner.Infrastructure;

/// <summary>Exécutable et arguments initiaux nécessaires au relancement du consommateur.</summary>
/// <param name="FileName">Chemin ou nom de l'exécutable à lancer.</param>
/// <param name="Arguments">
/// Arguments initiaux de l'exécutable, avant ajout des arguments du protocole enfant.
/// </param>
public sealed record ChildProcessCommand(string FileName, IReadOnlyList<string> Arguments)
{
    /// <summary>Construit la commande permettant de relancer le programme consommateur courant.</summary>
    /// <returns>
    /// L'exécutable courant, accompagné du chemin de l'assembly d'entrée lorsque le lanceur est dotnet.
    /// </returns>
    /// <remarks>
    /// Un apphost autonome se relance directement. Cette détection suppose un processus .NET avec un exécutable
    /// et, pour dotnet, un assembly d'entrée disponibles.
    /// </remarks>
    public static ChildProcessCommand ForCurrentProcess()
    {
        string executable = Environment.ProcessPath!;
        string[] arguments = Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            ? [Assembly.GetEntryAssembly()!.Location]
            : [];
        return new ChildProcessCommand(executable, arguments);
    }
}
