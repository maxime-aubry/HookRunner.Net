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

    /// <summary>Extrait les cibles relatives des liens Markdown en retirant les fragments d'ancre.</summary>
    /// <param name="file">Fichier Markdown à lire lors de l'énumération.</param>
    /// <returns>Les cibles locales non vides, sans les éventuels chevrons qui les entourent.</returns>
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

    /// <summary>Décode une cible relative et vérifie son existence à partir du dossier du document.</summary>
    /// <param name="file">Document Markdown contenant le lien.</param>
    /// <param name="target">Cible locale du lien, sans fragment d'ancre.</param>
    /// <exception cref="FileNotFoundException">La cible résolue n'existe ni comme fichier ni comme dossier.</exception>
    private static void ValidateTarget(string file, string target)
    {
        string resolved = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, Uri.UnescapeDataString(target)));
        if (!File.Exists(resolved) && !Directory.Exists(resolved))
        {
            throw new FileNotFoundException($"Broken documentation link in {file}: {target}");
        }
    }
}
