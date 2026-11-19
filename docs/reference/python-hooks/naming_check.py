import sys
from dataclasses import dataclass, field
from pathlib import Path

PROJECT_DIR = Path(__file__).parent.parent
SOURCE_DIR = PROJECT_DIR / "TimeTracker"
COMPONENTS_DIR = SOURCE_DIR / "Components"
IGNORED_DIRS = {"bin", "obj", "wwwroot"}
COMPONENT_EXTENSIONS = (".razor.css", ".razor")


@dataclass
class NamingRule:
    suffix: str
    directory: Path
    exceptions: set[str] = field(default_factory=set)


NAMING_RULES = [
    NamingRule("Page", COMPONENTS_DIR / "Pages", {"Error", "Auth"}),
    NamingRule("Layout", COMPONENTS_DIR / "Layout", {"NavMenu", "ReconnectModal", "ManageLayout"}),
    NamingRule("Dialog", COMPONENTS_DIR / "Dialogs"),
]


def get_component_name(file_path: Path) -> str | None:
    for extension in COMPONENT_EXTENSIONS:
        if file_path.name.endswith(extension):
            return file_path.name[: -len(extension)]
    return None


def is_exception(component_name: str, rule: NamingRule) -> bool:
    return component_name in rule.exceptions


def find_rule_errors(file_path: Path, component_name: str, rule: NamingRule) -> list[str]:
    if is_exception(component_name, rule):
        return []

    relative_path = file_path.relative_to(PROJECT_DIR)
    relative_directory = rule.directory.relative_to(PROJECT_DIR)
    has_suffix = component_name.endswith(rule.suffix)
    is_in_directory = file_path.parent == rule.directory

    if has_suffix and not is_in_directory:
        return [f"{relative_path} : se termine par « {rule.suffix} », il doit être dans {relative_directory}"]
    if is_in_directory and not has_suffix:
        return [f"{relative_path} : est dans {relative_directory}, son nom doit se terminer par « {rule.suffix} »"]
    return []


def is_ignored(file_path: Path) -> bool:
    return any(part in IGNORED_DIRS for part in file_path.relative_to(SOURCE_DIR).parts)


def find_naming_errors() -> list[str]:
    naming_errors = []
    for file_path in sorted(SOURCE_DIR.rglob("*")):
        if is_ignored(file_path):
            continue
        component_name = get_component_name(file_path)
        if component_name is None:
            continue
        for rule in NAMING_RULES:
            naming_errors += find_rule_errors(file_path, component_name, rule)
    return naming_errors


if __name__ == "__main__":
    naming_errors = find_naming_errors()
    for naming_error in naming_errors:
        print(naming_error)
    sys.exit(1 if naming_errors else 0)
