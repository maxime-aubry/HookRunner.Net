# Architecture de HookRunner

Ce document rassemble l'ensemble des règles métier, contrats et décisions
validés pour le projet. Il constitue la référence de conception de HookRunner.

Conception issue du cadrage du 16 novembre 2026, consolidée et validée à
l'issue de l'entretien du 17 novembre 2026. Ce document décrit la cible ;
les projets C# ne sont pas encore implémentés. Les règles métier et
responsabilités ci-dessous constituent la référence validée pour
l'implémentation. Les détails de réalisation qui n'affectent pas ces règles
seront précisés pendant celle-ci.

## Périmètre

HookRunner est une bibliothèque réutilisable en C# ciblant **.NET 11**
(`net11.0`). Elle permet d'exécuter des hooks déclenchés par **Codex ou Claude**,
sous **Windows, Linux et macOS**.

Le dépôt contient HookRunner et ses tests. Le projet consommateur appartient à un autre dépôt. Il
déclare ses hooks concrets dérivés de `HookStrategy`, les enregistre et
fournit son propre exécutable.
Dans la solution .NET utilisateur, ce projet supplémentaire côtoie les projets
à développer. Il référence HookRunner comme dépendance NuGet et contient la liste
des hooks concrets disponibles. Lorsqu'un événement Codex ou Claude survient,
une commande de la configuration native lance ce consommateur avec le nom
du hook souhaité. Le consommateur s'appuie sur HookRunner pour résoudre et
exécuter ce hook. Le consommateur fournit un programme appelable en ligne de
commande, sur le modèle du script Python. Une application console .NET convient
à cet hébergement ; la bibliothèque HookRunner n'impose pas un modèle de
projet particulier au consommateur, à condition qu'il respecte le contrat
d'entrée, de diagnostics et de code de sortie, et fournisse le point
d'entrée d'exécution isolée nécessaire à la supervision du timeout.
Aucune convention de nommage des fichiers ni règle métier n'est imposée par HookRunner.
Les hooks tels que `RemoveVarKeywordHook` sont des exemples de classes du
projet consommateur. HookRunner fournit les abstractions nécessaires à leur
exécution ; ces hooks concrets ne font pas partie de la bibliothèque.

## Projets et dépendances

| Projet | Responsabilités | Dépendances internes |
| --- | --- | --- |
| `HookRunner.Domain` | Règles de compteur et de blocage, exceptions métier et modèles indépendants de l'exécution. | Aucune |
| `HookRunner.Application` | `HookStrategy`, `Context`, `RunHookUseCase` et contrat générique `IUseCase<TRequest, TResponse>`, orchestration, gestion des compteurs et timeout global. Contrats d'exécution isolée des hooks, des commandes, des prompts et de stockage, et modèles de ces opérations. | `Domain` |
| `HookRunner.Infrastructure` | Exécution des hooks dans des processus isolés, supervision et arrêt forcé des processus suivis, CLI Codex/Claude, capture des résultats, adaptateurs de protocoles et persistance. | `Domain`, `Application` |
| `HookRunner.Cli` | Points d'entrée de supervision et d'exécution isolée réutilisables, arguments, configuration, injection de dépendances et branchement des adaptateurs et événements. | `Application`, `Infrastructure` |

`HookStrategy` appartient à `Application`, car elle permet de coordonner
du code C#, des commandes et des prompts. Ses fonctionnalités communes
délèguent aux contrats de services injectés de cette couche ; elles ne lancent
pas directement de processus. Les adaptateurs concrets restent dans
`Infrastructure`. Les hooks concrets sont définis dans le projet consommateur
et dérivent de cette classe de base applicative.

`Domain` définit les règles de compteur et de blocage. `Context`, dans
`Application`, les applique aux résultats d'exécution et coordonne la
persistance. Aucune dépendance de `Domain` vers `Application` n'est autorisée.

`HookRunner.Cli` est une bibliothèque d'intégration appelée par l'exécutable
consommateur. Le code de composition du consommateur utilise Scrutor pour
enregistrer les stratégies. Le scan est configuré explicitement en C# et limité
aux assemblies du consommateur choisies pour contenir les hooks concrets.
Il sélectionne les classes concrètes dérivées de `HookStrategy` et les
enregistre sous ce type de base, afin de fournir `IEnumerable<HookStrategy>`
au cas d'usage. Scrutor reste une dépendance du code de composition ;
`Application` ne le référence pas. Le scan ne détermine pas le nom d'un hook :
la sélection reste fondée sur sa propriété `Name`.
La configuration, l'injection de dépendances et les logs utilisent les
mécanismes standards de .NET. Le Domain et l'Application restent indépendants
des implémentations de ces services et des modèles de protocoles des agents.

Les interfaces se limitent aux frontières utiles, notamment les commandes,
les prompts et le stockage. La conception ne nécessite pas d'interface
supplémentaire doublonnant la classe abstraite des stratégies.

## Concepts et sélection

Le vocabulaire de référence est défini dans [`GLOSSARY.md`](../GLOSSARY.md) :

- **Événement** : événement émis par un agent IA, par exemple `Stop`.
- **Hook** : traitement concret identifié par son nom, par exemple `remove_var_keyword`, réalisé dans le consommateur par un hook tel que `RemoveVarKeywordHook`.
- **Stratégie** : contrat d'exécution commun, réalisé par chaque hook concret.
- **Contexte** : objet qui reçoit une stratégie et en déclenche l'exécution.
- **Invocation** : occurrence d'exécution d'un seul hook, déclenchée par une commande associée à un événement.
- **Projet cible** : projet de la solution utilisateur sélectionné par un hook pour y exécuter des commandes ou des prompts.

