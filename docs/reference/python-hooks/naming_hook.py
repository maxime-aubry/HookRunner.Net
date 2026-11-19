import sys

from hook_common import run_check_hook

HOOK_NAME = "naming"

COMMAND = 'python3 checks/naming_check.py'

RULES_PROMPT = """
Les pages (.razor / .razor.css) se terminent par « Page » et sont dans TimeTracker/Components/Pages (exceptions : Error, Auth).
Les layouts se terminent par « Layout » et sont dans TimeTracker/Components/Layout (exceptions : NavMenu, ReconnectModal, ManageLayout).
Les dialogues se terminent par « Dialog » et sont dans TimeTracker/Components/Dialogs.
Renommer ou déplacer les fichiers fautifs, et mettre à jour leurs références (typeof, balises, namespaces).
"""

if __name__ == "__main__":
    sys.exit(run_check_hook(HOOK_NAME, COMMAND, RULES_PROMPT))
