# HookRunner.Cli

[Retour à l'index](README.md). Cette bibliothèque est appelée par le programme
consommateur et compose Application avec les adaptateurs d'Infrastructure.

## HookRunnerHost

[Source](../../src/HookRunner.Cli/Hosting/HookRunnerHost.cs).
`RunAsync` construit les services puis choisit le superviseur ou l'enfant
selon le premier argument. Il libère le conteneur de manière asynchrone.
Toute exception, y compris lors de cette libération, est diagnostiquée sur
stderr et donne le code `2`.

`RunAsync` se consacre à cette gestion d'erreur. `RunComposedAsync` possède
la composition, le routage et la libération, tous couverts par le même `catch`.

`BuildServiceProvider` enregistre l'exécuteur isolé, le contexte transitoire,
le cas d'usage et un préparateur d'options à résolution différée. Le callback
du consommateur s'exécute après ces valeurs par défaut et peut les remplacer.
Il est réutilisé dans chaque processus ; il doit donc reconstruire les mêmes
stratégies et leurs dépendances sans dépendre d'instances du superviseur.

`RunSupervisorAsync` analyse la commande, lit stdin jusqu'à sa fin, adapte
l'événement Codex puis appelle `RunHookUseCase`. Il propage le code de réponse.
La configuration différée n'est résolue que sur ce parcours.

## Analyse des arguments

[RunHookArgumentsParser](../../src/HookRunner.Cli/Arguments/RunHookArgumentsParser.cs)
accepte `execute <nom> --agent codex`, avec `--timeout` et `--projects`
facultatifs. `ParseOptions` exige des paires nom-valeur uniques et refuse les
options inconnues. Le timeout absent reste `null`. Les projets sont séparés
par virgules, avec retrait des espaces périphériques ; leur résolution ne
relève pas de cet analyseur.

[TimeoutSecondsParser](../../src/HookRunner.Cli/Arguments/TimeoutSecondsParser.cs)
valide un entier positif Int32 en culture invariante, sans signe ni fraction.
La commande et la configuration utilisent la même validation.

## Configuration

[HookRunnerConfigurationLoader](../../src/HookRunner.Cli/Configuration/HookRunnerConfigurationLoader.cs)
charge obligatoirement `appsettings.json` depuis le dossier fourni à `Load`.
L'hôte fournit `AppContext.BaseDirectory` lors de la résolution différée du
préparateur, indépendamment du dossier de lancement. Le loader possède et
libère la configuration après lecture.

`ReadConfiguration` lit le timeout (défaut 120 secondes), le chemin d'état et
`MaxBlockingRequests` (défaut trois). `ReadHookLimits` collecte les surcharges
`Hooks:<nom>:MaxBlockingRequests`, sans distinction de casse. Les plafonds sont
des entiers non négatifs ; toute surcharge de `StateFilePath` par hook est
refusée, même pour un hook non sélectionné.

[HookRunnerConfiguration](../../src/HookRunner.Cli/Configuration/HookRunnerConfiguration.cs)
transporte uniquement ces valeurs. Son chemin d'état reste non résolu ; un chemin
absent devient une chaîne vide qui sera refusée par le préparateur d'options.

## Point d'entrée enfant

[ChildHookEntryPoint](../../src/HookRunner.Cli/Execution/ChildHookEntryPoint.cs)
exige le marqueur interne et deux chemins. `ExecuteHookAsync` désérialise
la requête préparée, résout la stratégie et l'appelle directement. Il ne lit
ni stdin ni la configuration et ne recrée pas d'orchestration applicative.
Une `HookValidationException` devient un résultat de validation ; les autres erreurs deviennent un résultat technique versionné.
La lecture JSON est isolée dans `ReadRequestAsync` ; `ExecuteRequestAsync`
sélectionne et exécute la stratégie. `ExecuteHookAsync` se consacre à la
traduction des exceptions.

`RunAsync` écrit ce résultat et rend `0` si l'écriture réussit. Une erreur
d'écriture remonte à l'hôte et donne `2`. Les contrats XML des méthodes
explicitent cette distinction entre succès du transport et succès du hook.
