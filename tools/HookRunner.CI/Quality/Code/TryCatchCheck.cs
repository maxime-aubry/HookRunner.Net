using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace HookRunner.CI;

/// <summary>Vérifie que chaque interception occupe entièrement le corps de sa fonction.</summary>
internal static class TryCatchCheck
{
    /// <summary>Analyse les sources C# du dépôt et signale toutes les interceptions mal structurées.</summary>
    /// <param name="root">Racine absolue du dépôt à parcourir.</param>
    /// <returns>0 si les sources sont conformes, sinon 1 avec les emplacements écrits sur stderr.</returns>
    /// <remarks>Les simples try/finally sont hors du périmètre de cette règle.</remarks>
    public static int Run(string root)
    {
        bool valid = true;
        foreach (string file in SourceFiles(root))
        {
            if (!CheckFile(root, file))
            {
                valid = false;
            }
        }

        return valid ? 0 : 1;
    }

    /// <summary>Énumère les sources sans parcourir les sorties de compilation, caches ou liens de dossiers.</summary>
    /// <param name="directory">Dossier à parcourir récursivement.</param>
    /// <returns>Les chemins des fichiers C# manuscrits, dans un ordre stable.</returns>
    private static IEnumerable<string> SourceFiles(string directory)
    {
        foreach (string file in Directory.EnumerateFiles(directory, "*.cs").Order(StringComparer.Ordinal))
        {
            yield return file;
        }

        foreach (string child in Directory.EnumerateDirectories(directory).Order(StringComparer.Ordinal))
        {
            string name = Path.GetFileName(child);
            if (name.Equals("bin", StringComparison.OrdinalIgnoreCase)
                || name.Equals("obj", StringComparison.OrdinalIgnoreCase)
                || name.Equals("artifacts", StringComparison.OrdinalIgnoreCase)
                || name.Equals(".git", StringComparison.OrdinalIgnoreCase)
                || name.Equals(".nuget", StringComparison.OrdinalIgnoreCase)
                || (File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0)
            {
                continue;
            }

            foreach (string file in SourceFiles(child))
            {
                yield return file;
            }
        }
    }

    /// <summary>Refuse les erreurs de syntaxe et les interceptions qui ne terminent pas seules leur fonction.</summary>
    /// <param name="root">Racine utilisée pour rendre les diagnostics relatifs au dépôt.</param>
    /// <param name="file">Source C# à analyser sans compilation sémantique.</param>
    /// <returns>True si le fichier est syntaxiquement valide et respecte la règle HR0001.</returns>
    private static bool CheckFile(string root, string file)
    {
        string source = File.ReadAllText(file);
        string path = Path.GetRelativePath(root, file);
        string[][] symbolSets = [[], ["TRACE"], ["DEBUG", "TRACE"]];
        IEnumerable<string> diagnostics = symbolSets
            .Select(symbols => CSharpSyntaxTree.ParseText(source, CSharpParseOptions.Default.WithPreprocessorSymbols(symbols), path))
            .SelectMany(Diagnostics)
            .Distinct(StringComparer.Ordinal);
        bool valid = true;
        foreach (string diagnostic in diagnostics)
        {
            Console.Error.WriteLine(diagnostic);
            valid = false;
        }

        return valid;
    }

    /// <summary>Produit les erreurs de syntaxe et de structure pour une configuration du préprocesseur.</summary>
    /// <param name="tree">Arbre syntaxique de la source dans la configuration contrôlée.</param>
    /// <returns>Les diagnostics de compilation ou HR0001 avec leurs emplacements sources.</returns>
    private static IEnumerable<string> Diagnostics(SyntaxTree tree)
    {
        foreach (Diagnostic diagnostic in tree.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            yield return diagnostic.ToString();
        }

        foreach (TryStatementSyntax statement in tree.GetRoot().DescendantNodes().OfType<TryStatementSyntax>())
        {
            if (statement.Catches.Count > 0 && !OccupiesFunctionBody(statement))
            {
                FileLinePositionSpan location = statement.GetLocation().GetLineSpan();
                yield return $"{location.Path}({location.StartLinePosition.Line + 1},{location.StartLinePosition.Character + 1}): error HR0001: A try/catch must be the only statement in its function body, ending with its last catch (no finally).";
            }
        }

    }

    /// <summary>Vérifie la fonction la plus proche afin de traiter séparément fonctions locales et lambdas.</summary>
    /// <param name="statement">Interception dont le corps englobant doit être contrôlé.</param>
    /// <returns>True si le try/catch occupe seul le corps et ne possède pas de finally.</returns>
    private static bool OccupiesFunctionBody(TryStatementSyntax statement)
    {
        SyntaxNode? function = statement.Ancestors().FirstOrDefault(node => node is
            BaseMethodDeclarationSyntax or LocalFunctionStatementSyntax or AnonymousFunctionExpressionSyntax or AccessorDeclarationSyntax);
        BlockSyntax? body = function switch
        {
            BaseMethodDeclarationSyntax method => method.Body,
            LocalFunctionStatementSyntax local => local.Body,
            AnonymousFunctionExpressionSyntax { Body: BlockSyntax block } => block,
            AccessorDeclarationSyntax accessor => accessor.Body,
            _ => null,
        };
        return body is not null && body.Statements.Count == 1 && body.Statements[0] == statement && statement.Finally is null;
    }
}
