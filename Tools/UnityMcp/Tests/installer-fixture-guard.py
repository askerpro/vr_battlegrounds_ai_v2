"""Подмена только OS inventory в собственной runtime-копии для installer fixtures."""
from pathlib import Path
import json
import shutil

def configure_fixture_inventory(runtime, project):
    runtime, project = Path(runtime).resolve(), Path(project).resolve()
    assert runtime.parent.name == "editor-broker" and runtime.parent.parent.name == ".agent-state"
    assert runtime.is_relative_to(project.parent), "runtime не принадлежит этому fixture"
    package = runtime / "editor_broker"
    shutil.copy2(package / "client_guard.py", package / "client_guard_source.py")
    (package / "client_guard.py").write_text('''# Только собственный installer fixture; production guard ниже импортируется без изменений.
from pathlib import Path
import json
import sys
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from editor_broker import client_guard_source as guard
def fixture_roots():
    values = json.loads(Path(__file__).with_name("fixture-native-roots.json").read_text())
    if values == "unknown":
        raise ValueError("fixture: native inventory unknown")
    return [Path(value) for value in values]
guard.unity_project_roots = fixture_roots
if __name__ == "__main__":
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")
    raise SystemExit(guard.main())
''', encoding="utf-8")
    inventory = package / "fixture-native-roots.json"
    inventory.write_text(json.dumps([]), encoding="utf-8")
    return inventory