Chaque hook concret hérite de la classe abstraite `HookStrategy`, fournie
par `HookRunner.Application`. Celle-ci
définit le contrat d'exécution et fournit des fonctionnalités communes,
notamment `ExecutePrompt()`. Il n'existe pas de stratégie distincte qui
encapsule chaque hook : le hook concret est lui-même la stratégie concrète.

Le contrat d'exécution des stratégies concrètes est :

```csharp
public abstract Task ExecuteAsync(
    InvocationData invocation,
    HookOptions options,
    CancellationToken cancellationToken);
```

Les options sont propres à chaque invocation et proviennent des paramètres
de la commande native de l'agent. HookRunner les valide et prépare un objet
`HookOptions`, défini dans `Application`, avant l'exécution du hook. Cet objet
contient le timeout effectif et la liste des projets cibles résolus. Chaque
projet expose son nom, le chemin absolu de son fichier `.csproj` et celui de
son dossier. Si aucun projet n'est demandé, cette liste est vide et le dossier
de travail par défaut est la racine de la solution.
La racine et ce dossier de travail par défaut sont également disponibles
dans les options préparées pour le hook. Le contexte reçoit ces options,
applique le timeout effectif à l'exécution isolée et transmet l'objet à
`ExecuteAsync` dans le processus enfant.
Les constructeurs de la stratégie reçoivent les services réutilisables par
injection de dépendances ; les options d'invocation sont passées à l'exécution.

`InvocationData` contient les données de l'appel : événement, session,
projet, répertoire de travail et JSON original. Ces données sont transmises
à chaque exécution ; les services réutilisables sont injectés au constructeur.

La configuration native de l'agent associe les événements à des commandes.
Pour Codex, cette configuration est `hooks.json` ; Claude dispose de sa
configuration équivalente. Par exemple, la commande `execute remove_var_keyword`
permet de résoudre la stratégie concrète `RemoveVarKeywordHook`.
Chaque commande `execute <nom_du_hook>` sélectionne exactement un hook et
constitue une invocation distincte de HookRunner. Un événement peut déclencher
plusieurs hooks au moyen de plusieurs commandes dans sa configuration native.
Chaque hook est exécuté au travers de sa stratégie, avec son propre contexte
d'exécution et son résultat individuel.

Le contexte est la classe `Context`. Il reçoit une stratégie par `SetStrategy()`
et en déclenche l'exécution par `ExecuteAsync()`, suivant le pattern Strategy :

```csharp
Context context = new Context();
context.SetStrategy(new RemoveVarKeywordHook());
await context.ExecuteAsync();
```

Cet exemple illustre le pattern. Les services sont injectés aux constructeurs ;
le cas d'usage reçoit les données neutres d'invocation préparées par
l'adaptateur, prépare les options et fournit ces données au contexte avec la
stratégie résolue. Les signatures détaillées des
constructeurs seront définies à l'implémentation dans le respect de ces
responsabilités.

L'agent déclencheur est indiqué explicitement au lancement par
`--agent codex` ou `--agent claude`. Les arguments transmis au programme
consommateur suivent la forme :

```text
execute <nom_du_hook> --agent <codex|claude> [--timeout <secondes>] [--projects "<nom1,nom2,...>"]
```

Par exemple :

```text
execute remove_var_keyword --agent codex --timeout 30 --projects "tata,titi,toto"
```

Pour lancer un consommateur console .NET depuis les sources, la commande
complète inclut le lancement de son projet. Par exemple, pour un projet
consommateur nommé `Hooks` et situé dans `./Hooks` :

```text
dotnet run --project ./Hooks -- execute remove_var_keyword --agent codex
```

Le séparateur `--` délimite les options de `dotnet run` et les arguments du
programme consommateur. `--project` désigne le projet exécutable consommateur,
qui définit et enregistre les hooks concrets et référence `HookRunner.Cli`.
`HookRunner.Cli` reste une bibliothèque d'intégration appelée par ce programme.
`Hooks` est un nom d'exemple ; le consommateur choisit librement le nom de son projet.

Cet agent déclencheur est distinct de l'agent éventuellement utilisé pour
exécuter les prompts.
`--timeout` et `--projects` sont facultatifs. Sans `--timeout`, la valeur
par défaut de la configuration s'applique. Sans `--projects`, le dossier de
travail par défaut est la racine de la solution, sans sélection implicite de
tous ses projets. Les projets sont sélectionnés par leur nom ; aucun chemin
de projet n'est fourni dans la commande. Les options `--options` et
`--project-root` ne sont pas retenues.
Le nom permettant de retrouver le hook est la valeur de la propriété `Name`
de `HookStrategy`. Par exemple, un hook concret `RemoveVarKeywordHook` du
consommateur peut avoir `Name` égal à `remove_var_keyword` et être sélectionné
par `execute remove_var_keyword`. La sélection ne déduit pas le nom à partir
du nom de la classe et ne nécessite pas d'association séparée entre un alias
et un type. Cet exemple ne constitue pas un hook prédéfini fourni par HookRunner.
`Name` est obligatoire et unique parmi les hooks enregistrés par le
consommateur. Un nom vide ou un doublon est une erreur de configuration :
diagnostic et retour `2`, avant l'exécution d'un hook. La recherche ignore
les différences entre majuscules et minuscules : `remove_var_keyword` et
`REMOVE_VAR_KEYWORD` sélectionnent le même hook et le même compteur.
Deux noms qui ne diffèrent que par leurs majuscules constituent un doublon.
Si aucun hook n'est configuré pour un événement, aucune commande HookRunner
n'est lancée : aucun traitement ni changement de compteur n'a lieu. Il n'y a
alors pas de code de sortie HookRunner à produire. En revanche, une commande
`execute` sans nom de hook est invalide : diagnostic et retour `2`, sans
exécution de stratégie. Un nom de hook configuré mais inconnu reste
une erreur de configuration.

