using System.Text.RegularExpressions;

namespace HookRunner.CI;

internal static class DocumentationLinkCheck
{
    /// <summary>
    /// Vérifie l'existence des cibles locales des liens Markdown de la racine et du dossier docs.
    /// </summary>
    /// <param name="root">Chemin absolu de la racine du dépôt.</param>
    /// <returns>
    /// 0 après vérification de tous les liens locaux et écriture du nombre de fichiers contrôlés sur stdout.
    /// </returns>
    /// <remarks>
    /// Les fragments d'ancre sont retirés ; les liens absolus et externes sont ignorés. Les cibles sont décodées
    /// puis résolues relativement au fichier Markdown. Ce contrôle ne valide pas les ancres ni le contenu.
    /// </remarks>
    /// <exception cref="FileNotFoundException">
    /// Une cible locale de lien n'existe ni comme fichier ni comme dossier.
    /// </exception>
    public static int Run(string root)
    {
        List<string> files = [.. Directory.GetFiles(Path.Combine(root, "docs"), "*.md", SearchOption.AllDirectories)];
        files.AddRange(Directory.GetFiles(root, "*.md"));
        foreach (string file in files)
        {
            foreach (string target in LocalTargets(file))
            {
                ValidateTarget(file, target);
            }
        }

        Console.WriteLine($"Checked local file links in {files.Count} Markdown files (anchors and external links are not checked).");
        return 0;
    }

    private static IEnumerable<string> LocalTargets(string file)
    {
        foreach (Match match in Regex.Matches(File.ReadAllText(file), @"\]\(([^)]+)\)", RegexOptions.None, TimeSpan.FromSeconds(1)))
        {
            string target = match.Groups[1].Value.Split('#')[0].Trim('<', '>');
            if (target.Length > 0 && !Uri.TryCreate(target, UriKind.Absolute, out _))
            {
                yield return target;
            }
        }
    }

    private static void ValidateTarget(string file, string target)
    {
        string resolved = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, Uri.UnescapeDataString(target)));
        if (!File.Exists(resolved) && !Directory.Exists(resolved))
        {
            throw new FileNotFoundException($"Broken documentation link in {file}: {target}");
        }
    }
}
