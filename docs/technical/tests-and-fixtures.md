# Tests et fixtures

[Retour à l'index](README.md). Les commentaires XML des méthodes décrivent
le scénario, les données de théorie et le résultat attendu. Les petits doubles
privés restent proches des assertions qui les utilisent.

## Tests d'exécution

| Source | Responsabilité |
| --- | --- |
| [Application/UseCaseTests](../../tests/HookRunner.ExecutionTests/Application/UseCaseTests.cs) | Sélection d'un seul hook, options, recherche ascendante et absence de lancement après erreur. Les stratégies sentinelles lèvent si le superviseur les appelle directement ; l'exécuteur mémorise la requête. |
| [Application/TimeoutTests](../../tests/HookRunner.ExecutionTests/Application/TimeoutTests.cs) | Budget unique, confirmation, annulation extérieure, résultats tardifs et grands timeouts via Context, avec une horloge contrôlée sans attente réelle. |
| [Application/StatePersistenceTests](../../tests/HookRunner.ExecutionTests/Application/StatePersistenceTests.cs) | Expiration et annulation extérieure lors de la sauvegarde, priorité de l'annulation extérieure et propagation des pannes de stockage, avec vérification du compteur par l'invocation suivante. |
| [Infrastructure/Agents/Codex/InvocationTests](../../tests/HookRunner.ExecutionTests/Infrastructure/Agents/Codex/InvocationTests.cs) | Champs natifs invalides, session, dossier et conservation exacte du JSON original. |
| [Infrastructure/Execution/ProcessExecutionTests](../../tests/HookRunner.ExecutionTests/Infrastructure/Execution/ProcessExecutionTests.cs) | Enfant réel, résultats internes invalides, panne de lancement, annulation extérieure et arrêt forcé sur timeout d'un hook ignorant l'annulation. |
| [Cli/CliTests](../../tests/HookRunner.ExecutionTests/Cli/CliTests.cs) | Arguments, configuration et parcours superviseur. |
| [Cli/ConfigurationLoaderTests](../../tests/HookRunner.ExecutionTests/Cli/ConfigurationLoaderTests.cs) | Chargement, défauts, valeurs numériques et surcharges sur des fichiers JSON dans des dossiers temporaires indépendants. |
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
Les tests du loader utilisent la même interface `Load(dossier)` que l'hôte,
grâce à un accès interne accordé à `HookRunner.ExecutionTests` ; ils ne remplacent
pas la configuration du programme de tests et peuvent s'exécuter en parallèle.

Dans `Support`, `ConfigurationFile` restaure la configuration initiale et
`ConsoleCapture` restaure les flux précédents. Les doubles d'exécution
mémorisent leurs requêtes sans lancer d'enfant. `AsyncConsumerService`
permet d'observer la libération et ses erreurs. `ConsumerHost` borne l'attente
du superviseur et capture stdout et stderr.
[HostSimulation](../../tests/HookRunner.ExecutionTests/Support/HostSimulation.cs)
porte le mode simulé et
[HostResult](../../tests/HookRunner.ExecutionTests/Support/HostResult.cs)
conserve le PID, le code de sortie et les flux capturés. `RunningInvocation` garantit
l'annulation et l'attente finale des scénarios de processus, même après une
assertion en échec ; `ConsumerProcessObservation` attend la publication du PID.

## Compteurs et concurrence

[`CounterTests`](../../tests/HookRunner.ExecutionTests/Application/CounterTests.cs)
couvre plafonds, état corrompu ou inaccessible, identité, chemins physiques,
sérialisation et timeout pendant l'attente via `RunHookUseCase`.
[`CounterDomainTests`](../../tests/HookRunner.ExecutionTests/Application/CounterDomainTests.cs)
couvre les règles pures et les exceptions métier.
[`HostCounterTests`](../../tests/HookRunner.ExecutionTests/Integration/HostCounterTests.cs)
vérifie persistance entre lancements, surcharges et concurrence interprocessus.
[`HostConcurrencyTests`](../../tests/HookRunner.ExecutionTests/Integration/HostConcurrencyTests.cs)
synchronise explicitement les hooks par canaux nommés : une identité reste
occupée tandis qu'une autre termine, une invocation en attente expire sans
enfant ni changement de compteur, et plusieurs identités publient leurs
compteurs dans le même fichier sans perdre les entrées antérieures. Les tests
vérifient aussi la libération après succès, demande métier et erreur technique.
Un lien physique conserve l'ancien document publié avant l'autorisation des
écritures : son contenu reste inchangé après publication, ce qui détecte une
réécriture du fichier en place. `FileSnapshot` crée ce lien avec `fsutil`
sous Windows et `ln` sous Unix, sans garder ouvert le fichier remplacé.
[`StateFileObservation`](../../tests/HookRunner.ExecutionTests/Support/StateFileObservation.cs)
rouvre directement le chemin publié sans prendre le verrou de document. Une
première lecture complète précède la libération des hooks ; les lectures
continuent pendant leurs publications et une dernière suit leur terminaison.
Chaque instantané doit rester un JSON complet avec l'entrée préexistante et
des compteurs cohérents. L'ouverture partage aussi le droit de suppression
pour permettre le remplacement sous Windows ; aucune erreur d'accès ou de
lecture n'est masquée par une nouvelle tentative.
[`StatePublicationTests`](../../tests/HookRunner.ExecutionTests/Infrastructure/StatePublicationTests.cs)
conserve explicitement un lecteur ouvert pendant une sauvegarde réelle :
l'ancien handle doit lire le compteur précédent et le chemin publié le suivant.
Ce scénario reproduit sans dépendre de l'ordonnancement le refus de `File.Move`
sous Windows. Les contrats et erreurs de l'appel natif, son tampon Unicode et
la propriété du handle sont vérifiés par
[`WindowsStateFilePublisherTests`](../../tests/HookRunner.ExecutionTests/Infrastructure/WindowsStateFilePublisherTests.cs)
avec des appels natifs simulés, exécutables sur les trois systèmes.
Une frontière d'exécution contrôlée avec l'horloge manuelle suspend la
confirmation d'arrêt pendant qu'un autre processus tente la même identité ;
le verrou reste détenu jusqu'à confirmation, même en cas d'échec technique,
puis les invocations suivantes retrouvent leur compteur inchangé.
Les demandes à message vide ou composé d'espaces passent par le véritable enfant
et consomment le plafond, avec trois retours `2` puis un retour `0`.
[`DirectoryLink`](../../tests/HookRunner.ExecutionTests/Support/DirectoryLink.cs)
crée un lien symbolique Unix ou une jonction Windows pour tester les alias physiques.
[`MappedDrive`](../../tests/HookRunner.ExecutionTests/Support/MappedDrive.cs)
associe temporairement une lettre libre à une fixture Windows avec `subst`, puis
retire cette association sans supprimer le dossier cible. `UseCaseTests` l'utilise
pour vérifier qu'une jonction vers un autre lecteur est refusée avant l'exécution.

La collection
[`ProcessTestsGroup`](../../tests/HookRunner.ExecutionTests/Support/ProcessTestsGroup.cs)
sérialise les suites de processus instrumentés pour éviter les collisions à la
publication des rapports Coverlet. Le scénario explicitement concurrent utilise
les assemblies de la fixture non instrumentées ; les autres tests exécutent le
même code avec couverture. Aucun code de production n'est exclu de la mesure.
`HookGate` échange un signal d'entrée et une autorisation de terminer avec
l'enfant. Le hook publie aussi un témoin d'entrée propre à chaque invocation,
avant toute attente sur le canal ; l'absence de ce fichier après terminaison
prouve qu'un hook en attente de verrou n'a pas commencé. `RunningConsumer` annule et attend les superviseurs concurrents
avant la suppression de la solution temporaire, y compris après une assertion
en échec. Ces scénarios utilisent le stockage réel et aucun modèle.

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
[TryCatchTests](../../tests/HookRunner.CITests/Quality/TryCatchTests.cs)
exécute la commande `try-catch` sur des sources conformes et non conformes.
Les scénarios couvrent les clauses `finally` autorisées, les instructions avant
ou après un bloc, les imbrications, fonctions locales, lambdas, constructeurs,
accesseurs, chaînes et commentaires, erreurs de syntaxe, symboles `DEBUG` et
`TRACE`, ainsi que le parcours des sources hors sorties générées. Les assertions
portent sur le code de sortie et les diagnostics de la commande réelle.

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
exception métier à message fourni par le scénario, exception technique, sortie inattendue, doublon
de stratégie et résultat de protocole invalide. Ils ne nécessitent aucun
appel à un modèle. La fixture sert aussi à la consommation des packages
NuGet dans `scripts/test-consumer-packages.ps1`.