## Exécution et exceptions

Le contexte exécute la stratégie qui lui a été fournie par `SetStrategy()`.
Pour un événement déclenchant plusieurs commandes, l'ordre, la concurrence
et la poursuite après un échec relèvent du câblage natif et du comportement
de l'agent hôte. HookRunner n'ordonnance pas ces commandes et n'agrège pas
leurs résultats. Une dépendance entre deux traitements doit être prise
en compte lors de la conception du câblage consommateur.

Une stratégie reçoit ses services par injection de dépendances. Elle peut
enchaîner du code C#, des commandes et des prompts dans l'ordre choisi par
son développeur. Elle peut consulter les données de l'invocation : événement,
session, répertoire de travail et JSON original reçu de l'agent.
Les services exposent des données neutres ; le JSON original reste une donnée
opaque, sans dépendance des modèles métier envers les types natifs des agents.
Une stratégie ne connaît jamais son `Context`.

Une commande peut par exemple lancer PowerShell. Un prompt est réellement
exécuté par le service de prompts. Un même hook peut enchaîner ces opérations
et utiliser le résultat de l'une pour préparer la suivante. Leurs résultats
sont remis à la stratégie pour interprétation ; ils ne sont pas recopiés
automatiquement dans les diagnostics destinés à l'agent déclencheur.

HookRunner identifie la racine de la solution en partant du dossier de travail
communiqué par l'agent dans les données de l'événement. Il remonte les dossiers
parents et retient le premier dossier contenant au moins un fichier `.sln` ou
`.slnx`. Il examine tous les fichiers de solution de ce dossier et réunit
leurs références aux projets C# `.csproj`. Il ne poursuit pas la recherche
dans les dossiers parents ou dans les sous-dossiers. Un projet simplement
présent sur disque, sans référence dans ces solutions, n'est pas sélectionnable.
La recherche ne dépend pas du dossier du programme consommateur compilé.
Si aucun fichier de solution n'est trouvé, l'invocation émet un diagnostic
et retourne `2`, sans exécuter le hook ni modifier les compteurs. Si des
solutions sont imbriquées, le dossier de solution le plus proche est retenu.

`--projects` contient une liste de noms séparés par des virgules. Le nom d'un
projet est celui de son fichier `.csproj` sans extension : `Boutique.Domain`
désigne par exemple `src/Domain/Boutique.Domain.csproj`. La comparaison des noms
ignore la casse sur Windows, Linux et macOS. Les espaces autour des noms sont
retirés et les noms répétés, y compris avec une casse différente, ne
sélectionnent qu'une seule fois le projet. Une valeur explicitement vide ou
un élément vide, comme `--projects ""` ou `--projects "tata,,titi"`, est
invalide. Seuls les noms exacts sont acceptés, sans joker ni recherche partielle.

Les chemins référencés sont résolus depuis chaque fichier de solution. Un
même fichier `.csproj` référencé par plusieurs solutions ne compte que pour
un seul projet. Si deux fichiers projet distincts correspondent au même nom
demandé, une exception de résolution est levée. Cette ambiguïté ne bloque pas
une invocation qui ne demande pas ce nom. La recherche n'utilise ni le nom
d'affichage dans la solution, ni le nom d'assembly. Seuls les projets C#
`.csproj` sont pris en charge.

Chaque projet demandé doit exister et se trouver dans la racine retenue ou
ses descendants. Un projet situé en dehors de cette racine est refusé même
s'il est référencé dans une solution. Un nom introuvable, un fichier absent,
un nom ambigu, un projet hors de la racine ou une liste de noms invalide
provoque une exception traitée à la frontière CLI : diagnostic précis et
retour `2`, avant l'exécution du hook et sans modifier les compteurs. Tous les
projets demandés sont validés avant de lancer le hook ; aucun traitement
partiel des projets trouvés n'a lieu si cette préparation échoue.

HookRunner fournit les projets résolus dans `HookOptions`, pour permettre
au hook d'y exécuter des commandes PowerShell ou d'autres commandes et des
prompts. Le hook choisit les opérations, leur ordre et le dossier de travail
de chaque commande ou prompt. Il peut traiter chaque projet séparément ou
préparer une opération commune à plusieurs projets. La sélection de plusieurs
projets ne provoque pas plusieurs exécutions automatiques du hook : une
invocation exécute toujours un seul hook.
Sans `--projects`, la liste des projets cibles est vide et le dossier de
travail par défaut est la racine de la solution. Le dossier de travail
d'origine de l'agent, la racine de la solution et les dossiers des projets
cibles restent des informations distinctes.

L'exécution est asynchrone et ne retourne aucune valeur métier. Sa terminaison
normale indique un succès. Une exception métier dédiée,
`HookValidationException`, est levée lorsque des corrections sont nécessaires ; son
message contient les diagnostics et les consignes utiles. Les autres exceptions
signalent des erreurs techniques. Leur traitement appartient à `Context`.

Lorsqu'une sortie brute de commande ou de prompt est incluse dans un
diagnostic, seuls ses **8 000 premiers caractères** sont conservés, comme
dans `hook_common.py`. Cette limite ne tronque pas le résultat fourni à la
stratégie pour son analyse, ni les consignes de correction jointes au message.
La construction du diagnostic distingue donc les sorties brutes et les
consignes ; le contexte ne cherche pas à les séparer en analysant le texte
final d'une exception.

La stratégie interprète les résultats métier. Un code de sortie non nul peut
signifier qu'un analyseur a trouvé des violations ; il ne constitue donc pas
automatiquement un incident d'exécution.

