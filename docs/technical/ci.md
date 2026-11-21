# Outils CI

[Retour à l'index](README.md). La [documentation CI](../ci.md) décrit
les commandes de validation et les rapports ; cette page décrit leur code C#.

## Programme de contrôle

[Source](../../tools/HookRunner.CI/Hosting/Program.cs).
`Main` route `coverage`, `audit` et `docs`. Les erreurs de lecture ou de
structure prises en charge sont écrites sur stderr et donnent `1`.

- `CheckCoverage` découvre les projets de production, leurs inventaires et
  les rapports Coverlet. Il écrit `gate.json`, `gate.md` et la synthèse stdout.
- `CheckAudit` charge le rapport NuGet et écrit les vulnérabilités bloquantes.
- `CheckDocumentation` résout les liens Markdown locaux relativement à leur
  document et exige une cible existante. Les ancres et URL externes ne sont
  pas contrôlées.

## Couverture

[CoverageGate](../../tools/HookRunner.CI/Quality/Coverage/CoverageGate.cs)
lit les lignes exécutables du PDB portable avec `ReadSequencePoints`, en
excluant les points cachés et les sources sous `obj/`. `Evaluate` fusionne
les exécutions observées par toutes les suites et les compare à cet inventaire
indépendant : l'absence d'un document dans le collecteur ne peut pas masquer
une ligne à couvrir. Les branches des rapports participent aussi au verdict.
La racine déterministe `/_/` des PDB de CI est résolue dans la racine locale
du dépôt avant comparaison avec les chemins JSON Coverlet. Les noms de fichiers
ou suffixes seuls ne suffisent jamais à identifier un document.

[CoverageResult](../../tools/HookRunner.CI/Quality/Coverage/CoverageResult.cs)
expose l'assembly, le statut et les points manquants. Un inventaire vide donne
`not-applicable` ; sinon toutes les lignes et branches doivent être couvertes.

## Audit NuGet

[AuditGate](../../tools/HookRunner.CI/Quality/Audit/AuditGate.cs)
parcourt les dépendances directes et transitives d'un rapport version `1`.
`BlockingVulnerabilities` retient High et Critical sans distinction de casse.
Une version inconnue, l'absence de projets ou un problème de collecte est
refusé. L'absence de frameworks pour un projet sans signalement est acceptée.

Chaque méthode, y compris celles du point d'entrée, décrit ses entrées,
sorties et erreurs dans ses commentaires XML.
