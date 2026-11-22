# Tests et fixtures

[Retour à l'index](README.md). Les commentaires XML des méthodes décrivent
le scénario, les données de théorie et le résultat attendu. Les petits doubles
privés restent proches des assertions qui les utilisent.

## Tests d'exécution

| Source | Responsabilité |
| --- | --- |
| [Application/UseCaseTests](../../tests/HookRunner.ExecutionTests/Application/UseCaseTests.cs) | Sélection d'un seul hook, options, recherche ascendante et absence de lancement après erreur. Les stratégies sentinelles lèvent si le superviseur les appelle directement ; l'exécuteur mémorise la requête. |
| [Application/TimeoutTests](../../tests/HookRunner.ExecutionTests/Application/TimeoutTests.cs) | Budget unique, confirmation, annulation extérieure, résultats tardifs et grands timeouts via Context, avec une horloge contrôlée sans attente réelle. |
| [Infrastructure/Agents/Codex/InvocationTests](../../tests/HookRunner.ExecutionTests/Infrastructure/Agents/Codex/InvocationTests.cs) | Champs natifs invalides, session, dossier et conservation exacte du JSON original. |
| [Infrastructure/Execution/ProcessExecutionTests](../../tests/HookRunner.ExecutionTests/Infrastructure/Execution/ProcessExecutionTests.cs) | Enfant réel, résultats internes invalides, panne de lancement, annulation extérieure et arrêt forcé sur timeout d'un hook ignorant l'annulation. |
| [Cli/CliTests](../../tests/HookRunner.ExecutionTests/Cli/CliTests.cs) | Arguments, configuration et parcours superviseur. |
| [Cli/ChildHookTests](../../tests/HookRunner.ExecutionTests/Cli/ChildHookTests.cs) | Transport enfant et données déjà préparées. |
| [Cli/HostDisposalTests](../../tests/HookRunner.ExecutionTests/Cli/HostDisposalTests.cs) | Libération asynchrone après succès, panne ou erreur de libération. |
| [Integration/HostTests](../../tests/HookRunner.ExecutionTests/Integration/HostTests.cs) | Superviseur et enfant réels, scan des stratégies, dépendance consommateur, chargement depuis le programme compilé, flux et codes de sortie. |

Les utilitaires de [Support](../../tests/HookRunner.ExecutionTests/Support/)
créent une solution temporaire et localisent ou copient le consommateur
compilé. `ConsumerProgram.CopyTo` remplace les assemblies HookRunner par
celles sous test afin de conserver l'instrumentation de couverture.
`SolutionFixture.Dispose` supprime uniquement sa propre solution temporaire.
`ManualTimeProvider` avance l'horloge monotone et déclenche les minuteries
explicitement ; il peut aussi simuler un retard d'ordonnancement du rappel.
La collection `ConsoleTestsGroup` sérialise les scénarios qui modifient les
flux standard et la configuration globale du programme de tests.

Dans `Support`, `ConfigurationFile` restaure la configuration initiale et
`ConsoleCapture` restaure les flux précédents. Les doubles d'exécution
mémorisent leurs requêtes sans lancer d'enfant. `AsyncConsumerService`
permet d'observer la libération et ses erreurs. `ConsumerHost` borne l'attente
du superviseur et capture stdout et stderr. `RunningInvocation` garantit
l'annulation et l'attente finale des scénarios de processus, même après une
assertion en échec ; `ConsumerProcessObservation` attend la publication du PID.

## Tests des politiques

[LayerTests](../../tests/HookRunner.ArchitectureTests/Dependencies/LayerTests.cs)
vérifie le graphe exhaustif de projets, les références évaluées par MSBuild,
les références compilées et le maintien de Cli comme bibliothèque.

[GateTests](../../tests/HookRunner.CITests/Quality/GateTests.cs)
vérifie la lecture d'un PDB réel, la fusion de rapports synthétiques,
les points manquants.
[AuditGateTests](../../tests/HookRunner.CITests/Quality/AuditGateTests.cs)
vérifie séparément le format NuGet et la politique de sévérité.
[CommandTests](../../tests/HookRunner.CITests/Quality/CommandTests.cs)
vérifie le code `1` et les diagnostics des commandes réelles pour les inventaires
incomplets et les rapports d'audit invalides.

## Consommateur de test

[Program](../../fixtures/HookRunner.TestHost/Hosting/Program.cs) possède un
point d'entrée explicite documenté. Son scan Scrutor est limité à l'assembly
de [InspectHook](../../fixtures/HookRunner.TestHost/Hooks/InspectHook.cs).
Le hook écrit une [Observation](../../fixtures/HookRunner.TestHost/Observations/Observation.cs)
contenant son PID, les données reçues et la valeur de
[ConsumerDependency](../../fixtures/HookRunner.TestHost/Services/ConsumerDependency.cs).

Les modes contrôlés de
[HookFailureSimulation](../../fixtures/HookRunner.TestHost/Hooks/HookFailureSimulation.cs)
simulent attente annulable, blocage C# ignorant l'annulation,
exception, sortie inattendue, doublon
de stratégie et résultat de protocole invalide. Ils ne nécessitent aucun
appel à un modèle. La fixture sert aussi à la consommation des packages
NuGet dans `scripts/test-consumer-packages.ps1`.