Une réponse de modèle est une donnée destinée à la stratégie. Le moteur
n'infère pas de verdict à partir de son texte. Lorsqu'une réponse doit déterminer
un verdict métier, la stratégie demande et valide un format explicite, puis
termine normalement ou lève `HookValidationException` selon ce verdict.
Aucun appel de modèle supplémentaire n'est requis pour interpréter
automatiquement les autres réponses.

Si un prompt modifie des fichiers, la stratégie effectue les vérifications
nécessaires sur l'état obtenu avant de déclarer un succès. La seule réussite
du processus exécutant le prompt n'atteste pas la conformité des fichiers.

## Orchestration commune

`RunHookUseCase`, dans `HookRunner.Application`, est le point d'entrée
applicatif d'une invocation. Il résout le hook demandé, fournit la stratégie
et les données à `Context`, puis appelle `Context.ExecuteAsync()` pour
déclencher son exécution et retourner la réponse.

Les cas d'usage implémentent le contrat générique suivant, défini dans
`Application` :

```csharp
public interface IRequest { }

public interface IResponse { }

public interface IUseCase<TRequest, TResponse>
    where TRequest : IRequest
    where TResponse : IResponse
{
    Task<TResponse> ExecuteAsync(
        TRequest request,
        CancellationToken cancellationToken);
}
```

`IRequest` et `IResponse` sont des interfaces marqueurs. Chaque cas d'usage
possède ses types concrets d'entrée et de sortie. `RunHookUseCase` implémente
`IUseCase<RunHookRequest, RunHookResponse>`, avec `RunHookRequest : IRequest`
et `RunHookResponse : IResponse`.

`RunHookRequest` contient le nom du hook, les données neutres d'invocation,
le timeout demandé s'il est fourni et les noms des projets demandés.
Les arguments sont regroupés dans une propriété `Args` de type `RunHookArgs` ;
les données de l'événement restent dans une propriété `Invocation` :

```csharp
public sealed record RunHookArgs(
    string HookName,
    int? TimeoutSeconds,
    IReadOnlyList<string> Projects);

public sealed record RunHookRequest(
    RunHookArgs Args,
    InvocationData Invocation) : IRequest;

public sealed record RunHookResponse(int ExitCode) : IResponse;
```

Ces types sont définis dans `Application`. Le tableau brut `string[] args`
est analysé par la CLI, qui construit les arguments typés. L'absence de timeout
est représentée par `null` et l'absence de projets par une liste vide.
La CLI traduit les entrées natives et construit cette requête ; le cas
d'usage fait préparer les `HookOptions` par des services applicatifs,
notamment la résolution des projets et du timeout effectif, avant de
déclencher l'exécution. Les options résolues ne sont pas exigées de l'appelant
du cas d'usage.

`RunHookUseCase` reçoit les hooks enregistrés par injection de
`IEnumerable<HookStrategy>`. Il sélectionne le hook par sa propriété `Name`,
avec les règles d'unicité et de comparaison sans distinction de casse
définies précédemment. Les noms vides et les doublons sont rejetés avant
l'exécution. Le cas d'usage n'accède pas directement au conteneur d'injection
et aucune interface de résolution supplémentaire n'est requise.

`RunHookResponse` contient uniquement une propriété `ExitCode`, valant `0`
ou `2`, issue du résultat de `Context`. Le cas d'usage retourne cette réponse
à la CLI, qui en transmet le code à l'exécutable consommateur. La réponse
n'expose pas de booléen de succès : un code `0` peut notamment correspondre
à un timeout ou à un plafond atteint. L'émission des diagnostics reste
la responsabilité de `Context` pour l'exécution et de la frontière CLI
pour les erreurs de préparation.

`Context`, dans `HookRunner.Application`, est le contexte du pattern Strategy.
Il prend en charge :

- la supervision de l'exécution isolée de la stratégie fournie par `SetStrategy()` ;
- le timeout global, l'annulation et la demande d'arrêt forcé à expiration ;
- le traitement des exceptions métier et techniques ;
- les compteurs et leur persistance ;
- les règles de blocage ;
- l'émission des diagnostics et le choix du code de retour entier.

Les stratégies ne portent pas ces responsabilités communes.
La méthode `ExecuteAsync(CancellationToken cancellationToken)` de `Context`
retourne un `Task<int>`. Elle contient le `try/catch` qui entoure l'exécution
de la stratégie et centralise le traitement des exceptions, le timeout, les
compteurs et les diagnostics. Le code entier est transmis à l'exécutable
consommateur comme code de sortie.
`RunHookUseCase` coordonne l'invocation sans aucun `try/catch`.

`Context` s'exécute dans le processus superviseur. Il délègue l'exécution
du hook à un contrat d'`Application`, réalisé par `Infrastructure` au moyen
d'un processus enfant. La stratégie sélectionnée identifie le hook à lancer ;
le processus enfant résout ce même hook par son nom, avec les mêmes
enregistrements du consommateur, puis appelle son `ExecuteAsync` avec les
données et les options préparées. Les instances de stratégies et leurs
services injectés ne sont pas sérialisés entre les processus.
Le consommateur fournit les points d'entrée nécessaires via `HookRunner.Cli`.
Le point d'entrée enfant reçoit les données préparées du superviseur sans
relire l'événement natif de l'agent. Il ne relance ni le cas d'usage complet
ni `Context`, ne réacquiert pas le verrou et ne modifie pas les compteurs.

Le résultat interne de l'exécution isolée distingue une terminaison normale,
une `HookValidationException` avec ses diagnostics et une erreur technique.
L'adaptateur restitue ces issues au contrat applicatif pour que `Context`
applique les règles communes. Ce protocole interne est distinct du code de
sortie public `0` ou `2` et de la réponse native à l'agent.
Une terminaison inattendue du processus enfant ou un résultat interne
inexploitable constitue une erreur technique, sauf arrêt demandé pour timeout.

