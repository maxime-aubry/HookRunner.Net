# Outils CI

[Retour à l'index](README.md). La [documentation CI](../ci.md) décrit
les commandes de validation et les rapports ; cette page décrit leur code C#.

## Programme de contrôle

[Source](../../tools/HookRunner.CI/Hosting/Program.cs).
`Main` route `coverage`, `audit` et `docs`. Les erreurs de lecture ou de
structure prises en charge sont écrites sur stderr et donnent `1`.

- [CoverageCheck](../../tools/HookRunner.CI/Quality/Coverage/CoverageCheck.cs) découvre les projets de production, leurs inventaires et
  les rapports Coverlet. Il écrit `gate.json`, `gate.md` et la synthèse stdout.
- [AuditCheck](../../tools/HookRunner.CI/Quality/Audit/AuditCheck.cs) charge le rapport NuGet et écrit les vulnérabilités bloquantes.
- [DocumentationLinkCheck](../../tools/HookRunner.CI/Quality/Documentation/DocumentationLinkCheck.cs) résout les liens Markdown locaux relativement à leur
  document et exige une cible existante. Les ancres et URL externes ne sont
  pas contrôlées.

## Couverture

[CoverageGate](../../tools/HookRunner.CI/Quality/Coverage/CoverageGate.cs)
lit les lignes exécutables du PDB portable avec `ReadSequencePoints`, en
excluant les points cachés et les sources sous `obj/`. `Evaluate` fusionne
les exécutions observées par toutes les suites et les compare à cet inventaire
indépendant : l'absence d'un document dans le collecteur ne peut pas masquer
une ligne à couvrir. Les branches des rapports participent aussi au verdict.
La lecture PDB est déléguée à
[PdbSequencePointReader](../../tools/HookRunner.CI/Quality/Coverage/PdbSequencePointReader.cs),
et la lecture et fusion des observations à
[CoverletCoverageReader](../../tools/HookRunner.CI/Quality/Coverage/CoverletCoverageReader.cs).
Les identifiants de lignes et de branches sont typés avant leur formatage en diagnostics.
La racine déterministe `/_/` des PDB de CI est résolue dans la racine locale
du dépôt avant comparaison avec les chemins JSON Coverlet. Les noms de fichiers
ou suffixes seuls ne suffisent jamais à identifier un document.

[CoverageResult](../../tools/HookRunner.CI/Quality/Coverage/CoverageResult.cs)
expose l'assembly, le statut et les points manquants. Un inventaire vide donne
`not-applicable` ; sinon toutes les lignes et branches doivent être couvertes.

[ProductionInventory](../../tools/HookRunner.CI/Quality/Coverage/ProductionInventory.cs)
valide les champs du fichier MSBuild et nomme les chemins de l'assembly et du PDB.
Un inventaire incomplet produit un diagnostic et le code `1`, comme les rapports
d'audit invalides ou signalant un problème de collecte.
[CoverageReportWriter](../../tools/HookRunner.CI/Quality/Coverage/CoverageReportWriter.cs)
possède l'écriture des synthèses JSON et Markdown.

## Audit NuGet

[AuditGate](../../tools/HookRunner.CI/Quality/Audit/AuditGate.cs)
parcourt les dépendances directes et transitives d'un rapport version `1`.
`BlockingVulnerabilities` retient High et Critical sans distinction de casse.
Une version inconnue, l'absence de projets ou un problème de collecte est
refusé. L'absence de frameworks pour un projet sans signalement est acceptée.
Le parcours et la validation du format NuGet appartiennent à
[NuGetAuditReader](../../tools/HookRunner.CI/Quality/Audit/NuGetAuditReader.cs) ;
`AuditGate` conserve la politique de sévérité et les diagnostics bloquants.

Les commentaires XML des points d'entrée décrivent leurs entrées, sorties et erreurs.
