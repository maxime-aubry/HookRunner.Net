# HookRunner

HookRunner exécute des hooks en réponse aux événements émis par des agents IA.

## Language

**Projet consommateur** :
Projet de la solution utilisateur qui définit les hooks concrets et fournit
le point d'entrée permettant de les exécuter avec HookRunner.

**Projet cible** :
Projet de la solution utilisateur sélectionné par un hook pour y exécuter
des commandes ou des prompts.

**Événement** :
Événement émis par un agent IA, par exemple `Stop`.

**Hook** :
Traitement concret identifié par un nom, par exemple `remove_var_keyword`.
Chaque hook constitue une stratégie concrète.
_Avoid_ : Événement

**Stratégie** :
Contrat d'exécution commun aux hooks, dont chaque hook concret est une réalisation.

**Contexte** :
Objet qui reçoit une stratégie et en déclenche l'exécution.

**Invocation** :
Occurrence d'exécution d'un seul hook, déclenchée par une commande associée à un événement.
_Avoid_ : Événement, hook

**Compteur de demandes de blocage** :
Nombre de demandes de blocage émises pour un hook depuis son dernier succès,
dans une solution, pour un agent déclencheur et une session donnés, jusqu'au plafond configuré.
Il ne mesure pas les blocages effectivement appliqués par l'agent.
_Avoid_ : Compteur de blocages effectifs