Une protection à la frontière CLI traite les erreurs survenues avant le
contexte : configuration invalide, hook inconnu, entrée illisible, options
invalides ou échec de résolution des projets produisent un diagnostic et un
retour `2`. La stratégie n'est pas exécutée lorsque la préparation échoue.

L'identifiant de session fourni par l'agent est obligatoire. S'il est absent
ou inexploitable, l'invocation produit un diagnostic et un retour `2`, sans
exécuter la stratégie ni modifier les compteurs. HookRunner ne crée pas de
session de remplacement commune telle que `"?"`.

Une invocation suit ce déroulement :

1. Charger et valider la configuration, les paramètres de commande et l'événement reçu.
2. Sélectionner l'adaptateur et préparer les données neutres d'invocation.
3. Dans la CLI, construire `RunHookRequest` à partir de `RunHookArgs` et des
   données neutres, puis appeler `RunHookUseCase.ExecuteAsync` avec la requête
   et le token d'annulation.
4. Dans le cas d'usage, faire trouver la racine et les solutions, résoudre tous
   les projets demandés et préparer `HookOptions` avec le timeout effectif
   et les chemins résolus, au moyen des services applicatifs.
5. Sélectionner le hook demandé parmi les stratégies injectées, après
   validation des noms enregistrés.
6. Fournir cette stratégie à `Context.SetStrategy()` ainsi que les données
   d'invocation et les options au contexte, puis appeler `Context.ExecuteAsync`
   pour superviser l'appel isolé de
   `HookStrategy.ExecuteAsync(invocation, options, cancellationToken)`
   et traiter sa terminaison, son exception ou son timeout. Une invocation
   n'exécute qu'un seul hook.
7. Dans le contexte, mettre à jour le compteur si nécessaire, émettre les
   diagnostics et retourner le code entier.
8. Retourner ce code dans `RunHookResponse.ExitCode`, puis le transmettre
   via la CLI à l'exécutable consommateur.

## Compteurs et blocages

Les compteurs sont isolés par racine canonique de la solution à contrôler,
agent déclencheur, session de cet agent et nom de hook. Deux worktrees ont
des compteurs distincts.
L'événement ne fait pas partie de cette identité : deux événements appelant
le même hook dans la même racine de solution, pour le même agent et la même
session, partagent le compteur. Les noms des projets cibles et le timeout ne
font pas partie de cette identité.
Le compteur mesure les demandes de blocage émises depuis le dernier succès,
jusqu'au plafond, et non les blocages effectivement appliqués par l'hôte.
Le plafond reprend la valeur de référence de **trois demandes de blocage**, configurable.

| Situation | Comportement |
| --- | --- |
| Terminaison normale de la stratégie | Remise à zéro du compteur, trace du succès et retour `0`. |
| `HookValidationException`, plafond non atteint | Incrémentation du compteur, trace de la demande de blocage, diagnostic via `Console.Error.WriteLine(...)` et retour `2`. |
| `HookValidationException`, plafond déjà atteint | Compteur inchangé, trace uniquement et retour `0`. |
| Erreur technique hors timeout | Diagnostic via `Console.Error.WriteLine(...)` et retour `2`, indépendamment du plafond. Compteur métier inchangé. |
| Timeout avec arrêt du processus du hook et des processus suivis confirmé | Arrêt forcé, compteur inchangé, trace uniquement et retour `0`. |
| Timeout pendant l'attente de sérialisation, avant acquisition du verrou | Hook non démarré, compteur inchangé, trace uniquement et retour `0`. |
| Arrêt du processus du hook ou des processus suivis impossible à confirmer dans le délai technique borné, même après timeout | Diagnostic et retour `2`. Compteur métier inchangé. |
| Annulation extérieure pouvant être traitée avant l'arrêt du programme | Diagnostic et retour `2`. Compteur métier inchangé. |

Cette table décrit le résultat individuel d'un hook et donc de son invocation.
Les résultats de plusieurs commandes natives ne sont pas agrégés par HookRunner.

Avec un plafond de trois, les trois premières exceptions métier nécessitant des
corrections bloquent lorsque l'événement le permet. La quatrième ne bloque
plus pour ce motif. La stratégie continue néanmoins à être exécutée : un
succès doit pouvoir réinitialiser le compteur et une erreur technique reste
une erreur. Après un succès, un nouvel échec dispose à nouveau du plafond.

Cette politique reprend le comportement de
[`hook_common.py`](../hook_common.py) pour le succès, les corrections, le
plafond et le retour permissif après timeout. Comme `subprocess.run` en Python,
HookRunner impose un arrêt forcé du processus exécutant le traitement à
l'expiration du timeout. La supervision inclut également les processus
suivis ; l'impossibilité de confirmer leur arrêt reste une erreur technique.
Les scripts Python ne lisent pas l'événement : tout code de sortie de commande non nul
consomme une unité du compteur jusqu'au plafond et produit un retour `2`,
même si l'hôte ne peut pas bloquer sur cet événement. Le compteur Python
mesure donc les retours demandant un blocage, sans constater le blocage
effectif de l'agent. HookRunner conserve cette règle pour les exceptions
métier, y compris sur les événements non bloquants. Par exemple, trois
demandes émises sur un événement informatif épuisent le plafond de trois :
un échec métier ultérieur du même hook sur `Stop`, avec la même identité
de compteur, retourne `0`. Seul un succès remet le compteur à zéro.
La distinction explicite entre exception métier et
erreur technique ajoute un traitement centralisé des erreurs techniques,
que le script Python ne capture pas. Un retour `0` après un timeout ou après
épuisement du plafond autorise la poursuite sans constituer un succès métier ;
il ne remet pas le compteur à zéro.

