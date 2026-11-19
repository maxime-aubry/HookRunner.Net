import sys

from hook_common import run_check_hook

HOOK_NAME = "stylelint"

COMMAND = 'npx stylelint "TimeTracker/Components/**/*.css" "TimeTracker/wwwroot/app.css"'

RULES_PROMPT = """
Ne pas mettre de couleur en dur dans le CSS et réutiliser les variables de :root de app.css.
Créer une nouvelle variable seulement quand c'est nécessaire, ou bien réutiliser une couleur existante, ne pas créer une nouvelle couleur si une couleur déjà existante est très proche.
"""

if __name__ == "__main__":
    sys.exit(run_check_hook(HOOK_NAME, COMMAND, RULES_PROMPT))
