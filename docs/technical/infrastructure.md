# HookRunner.Infrastructure

[Retour à l'index](README.md). Les adaptateurs dépendent des contrats
d'Application et ne déplacent aucune règle métier dans les protocoles.
Les commentaires XML des sources détaillent les paramètres, retours et erreurs.

## CodexEventAdapter

[Source](../../src/HookRunner.Infrastructure/Agents/Codex/CodexEventAdapter.cs).
`Parse` exige un objet JSON avec `session_id`, `hook_event_name` et `cwd`
non vides. Le dossier doit être absolu et existant. `RequiredString` centralise
la validation des champs. L'invocation conserve exactement le JSON d'origine,
porte l'agent `codex` et n'invente pas de projet ni de session de secours.

## HookOptionsPreparer

[Source](../../src/HookRunner.Infrastructure/Configuration/HookOptionsPreparer.cs).
`Prepare` applique la surcharge du timeout, valide sa positivité et exige
un chemin d'état configuré relatif à la racine. Les chemins enracinés et les
destinations normalisées hors de la solution sont refusés avant l'exécution.
Depuis le dossier de l'agent, il remonte jusqu'au premier dossier contenant
un fichier `.sln` ou `.slnx`. La racine devient le dossier par défaut ; le
chemin d'état y est résolu. Sans sélection, la liste de projets reste vide. Il ne crée
pas d'état et ne déclenche aucune minuterie.

La recherche ascendante appartient à
[SolutionRootLocator](../../src/HookRunner.Infrastructure/Configuration/SolutionRootLocator.cs).
[StateFilePathResolver](../../src/HookRunner.Infrastructure/Configuration/StateFilePathResolver.cs)
regroupe la validation du chemin configuré et son confinement dans la solution.
Le contrôle porte aussi sur la destination physique des liens : une jonction
vers un autre lecteur Windows est refusée avant l'ouverture de l'état.
Le préparateur conserve l'ordre des validations et assemble les options finales.

[TargetProjectResolver](../../src/HookRunner.Infrastructure/Configuration/TargetProjectResolver.cs)
lit les solutions `.sln` avec `Microsoft.Build.Construction.SolutionFile` et les
solutions `.slnx` avec `Microsoft.VisualStudio.SolutionPersistence`, sans explorer
les autres niveaux ni évaluer les fichiers projets. Le lecteur classique accepte
les noms de fichiers identiques dans un même dossier logique, afin que seuls les
noms demandés déclenchent le contrôle d'ambiguïté. Les dossiers de solution sont
exclus des références de projets. Les noms sont
ceux des fichiers `.csproj` sans extension : espaces périphériques retirés,
comparaison insensible à la casse, demandes et références au même fichier physique
dédupliquées. L'ordre des demandes est conservé.
Lorsque plusieurs noms désignent le même fichier physique, seuls le nom et le
chemin de la première demande sont conservés, après validation de tous les noms.

Seuls les noms demandés peuvent provoquer un diagnostic d'ambiguïté. Un nom
vide, un chemin, un joker, une correspondance partielle ou un projet non référencé,
non C#, absent ou hors racine est refusé avant toute exécution et tout accès aux
compteurs. Le confinement vérifie aussi les destinations physiques des liens.
Les projets sélectionnés exposent leur nom et les chemins absolus du fichier
et de son dossier. Plusieurs projets sont transmis dans une unique invocation.

## Persistance des compteurs

[FileHookStateStore](../../src/HookRunner.Infrastructure/Storage/FileHookStateStore.cs)
implémente `IHookStateStore` et retourne une session `IHookStateSession` possédant
le verrou d'identité jusqu'à sa libération. Le verrou couvre attente de résultat,
arrêt du processus enfant et persistance. Un verrou de document distinct protège
les lectures et fusions d'écriture ; il n'est pas conservé pendant le hook.
`OpenLockedSessionAsync` prend en charge la libération du verrou d'identité si
l'initialisation échoue. `ReadSessionAsync` possède le verrou de document pendant
la lecture et la construction de la session. `WriteTemporaryAsync` garantit le
nettoyage du temporaire après la tentative de publication. Ces fonctions isolent
les blocs de traitement d'erreur et de nettoyage sans élargir leur périmètre.

[CanonicalPath](../../src/HookRunner.Infrastructure/Storage/CanonicalPath.cs)
résout les composants symboliques et retrouve l'orthographe physique de chaque
composant existant, selon la sensibilité réelle du volume. Les suffixes absents
sont conservés sans création d'entrée. Un lien cassé utilise la cible fournie
par .NET, même absente ; les erreurs de résolution, notamment les boucles
signalées par .NET, sont propagées. L'inspection d'existence garde les limites
de .NET et ne distingue pas une absence d'une erreur masquée par cette inspection.
Une empreinte
SHA-256 de la racine physique, de l'agent, de la session et du nom de hook
normalisé identifie chaque entrée. Les worktrees conservent leurs propres racines.
Le document JSON versionné contient `Version` et `Counters`, un dictionnaire
d'empreintes vers des entiers non négatifs. Les propriétés manquantes, inconnues,
dupliquées, nulles ou invalides sont refusées sans réécrire le document.

Chaque écriture utilise un temporaire adjacent, synchronisé sur disque puis
publié par [StateFilePublisher](../../src/HookRunner.Infrastructure/Storage/StateFilePublisher.cs).
Sous Unix, la publication utilise `File.Move` avec remplacement. Sous Windows,
[WindowsStateFilePublisher](../../src/HookRunner.Infrastructure/Storage/WindowsStateFilePublisher.cs)
ouvre le temporaire avec le droit `DELETE` et soumet `FileRenameInfoEx` avec
`FILE_RENAME_REPLACE_IF_EXISTS | FILE_RENAME_POSIX_SEMANTICS`.
Les chemins absolus normalisés du temporaire et de la destination reçoivent
un préfixe étendu (`\\?\` pour un chemin local, `\\?\UNC\` pour un partage UNC) ;
les chemins de périphérique déjà préfixés sont conservés. Cette conversion
permet les chemins longs même lorsque `LongPathsEnabled` est désactivé.
Les lecteurs existants conservent l'ancien document ; les nouvelles ouvertures obtiennent
le nouveau sans suppression préalable du chemin. `File.Move` refuse ici une
destination ouverte sous Windows et `File.Replace` peut exposer un chemin
momentanément absent. Le renommage étendu exige un système de fichiers qui le
prend en charge ; une erreur native remonte comme `IOException` avec son
`Win32Exception` d'origine, sans repli sur une publication non atomique.
Les contrats natifs sont décrits par
[Microsoft](https://learn.microsoft.com/windows-hardware/drivers/ddi/ntifs/ns-ntifs-_file_rename_information).
Les fichiers `.lock` adjacents sont des supports de
verrouillage stables, sans compteur. Ils restent en place après libération pour
éviter qu'un autre processus verrouille un inode différent. Les erreurs de
partage sont retentées avec le token du budget global ; les autres erreurs
remontent immédiatement à Context. L'état absent est créé sous verrou avant
de lancer le hook. La configuration et l'état restent des fichiers distincts.
[StateFileLock](../../src/HookRunner.Infrastructure/Storage/StateFileLock.cs)
sépare la tentative d'acquisition (`TryAcquire`, dont le résultat null représente
un conflit de partage reconnu) de l'attente annulable entre tentatives (`AcquireAsync`).

## ChildProcessCommand

[Source](../../src/HookRunner.Infrastructure/Execution/ChildProcessCommand.cs).
`ForCurrentProcess` prépare le relancement du consommateur. Avec `dotnet`,
le premier argument est l'assembly d'entrée ; avec un apphost, l'exécutable
se relance directement. Les arguments du protocole seront ajoutés ensuite.

## ProcessHookExecutor

[Source](../../src/HookRunner.Infrastructure/Execution/ProcessHookExecutor.cs).
`ExecuteAsync` crée un dossier temporaire, sérialise la requête et lance
le consommateur avec les chemins de transport. Il ferme stdin et draine
stdout et stderr sans les recopier dans les diagnostics du superviseur.
Le dossier de travail provient des options préparées.

Une attente interrompue entraîne l'arrêt de l'arbre de processus et une
confirmation bornée à cinq secondes, avec un token technique distinct du
token d'exécution. Il n'existe aucune grâce coopérative avant l'arrêt forcé.
Le token global est revérifié immédiatement avant le lancement pour empêcher
un démarrage après annulation pendant la préparation du transport.
Un code enfant non nul ou un résultat
absent provoque une erreur technique. La version, le statut et la présence
d'un diagnostic pour les échecs sont vérifiés avant de rendre un résultat
neutre à Application. Le dossier temporaire est supprimé à la libération du transport.
Les erreurs de transport, d'annulation ou de nettoyage remontent à Context.

L'exécuteur orchestre deux modules internes :
[ChildHookTransport](../../src/HookRunner.Infrastructure/Execution/ChildHookTransport.cs)
possède le dossier temporaire, la sérialisation et la validation du résultat ;
[ChildProcessRunner](../../src/HookRunner.Infrastructure/Execution/ChildProcessRunner.cs)
possède le lancement, le drainage des flux et l'arrêt garanti du processus.

## Protocole interne

Les constantes de [ChildHookProtocol](../../src/HookRunner.Infrastructure/Execution/Protocol/ChildHookProtocol.cs)
définissent le marqueur `--hookrunner-child` et la version `1`.
[ChildHookResult](../../src/HookRunner.Infrastructure/Execution/Protocol/ChildHookResult.cs)
contient version, statut obligatoire et diagnostic facultatif en cas de succès.
Le code de sortie de l'enfant décrit le transport ; le statut du hook se lit
dans ce résultat. Les modèles de transport restent dans Infrastructure.
