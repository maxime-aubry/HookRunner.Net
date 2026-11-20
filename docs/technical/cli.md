# HookRunner.Cli

[Retour à l'index](README.md). Cette bibliothèque est appelée par le programme
consommateur et compose Application avec les adaptateurs d'Infrastructure.

## HookRunnerHost

[Source](../../src/HookRunner.Cli/Hosting/HookRunnerHost.cs).
`RunAsync` construit les services puis choisit le superviseur ou l'enfant
selon le premier argument. Il libère le conteneur de manière asynchrone.
Toute exception, y compris lors de cette libération, est diagnostiquée sur
stderr et donne le code `2`.

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

[HookRunnerConfiguration](../../src/HookRunner.Cli/Configuration/HookRunnerConfiguration.cs)
charge obligatoirement `appsettings.json` depuis `AppContext.BaseDirectory`,
indépendamment du dossier de lancement. `Load` lit le timeout, avec défaut
à 120 secondes, et le chemin d'état. Un chemin absent sera refusé par le
préparateur d'options ; la configuration est libérée après lecture.

## Point d'entrée enfant

[ChildHookEntryPoint](../../src/HookRunner.Cli/Execution/ChildHookEntryPoint.cs)
exige le marqueur interne et deux chemins. `ExecuteHookAsync` désérialise
la requête préparée, résout la stratégie et l'appelle directement. Il ne lit
ni stdin ni la configuration et ne recrée pas d'orchestration applicative.
Les erreurs deviennent un résultat technique versionné.

`RunAsync` écrit ce résultat et rend `0` si l'écriture réussit. Une erreur
d'écriture remonte à l'hôte et donne `2`. Les contrats XML des méthodes
explicitent cette distinction entre succès du transport et succès du hook.
