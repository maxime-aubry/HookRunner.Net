# Tests et fixtures

[Retour à l'index](README.md). Les commentaires XML des méthodes décrivent
le scénario, les données de théorie et le résultat attendu. Les petits doubles
privés restent proches des assertions qui les utilisent.

## Tests d'exécution

| Source | Responsabilité |
| --- | --- |
| [Application/UseCaseTests](../../tests/HookRunner.ExecutionTests/Application/UseCaseTests.cs) | Sélection d'un seul hook, options, recherche ascendante et absence de lancement après erreur. Les stratégies sentinelles lèvent si le superviseur les appelle directement ; l'exécuteur mémorise la requête. |
| [Infrastructure/Agents/Codex/InvocationTests](../../tests/HookRunner.ExecutionTests/Infrastructure/Agents/Codex/InvocationTests.cs) | Champs natifs invalides, session, dossier et conservation exacte du JSON original. |
| [Infrastructure/Execution/ProcessExecutionTests](../../tests/HookRunner.ExecutionTests/Infrastructure/Execution/ProcessExecutionTests.cs) | Enfant réel, résultats internes invalides, panne de lancement et terminaison sur annulation extérieure. |
| [Cli/CliTests](../../tests/HookRunner.ExecutionTests/Cli/CliTests.cs) | Arguments, configuration, composition, libération asynchrone, transport enfant et données déjà préparées. |
| [Integration/HostTests](../../tests/HookRunner.ExecutionTests/Integration/HostTests.cs) | Superviseur et enfant réels, scan des stratégies, dépendance consommateur, chargement depuis le programme compilé, flux et codes de sortie. |

Les utilitaires de [Support](../../tests/HookRunner.ExecutionTests/Support/)
créent une solution temporaire et localisent ou copient le consommateur
compilé. `ConsumerProgram.CopyTo` remplace les assemblies HookRunner par
celles sous test afin de conserver l'instrumentation de couverture.
`SolutionFixture.Dispose` supprime uniquement sa propre solution temporaire.
La collection `ConsoleTestsGroup` sérialise les scénarios qui modifient les
flux standard et la configuration globale du programme de tests.

Dans `CliTests`, `ConfigurationFile` restaure la configuration initiale et
`ConsoleCapture` restaure les flux précédents. Les doubles d'exécution
mémorisent leurs requêtes sans lancer d'enfant. `AsyncConsumerService`
permet d'observer la libération et ses erreurs. Dans `HostTests`, le helper
`RunHostAsync` borne l'attente du superviseur et capture stdout et stderr.

## Tests des politiques

[LayerTests](../../tests/HookRunner.ArchitectureTests/Dependencies/LayerTests.cs)
vérifie le graphe exhaustif de projets, les références évaluées par MSBuild,
les références compilées et le maintien de Cli comme bibliothèque.

[GateTests](../../tests/HookRunner.CITests/Quality/GateTests.cs)
vérifie la lecture d'un PDB réel, la fusion de rapports synthétiques,
les points manquants et la politique de sévérité de l'audit NuGet.

## Consommateur de test

[Program](../../fixtures/HookRunner.TestHost/Hosting/Program.cs) possède un
point d'entrée explicite documenté. Son scan Scrutor est limité à l'assembly
de [InspectHook](../../fixtures/HookRunner.TestHost/Hooks/InspectHook.cs).
Le hook écrit une [Observation](../../fixtures/HookRunner.TestHost/Observations/Observation.cs)
contenant son PID, les données reçues et la valeur de
[ConsumerDependency](../../fixtures/HookRunner.TestHost/Services/ConsumerDependency.cs).

Les modes contrôlés simulent attente, exception, sortie inattendue, doublon
de stratégie et résultat de protocole invalide. Ils ne nécessitent aucun
appel à un modèle. La fixture sert aussi à la consommation des packages
NuGet dans `scripts/test-consumer-packages.ps1`.