Les compteurs sont conservés dans un fichier d'état distinct de la
configuration, situé dans le projet consommateur. Ce fichier mémorise les
nombres de demandes de blocage entre deux lancements de HookRunner.
Un seul fichier conserve les compteurs de tous
les hooks du projet consommateur ; chaque identité de compteur garde sa
propre entrée. Le dossier et le nom de ce fichier sont choisis une seule fois
dans la configuration globale du projet consommateur, sans surcharge par hook.
Le paramètre `HookRunner:StateFilePath` est obligatoire. Son chemin est
relatif à la racine de la solution ; par exemple,
`Hooks/state.json` désigne le fichier du projet consommateur dans
la solution courante. Si le chemin n'est pas fourni, une exception de
configuration maîtrisée est levée et traitée à la frontière CLI : diagnostic
et retour `2`, sans exécuter de hook ni modifier les compteurs.
Si ce fichier n'existe pas, HookRunner le crée automatiquement, avec des
compteurs initialement à zéro. Un fichier existant corrompu reste une erreur
et n'est pas remplacé par un fichier vide.
La persistance doit garantir des écritures atomiques et protéger
la mise à jour des compteurs contre les invocations concurrentes.
Pour une même identité de compteur, l'exécution complète des hooks est
sérialisée, pas seulement les écritures du compteur. Les identités distinctes
restent exécutables en parallèle. Chaque invocation ne sélectionnant qu'un
hook, elle n'acquiert qu'une seule identité de compteur.
Le superviseur détient ce verrou pendant l'exécution isolée et la phase
bornée de confirmation d'arrêt. Un timeout ne laisse pas une stratégie C#
ignorant l'annulation retenir indéfiniment le verrou.
Une erreur de lecture ou d'écriture de l'état produit un diagnostic et un retour
`2` ; un état corrompu n'est pas remplacé silencieusement par des compteurs à zéro.

## Timeout, processus et prompts

Le timeout possède une valeur par défaut de **120 secondes**, configurable.
Une valeur spécifique peut être fournie par `--timeout` dans la commande
native de l'agent et remplace cette valeur par défaut pour l'invocation.
HookRunner prépare le timeout effectif dans `HookOptions` avant l'appel du
hook. `Context` en impose l'échéance depuis le processus superviseur, au
travers du contrat d'exécution isolée ; le token transmis à la stratégie
permet aussi une annulation coopérative. Le timeout fourni par la commande accepte uniquement un nombre entier de secondes
strictement supérieur à zéro. Une valeur nulle, négative, non entière ou
non numérique est une erreur de configuration : diagnostic et retour `2`, sans
exécuter le hook. Les valeurs ne pouvant pas être représentées par le moteur
sont également refusées comme invalides.
Le timeout effectif couvre
l'attente d'acquisition du verrou de sérialisation puis l'exécution complète
du seul hook de l'invocation, y compris le démarrage du processus enfant,
ses commandes et ses prompts successifs.
Le budget démarre avant l'attente du verrou et n'est pas remis à zéro lors
de son acquisition. S'il expire avant l'acquisition, la stratégie ne démarre
pas : trace uniquement, retour `0` et compteur inchangé.
Son expiration entraîne l'arrêt forcé du processus exécutant le hook et des
processus suivis, puis une trace et un retour `0` si leur arrêt est confirmé,
sans diagnostic de blocage ni modification du compteur. Le superviseur
n'attend pas que le code C# observe le token et n'accorde pas de délai
supplémentaire d'exécution coopérative. Un résultat métier reçu après
l'expiration ne transforme pas le timeout en succès et ne modifie pas le
compteur. Le contexte distingue ce timeout des autres erreurs techniques.
Une impossibilité de confirmer l'arrêt dans le délai technique borné décrit
ci-dessous prime sur ce retour permissif : diagnostic et retour `2`, compteur inchangé.
Une annulation extérieure, lorsqu'elle peut être traitée avant l'arrêt du
programme, produit également un diagnostic et un retour `2`, compteur inchangé.

Le timeout définit la durée maximale autorisée pour l'attente du verrou et
l'exécution du hook. L'isolation du hook C# dans un processus enfant permet
au superviseur d'en imposer l'arrêt même si la stratégie ignore entièrement
l'annulation. Le token reste disponible pour les opérations qui le prennent
en charge, mais ne constitue pas le mécanisme garantissant cette limite.
L'infrastructure supervise les processus de commandes et de prompts de façon
à pouvoir les arrêter même lorsque le processus du hook est tué ; tuer
seulement un shell sans gérer les processus qu'il a lancés ne suffit pas.
Pour un prompt, la confirmation porte sur la terminaison du processus CLI
local et des processus suivis. Le timeout déclenche leur arrêt ; la
confirmation est obtenue en constatant leur terminaison. L'échec de cette
confirmation est un incident technique, pas une impossibilité systématique.
Cette confirmation ne garantit pas l'arrêt d'un éventuel traitement côté
service distant.

L'arrêt demandé à l'échéance et sa confirmation nécessitent un traitement
technique dont la durée peut s'ajouter au budget. Cette phase est bornée par
un délai fini défini par l'infrastructure et ne prolonge pas l'exécution
autorisée du hook ; aucune attente de terminaison n'est illimitée. L'invocation
peut donc dépasser brièvement le timeout pour confirmer l'arrêt, émettre la
trace ou le diagnostic et restituer le code de sortie. Le verrou est libéré
à la fin de ce traitement, y compris en cas d'échec de confirmation d'arrêt.
Un arrêt forcé ne garantit ni l'exécution des blocs `finally` du hook ni
l'annulation des modifications déjà effectuées dans les fichiers.

