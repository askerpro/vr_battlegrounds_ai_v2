"""Импорт не получает доступ к основному checkout по старому lock-файлу."""
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import time
import unittest


class ImportAccessTests(unittest.TestCase):
    def test_legacy_owner_cannot_authorize_main_checkout(self):
        script = Path(__file__).resolve().parents[1] / "import_environment_files.py"
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            subprocess.run(["git", "--no-pager", "init", "--quiet"], cwd=root, check=True)
            lock = root / "tmp/unity-lock/info"
            lock.parent.mkdir(parents=True)
            lock.write_text("owner=fixture\nuntil=" + str(int(time.time()) + 600))
            plan = root / "plan.json"
            plan.write_text(json.dumps({"conflicts": [], "packages": list(range(13)), "files": []}))
            result = subprocess.run([sys.executable, str(script), str(plan), "--owner", "fixture"],
                                    cwd=root, capture_output=True)
            self.assertNotEqual(result.returncode, 0, "legacy lock authorized writes in main checkout")
            self.assertFalse((root / "copy-progress.json").exists())


if __name__ == "__main__":
    unittest.main()
