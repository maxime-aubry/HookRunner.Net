namespace HookRunner.CITests;

/// <summary>Vérifie la structure des interceptions à travers la commande publique du programme CI.</summary>
public sealed class TryCatchTests
{
    /// <summary>Accepte les interceptions isolées et les fonctions indépendantes qui les contiennent.</summary>
    /// <param name="source">Source syntaxiquement valide représentant une structure autorisée.</param>
    /// <returns>Une tâche représentant l'exécution de la commande et ses assertions.</returns>
    [Theory]
    [InlineData("class C { void M() { try { Run(); } catch (System.Exception) { throw; } } }")]
    [InlineData("class C { void M() { try { Run(); } catch (System.IO.IOException) when (Retry()) { Recover(); } catch { throw; } } }")]
    [InlineData("class C { void M() { Prepare(); try { Run(); } finally { Clean(); } Finish(); } }")]
    [InlineData("class C { void M() { Prepare(); void Local() { try { Run(); } catch { throw; } } Local(); } }")]
    [InlineData("class C { void M() { System.Action action = () => { try { Run(); } catch { throw; } }; action(); } }")]
    [InlineData("class C { C() { try { Run(); } catch { throw; } } int Value { get { try { return Read(); } catch { throw; } } } }")]
    [InlineData("class C { string Text() => \"try { } catch { }\"; /* try {} catch {} */ }")]
    [InlineData("class C { string Text() => \"\"\"try { } catch { }\"\"\"; }")]
    public async Task ConformingSourceReturnsZero(string source)
    {
        using TemporaryDirectory directory = new();
        directory.Write("Example.cs", source);

        (int exitCode, string error) = await CiProgram.RunAsync(["try-catch", directory.Root]);

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
    }

    /// <summary>Refuse une méthode qui effectue une préparation avant son interception.</summary>
    /// <returns>Une tâche représentant la commande et la vérification de son diagnostic.</returns>
    [Fact]
    public async Task StatementBeforeTryReturnsOneWithSourceLocation()
    {
        using TemporaryDirectory directory = new();
        directory.Write("src/Example.cs", """
            class Example
            {
                void Run()
                {
                    Prepare();
                    try { Execute(); }
                    catch (System.Exception) { throw; }
                }
            }
            """);

        (int exitCode, string error) = await CiProgram.RunAsync(["try-catch", directory.Root]);

        Assert.Equal(1, exitCode);
        Assert.Contains("Example.cs(6,9): error HR0001", error, StringComparison.Ordinal);
    }

    /// <summary>Refuse les instructions après l'interception et les interceptions imbriquées.</summary>
    /// <param name="source">Source contenant une structure interdite dans une fonction.</param>
    /// <returns>Une tâche représentant la commande et la vérification de son diagnostic.</returns>
    [Theory]
    [InlineData("class C { void M() { try { Run(); } catch { throw; } Finish(); } }")]
    [InlineData("class C { void M() { while (true) { try { Run(); } catch { Retry(); } } } }")]
    [InlineData("class C { void M() { try { Run(); } catch { throw; } finally { Clean(); } } }")]
    [InlineData("class C { void M() { try { try { Run(); } catch { throw; } } catch { throw; } } }")]
    [InlineData("class C { void M() { void Local() { Prepare(); try { Run(); } catch { throw; } } } }")]
    [InlineData("class C { void M() { System.Action action = () => { Prepare(); try { Run(); } catch { throw; } }; } }")]
    [InlineData("class C { C() { Prepare(); try { Run(); } catch { throw; } } }")]
    [InlineData("class C { int Value { get { Prepare(); try { return Read(); } catch { throw; } } } }")]
    public async Task NonconformingSourceReturnsOne(string source)
    {
        using TemporaryDirectory directory = new();
        directory.Write("Example.cs", source);

        (int exitCode, string error) = await CiProgram.RunAsync(["try-catch", directory.Root]);

        Assert.Equal(1, exitCode);
        Assert.Contains("error HR0001", error, StringComparison.Ordinal);
    }

    /// <summary>Vérifie que la vérification parcourt tous les fichiers sans analyser les sorties générées.</summary>
    /// <returns>Une tâche représentant la commande et ses assertions sur les diagnostics.</returns>
    [Fact]
    public async Task ReportsEveryHandwrittenFileAndSkipsBuildDirectories()
    {
        using TemporaryDirectory directory = new();
        const string source = "class C { void M() { Prepare(); try { Run(); } catch { throw; } } }";
        directory.Write("src/First.cs", source);
        directory.Write("tests/Second.cs", source);
        directory.Write("tools/Third.cs", source);
        directory.Write("fixtures/Fourth.cs", source);
        directory.Write("src/bin/Generated.cs", source);
        directory.Write("src/obj/Generated.cs", source);
        directory.Write("artifacts/Generated.cs", source);
        directory.Write(".git/Generated.cs", source);
        directory.Write(".nuget/Generated.cs", source);

        (int exitCode, string error) = await CiProgram.RunAsync(["try-catch", directory.Root]);

        Assert.Equal(1, exitCode);
        Assert.Contains("First.cs", error, StringComparison.Ordinal);
        Assert.Contains("Second.cs", error, StringComparison.Ordinal);
        Assert.Contains("Third.cs", error, StringComparison.Ordinal);
        Assert.Contains("Fourth.cs", error, StringComparison.Ordinal);
        Assert.DoesNotContain("Generated.cs", error, StringComparison.Ordinal);
    }

    /// <summary>Refuse une source invalide plutôt que de conclure à sa conformité.</summary>
    /// <returns>Une tâche représentant la commande et ses assertions sur l'erreur de syntaxe.</returns>
    [Fact]
    public async Task SyntaxErrorReturnsOneWithCompilerDiagnostic()
    {
        using TemporaryDirectory directory = new();
        directory.Write("Broken.cs", "class Broken { void M( }");

        (int exitCode, string error) = await CiProgram.RunAsync(["try-catch", directory.Root]);

        Assert.Equal(1, exitCode);
        Assert.Contains("Broken.cs", error, StringComparison.Ordinal);
        Assert.Contains("error CS", error, StringComparison.Ordinal);
    }

    /// <summary>Contrôle aussi les branches conditionnelles activées par les configurations usuelles du SDK.</summary>
    /// <param name="symbol">Symbole qui active la source non conforme.</param>
    /// <returns>Une tâche représentant la commande et son diagnostic de structure.</returns>
    [Theory]
    [InlineData("DEBUG")]
    [InlineData("TRACE")]
    public async Task ConditionalSourceReturnsOne(string symbol)
    {
        using TemporaryDirectory directory = new();
        directory.Write("Conditional.cs", $$"""
            #if {{symbol}}
            class C { void M() { Prepare(); try { Run(); } catch { throw; } } }
            #endif
            """);

        (int exitCode, string error) = await CiProgram.RunAsync(["try-catch", directory.Root]);

        Assert.Equal(1, exitCode);
        Assert.Contains("Conditional.cs", error, StringComparison.Ordinal);
        Assert.Contains("error HR0001", error, StringComparison.Ordinal);
    }
}