Cette décision reprend le principe d'arrêt forcé de
`subprocess.run(..., timeout=...)` utilisé dans `hook_common.py` et remplace
l'attente coopérative potentiellement illimitée initialement retenue.
Le coût accepté pour imposer cette limite est une frontière de processus
et un protocole interne de résultat ; le superviseur conserve le verrou
et la gestion des compteurs.

Les prompts sont réellement exécutés via les CLI Codex et Claude installées
sur la machine. Le chemin des exécutables et le modèle sont configurables.
L'agent qui exécute les prompts peut différer de celui qui déclenche le hook.
Selon la stratégie, un prompt peut demander une analyse ou une modification
des fichiers.

Chaque exécution de prompt utilise une session dédiée. Un marqueur hérité
par les processus enfants empêche ces sessions de réexécuter les hooks
HookRunner et de provoquer une boucle d'invocations.
Les commandes et agents enfants sont attendus avant de considérer le
hook terminé ; une impossibilité d'établir leur arrêt est signalée.

Références : [Codex non interactif](https://learn.chatgpt.com/docs/non-interactive-mode)
et [Claude programmatique](https://code.claude.com/docs/en/headless).

## Événements et adaptation des protocoles

Le projet consommateur peut déclencher HookRunner depuis les événements de
son agent compatibles avec un code de sortie et des diagnostics textuels.
Le moteur n'est pas limité à `Stop`.

L'adaptateur connaît l'agent déclencheur et l'événement. Il lit l'entrée native
et prépare les données neutres d'invocation. La réponse de HookRunner se limite
au code entier fourni par le contexte (`0` ou `2`) et aux diagnostics textuels
écrits sur la sortie d'erreur standard avec `Console.Error.WriteLine(...)`.
Les réponses structurées natives, notamment les décisions de
permission et les chemins de worktree, sont hors périmètre.

Les possibilités de blocage dépendent de l'événement. HookRunner ne peut pas
forcer un événement informatif à bloquer l'agent. Il ne peut pas non plus
produire lui-même une décision s'il ne démarre pas ou si l'hôte l'interrompt
avant qu'il puisse répondre.

Le retour au chatbot suit le fonctionnement de `hook_common.py` :

- l'agent lance le programme consommateur depuis sa configuration native ;
- le programme lit les données d'événement sur son entrée standard ;
- le hook réalise ses opérations, commandes et/ou prompts ;
- si des corrections sont requises et que le plafond le permet, le contexte
  émet les diagnostics et consignes sur la sortie d'erreur, puis retourne `2` ;
- un succès produit une trace, remet le compteur à zéro et retourne `0` ;
- le plafond atteint ou un timeout traité selon les règles précédentes
  produisent une trace et un retour `0`, sans remise à zéro du compteur.

Cette communication n'exige pas que le chatbot découvre les hooks comme
des outils ni qu'il reçoive un message à chaque succès. La manière dont
l'agent restitue les diagnostics dépend de l'événement utilisé.

Dans le script Python, `rules_prompt` est un texte de consignes joint au
diagnostic : il ne déclenche pas d'appel de modèle. HookRunner étend ce
fonctionnement avec la possibilité d'exécuter réellement des prompts à
l'intérieur d'une stratégie, puis de décider du résultat du hook.
Les différences déjà validées avec le Python, notamment les erreurs
techniques, l'isolation des compteurs, la session obligatoire, la concurrence
et la confirmation d'arrêt des processus, restent applicables.

Le câblage des événements appartient au projet consommateur, dans sa
configuration native de hooks. Le fichier `hooks.json` est la configuration
native de Codex : il associe les événements à des commandes qui sélectionnent
les hooks à exécuter. Il ne s'agit pas d'un fichier de routage propre à HookRunner.
Les fichiers reconnus nativement dépendent de l'agent :
Codex utilise notamment `.codex/hooks.json`, tandis que Claude déclare ses
hooks de projet dans ses fichiers `settings.json`.
Le format de chaque configuration native appartient à l'agent concerné.
La commande sélectionne le hook par son nom et indique l'agent déclencheur
avec `--agent codex` ou `--agent claude`. Les options de timeout et de
projets cibles sont fournies dans cette même commande par `--timeout` et
`--projects`, puis préparées et transmises au hook dans `HookOptions` à
chaque exécution.

Références : [hooks Codex](https://learn.chatgpt.com/docs/hooks) et
[hooks Claude](https://code.claude.com/docs/en/hooks).

## Configuration

Le projet consommateur fournit un fichier **`appsettings.json`** unique.
Les paramètres de la bibliothèque se trouvent dans sa section `HookRunner`.
Il n'existe pas de second fichier `appsettings.json` propre à la bibliothèque.
La valeur par défaut du timeout figure dans cette section :

```json
{
  "HookRunner": {
    "TimeoutSeconds": 120,
    "StateFilePath": "Hooks/state.json"
  }
}
```

Le fichier contient des valeurs globales et des surcharges par hook,
notamment :

- le plafond de blocages ;
- la valeur par défaut du timeout, remplacée lorsqu'un timeout est fourni
  dans la commande avec `--timeout` ;
- l'agent exécutant les prompts et le modèle ;
- les chemins des exécutables.

`appsettings.json` est chargé par défaut depuis le dossier du programme
consommateur compilé. Son emplacement est distinct de la racine de la solution
à contrôler et des dossiers des projets cibles. Le consommateur fournit ce
fichier avec son programme ; le dossier depuis lequel l'agent lance celui-ci
ne change pas la configuration lue.
Le timeout fourni avec `--timeout` remplace la valeur par défaut de la
configuration pour cette invocation et figure dans l'objet `HookOptions`
transmis à la stratégie.

L'emplacement du fichier qui conserve les compteurs est un paramètre global
du projet consommateur. Il ne peut pas être redéfini pour chaque hook.
Le fichier est situé dans le projet consommateur et est créé automatiquement
s'il est absent. `StateFilePath` est obligatoire et est résolu depuis la racine
de la solution à contrôler. Son absence déclenche une exception de configuration
maîtrisée, convertie en diagnostic et code `2` avant l'exécution d'un hook.

Les compteurs sont des données d'exécution et ne sont pas écrits dans
`appsettings.json`.

## Vérification et alignement du dépôt

Les tests doivent vérifier :

- les compteurs, leur remise à zéro, leur partage entre événements et leur
  consommation sur les événements non bloquants ;
- la sélection d'un seul hook par commande, l'unicité des noms et leur
  comparaison sans distinction entre majuscules et minuscules ;
- l'enregistrement des hooks concrets des assemblies explicitement choisies
  via Scrutor, et leur disponibilité dans la collection injectée au cas d'usage ;
- la construction de `RunHookRequest` avec ses arguments typés et les données
  neutres, la préparation des options par le cas d'usage, sa délégation à
  `Context` et la transmission de `RunHookResponse.ExitCode` à la CLI ;
- le rejet d'une commande sans nom de hook ou d'une entrée sans session
  exploitable, et la sélection explicite de l'agent déclencheur ;
- la création du fichier des compteurs, le rejet d'une configuration sans
  `StateFilePath` et sa résolution depuis la racine de la solution ;
- la limite de 8 000 caractères des sorties incluses dans les diagnostics,
  sans troncature des consignes ni des résultats analysés par la stratégie ;
- le timeout pendant l'attente de sérialisation, sans démarrage du hook ;
- l'arrêt forcé du processus d'un hook qui ignore le token, dès expiration
  du budget, sans attente coopérative supplémentaire ;
- l'inclusion du démarrage du processus enfant dans le même budget, sans
  remise à zéro du timeout après acquisition du verrou ou lancement du hook ;
- l'arrêt des commandes et prompts suivis même après l'arrêt forcé du
  processus du hook, y compris les descendants d'un shell ;
- la confirmation de terminaison des processus CLI locaux de prompts,
  sans supposer une confirmation de l'arrêt d'un service distant ;
- la confirmation d'arrêt dans un délai technique borné, le retour `2` en
  cas d'échec et la libération du verrou dans les deux cas ;
- le compteur inchangé après timeout, même si un résultat métier arrive
  tardivement, et l'absence de mise à jour du compteur par le processus enfant ;
- la restitution des succès, exceptions métier et erreurs techniques depuis
  le processus enfant, et le traitement d'une terminaison inattendue ou d'un
  résultat interne invalide comme erreur technique hors timeout ;
- le remplacement du timeout configuré par `--timeout`, son utilisation par
  le contexte et sa transmission à la stratégie, ainsi que le rejet des
  valeurs invalides ;
- la transmission d'un objet `HookOptions` propre à chaque invocation à
  `ExecuteAsync`, contenant le timeout effectif, la racine, le dossier de
  travail par défaut et les projets résolus avec leur nom, fichier et dossier ;
- la résolution des noms `.csproj` sans extension, sans distinction de casse,
  parmi les références de toutes les solutions `.sln`/`.slnx` du premier
  dossier trouvé par recherche ascendante depuis le dossier de l'agent ;
- l'arrêt de cette recherche au dossier retenu, même avec des solutions
  imbriquées, et le rejet d'une invocation sans solution trouvée ;
- la déduplication d'un même fichier projet référencé par plusieurs solutions,
  et le rejet de deux fichiers distincts correspondant à un nom demandé,
  y compris lorsqu'ils ne diffèrent que par la casse ;
- l'absence de blocage pour une ambiguïté portant sur un nom non demandé ;
- le rejet des projets demandés introuvables, absents du disque ou situés
  hors de la racine, ainsi que des projets non référencés ou non C# ;
- la suppression des espaces autour des noms et la déduplication des noms
  demandés, ainsi que le rejet des valeurs et éléments vides et des jokers ;
- le diagnostic et le retour `2` sans exécution partielle ni modification des
  compteurs lorsque les options ou la résolution d'un projet sont invalides ;
- l'utilisation des chemins résolus par les commandes shell et les prompts,
  avec choix des dossiers de travail et de l'ordre des opérations par le hook,
  et une seule exécution du hook pour plusieurs projets cibles ;
- l'absence de sélection implicite et la racine comme dossier de travail
  par défaut lorsque `--projects` n'est pas fourni ;
- le chargement de la configuration depuis le dossier du programme compilé ;
- le traitement des exceptions métier et techniques après épuisement du
  plafond, ainsi que le retour `0` sans remise à zéro lors d'un timeout ;
- l'annulation, la confirmation d'arrêt des processus, les résultats des
  processus et la concurrence du stockage ;
- les codes `0` et `2` et les diagnostics écrits via `Console.Error` pour les
  événements Codex/Claude pris en charge.

Les tests déterministes ne doivent pas nécessiter d'appels réels à un modèle.
L'implémentation sera développée en TDD, sans usage du mot-clé C# `var`,
avec tests unitaires et d'intégration couvrant ces comportements et
l'isolation des compteurs entre racines de solution, agents, sessions et hooks,
ainsi que leur partage pour une même identité malgré des options différentes.

Lors de l'implémentation, la CI et la documentation de contribution devront
être alignées sur .NET 11, et la validation devra couvrir Windows, Linux et
macOS. L'arbre courant ne contient pas de workflow de compilation ou de tests
.NET ; le workflow présent contrôle les noms de branches. Le cadrage signalait
un SDK local RC de .NET 11. Une validation avec le SDK stable sera nécessaire
avant de conclure à la compatibilité de l'implémentation.
