# HookRunner.Application

[Retour à l'index](README.md). Cette couche définit le contrat du hook et
coordonne une invocation sans lancer elle-même de processus. Son unique
référence interne est `HookRunner.Domain`.

## HookStrategy

[Source](../../src/HookRunner.Application/Hooks/HookStrategy.cs).

Classe abstraite publique dont hérite chaque hook du projet consommateur.
Elle n'a aucun service ni constructeur explicite dans l'implémentation actuelle.
Les dépendances propres au hook se déclarent dans son constructeur concret.

`Name` est une propriété abstraite de type `string`. Le nom doit être non vide
et unique sans distinction de casse parmi toutes les stratégies enregistrées.
Il doit rester stable entre les processus, car il sert à retrouver la stratégie
dans l'enfant. Il n'est pas déduit du nom de la classe.

### ExecuteAsync(InvocationData invocation, HookOptions options, CancellationToken cancellationToken)

Méthode abstraite publique renvoyant `Task`. `invocation` contient l'événement
d'origine ; `options` contient les paramètres préparés pour cet appel. Le hook
doit respecter le token pour ses opérations annulables. Une terminaison normale
indique un succès ; une exception remonte au point d'entrée enfant, qui produit
actuellement un échec technique. La classe de base ne fournit pas encore de
méthode d'exécution de commande ou de prompt.

## InvocationData

[Source](../../src/HookRunner.Application/Hooks/InvocationData.cs).

`record` public scellé représentant les données neutres d'une invocation.
Son constructeur reçoit les six propriétés suivantes, sans validation propre :

| Propriété | Type | Signification |
| --- | --- | --- |
| `Agent` | `string` | Agent déclencheur ; `codex` dans l'adaptateur actuel. |
| `EventName` | `string` | Nom de l'événement, par exemple `Stop`. |
| `SessionId` | `string` | Identifiant de session fourni par l'agent. |
| `Project` | `string?` | Information de projet facultative ; `null` pour Codex. |
| `WorkingDirectory` | `string` | Dossier d'origine de l'agent, distinct de la racine de solution. |
| `OriginalJson` | `string` | Texte JSON d'origine conservé intégralement, champs supplémentaires inclus. |

Le modèle ne dépend d'aucun type du protocole Codex. Un hook peut relire
`OriginalJson` pour ses besoins spécifiques.

## TargetProject, HookOptions et IHookOptionsPreparer

Sources : [TargetProject](../../src/HookRunner.Application/Hooks/Options/TargetProject.cs),
[HookOptions](../../src/HookRunner.Application/Hooks/Options/HookOptions.cs) et
[IHookOptionsPreparer](../../src/HookRunner.Application/Hooks/Options/IHookOptionsPreparer.cs).

`TargetProject` est un `record` public scellé de constructeur
`TargetProject(string Name, string ProjectFilePath, string DirectoryPath)`.
Il décrit le nom d'un projet cible, le chemin de son `.csproj` et son dossier.
La résolution effective des projets cibles n'est pas encore implémentée.

`HookOptions` est un `record` public scellé dont le constructeur expose :

| Propriété | Type | Signification et comportement actuel |
| --- | --- | --- |
| `TimeoutSeconds` | `int` | Timeout effectif, positif après préparation ; transmis sans déclencher de minuterie. |
| `SolutionRoot` | `string` | Premier dossier ascendant contenant une solution. |
| `DefaultWorkingDirectory` | `string` | Dossier de lancement de l'enfant ; actuellement égal à `SolutionRoot`. |
| `Projects` | `IReadOnlyList<TargetProject>` | Projets résolus ; liste vide dans le parcours superviseur actuel. |
| `StateFilePath` | `string` | Chemin absolu préparé pour l'état futur ; aucun fichier n'est lu ou écrit par ce modèle. |

### IHookOptionsPreparer.Prepare(RunHookArgs args, InvocationData invocation)

Contrat public synchrone renvoyant `HookOptions`. Il transforme les arguments
et données d'invocation en options prêtes à transmettre au hook. Il ne possède
pas de token d'annulation. Les erreurs de préparation remontent au cas d'usage
puis à la CLI, avant toute exécution isolée. L'implémentation actuelle est
[`HookOptionsPreparer`](infrastructure.md#hookoptionspreparer).

## IRequest, IResponse et IUseCase<TRequest, TResponse>

Sources : [IRequest](../../src/HookRunner.Application/Abstractions/IRequest.cs),
[IResponse](../../src/HookRunner.Application/Abstractions/IResponse.cs) et
[IUseCase](../../src/HookRunner.Application/Abstractions/IUseCase.cs).

`IRequest` et `IResponse` sont deux interfaces marqueurs publiques, sans membre.
`IUseCase<in TRequest, TResponse>` exige respectivement ces deux contrats pour
ses paramètres génériques. `TRequest` est contravariant.

### IUseCase.ExecuteAsync(TRequest request, CancellationToken cancellationToken)

Unique méthode du contrat, renvoyant `Task<TResponse>`. Elle représente une
opération applicative asynchrone. Le contrat générique ne prescrit pas de
traduction des exceptions ; cette décision appartient à son implémentation
et à la frontière qui l'appelle.

## RunHookArgs, RunHookRequest et RunHookResponse

Sources : [RunHookArgs](../../src/HookRunner.Application/UseCases/RunHook/RunHookArgs.cs),
[RunHookRequest](../../src/HookRunner.Application/UseCases/RunHook/RunHookRequest.cs) et
[RunHookResponse](../../src/HookRunner.Application/UseCases/RunHook/RunHookResponse.cs).

Ces trois `record` publics scellés transportent les données du cas d'usage :

| Type et constructeur | Rôle |
| --- | --- |
| `RunHookArgs(string HookName, int? TimeoutSeconds, IReadOnlyList<string> Projects)` | Nom demandé, surcharge facultative du timeout et noms des projets demandés. Les projets sont encore des noms, pas des `TargetProject` résolus. |
| `RunHookRequest(RunHookArgs Args, InvocationData Invocation)` | Requête complète, implémente `IRequest`. |
| `RunHookResponse(int ExitCode)` | Code final à propager au programme consommateur, implémente `IResponse`. |

Ils n'effectuent pas de validation dans leur constructeur. L'agent demandé
par la CLI a déjà été validé avant leur utilisation et figure dans `Invocation`.

## RunHookUseCase

[Source](../../src/HookRunner.Application/UseCases/RunHook/RunHookUseCase.cs).

Classe publique scellée implémentant
`IUseCase<RunHookRequest, RunHookResponse>`. Son constructeur primaire reçoit
`IEnumerable<HookStrategy> strategies`, `IHookOptionsPreparer optionsPreparer`
et `Context context`. Les stratégies sont celles enregistrées par le
consommateur ; les deux autres dépendances préparent et exécutent l'invocation.

### ExecuteAsync(RunHookRequest request, CancellationToken cancellationToken)

Renvoie `Task<RunHookResponse>` et procède dans cet ordre :

1. Refuse une requête `null` par `ArgumentNullException`.
2. Appelle `optionsPreparer.Prepare(request.Args, request.Invocation)`.
3. Résout une stratégie avec `HookSelection.Select`.
4. Prépare le contexte avec la stratégie, l'invocation et les options.
5. Attend `context.ExecuteAsync` et encapsule son code dans `RunHookResponse`.

Les erreurs des étapes de préparation et de sélection se propagent. Les erreurs
d'exécution sont traduites en code par `Context`. Aucun appel direct à la
méthode de la stratégie n'a lieu dans le superviseur. La préparation étant
synchrone, un token déjà annulé n'interrompt pas automatiquement ces étapes.

## HookSelection

[Source](../../src/HookRunner.Application/Hooks/HookSelection.cs).

Classe statique publique centralisant la sélection, utilisée dans le
superviseur et dans l'enfant.

### Select(IEnumerable<HookStrategy> strategies, string name)

Renvoie la `HookStrategy` enregistrée correspondant à `name`. La collection
`strategies` ne peut pas être `null` (`ArgumentNullException`). La méthode
parcourt **toutes** les stratégies et construit un dictionnaire avec
`StringComparer.OrdinalIgnoreCase` avant de rechercher le nom demandé.

Une propriété `Name` vide ou composée d'espaces, un doublon même avec une autre
casse, ou un nom demandé inconnu produit une `InvalidOperationException` avec
un diagnostic dédié. Une stratégie invalide empêche donc l'exécution même si
un autre hook valide était demandé. Les noms ne sont pas nettoyés par `Trim`.
La méthode ne crée pas de stratégie et n'en exécute aucune. L'énumération des
services DI peut toutefois provoquer leur construction en amont.

## IsolatedHookRequest, HookExecutionStatus et HookExecutionResult

Sources : [IsolatedHookRequest](../../src/HookRunner.Application/Hooks/Execution/IsolatedHookRequest.cs),
[HookExecutionStatus](../../src/HookRunner.Application/Hooks/Execution/HookExecutionStatus.cs) et
[HookExecutionResult](../../src/HookRunner.Application/Hooks/Execution/HookExecutionResult.cs).

`IsolatedHookRequest(string HookName, InvocationData Invocation, HookOptions Options)`
est le `record` public scellé sérialisé vers l'enfant. Il contient le nom
canonique fourni par la stratégie sélectionnée, et non son instance.

`HookExecutionStatus` est une énumération publique :

| Valeur | Nombre | Sens et prise en charge actuelle |
| --- | --- | --- |
| `Success` | `0` | Fin normale du hook. |
| `ValidationFailure` | `1` | Statut réservé à la validation métier ; accepté par le protocole, mais non produit par le point d'entrée enfant actuel. |
| `TechnicalFailure` | `2` | Exception technique pendant la lecture, la sélection ou l'exécution dans l'enfant. |

`HookExecutionResult(HookExecutionStatus Status, string? Diagnostic)` est le
`record` public scellé retourné à l'application. Le diagnostic peut être
`null` pour un succès ; l'adaptateur de processus exige un texte non vide pour
les autres statuts reçus. Cette validation n'est pas faite par le record.

## IIsolatedHookExecutor

[Source](../../src/HookRunner.Application/Hooks/Execution/IIsolatedHookExecutor.cs).

Interface publique séparant l'application du lancement de processus.

### ExecuteAsync(IsolatedHookRequest request, CancellationToken cancellationToken)

Renvoie `Task<HookExecutionResult>`. Une implémentation exécute le hook isolé
et traduit son protocole en résultat neutre. Les erreurs de transport peuvent
lever une exception, ensuite traitée par `Context`. L'implémentation de
production est [`ProcessHookExecutor`](infrastructure.md#processhookexecutor) ;
les tests utilisent aussi des exécuteurs qui mémorisent la requête.

## Context

[Source](../../src/HookRunner.Application/Hooks/Execution/Context.cs).

Classe publique scellée, construite avec `IIsolatedHookExecutor executor` et
`TextWriter diagnostics`. Elle conserve une requête privée nullable entre
`SetStrategy` et `ExecuteAsync`. Dans la composition par défaut, sa durée de
vie DI est transitoire et les diagnostics sont dirigés vers `Console.Error`.

### SetStrategy(HookStrategy strategy, InvocationData invocation, HookOptions options)

Méthode publique `void` qui refuse une stratégie `null`, puis remplace la
requête mémorisée par `new IsolatedHookRequest(strategy.Name, invocation, options)`.
Elle ne conserve pas l'instance du hook, ne prépare pas les options et n'exécute
rien. Elle ne valide pas elle-même `invocation`, `options` ou le nom.

### ExecuteAsync(CancellationToken cancellationToken)

Renvoie `Task<int>`. Exige une requête préparée puis délègue à l'exécuteur en
lui transmettant le token. Pour `Success`, écrit `Hook '<nom>' succeeded.` et
renvoie `0`. Pour tout autre statut, écrit le diagnostic et renvoie `2`.

Les exceptions, y compris l'absence de stratégie et l'annulation, sont
interceptées : la méthode écrit `Hook execution failed: <message>` et renvoie
`2`. Elle ne gère encore ni compteur, ni persistance, ni budget de timeout,
ni distinction de code entre validation et panne technique. Le writer doit
rester utilisable : une exception lors de l'écriture dans le bloc `catch`
peut encore remonter à l'appelant.
