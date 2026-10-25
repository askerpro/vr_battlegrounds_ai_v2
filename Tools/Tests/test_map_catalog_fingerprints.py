"""Проверка отпечатков MapRuntimeCatalog без Unity (Tools/LevelDesign/map_catalog_fingerprints.py)."""

from pathlib import Path
import subprocess
import sys
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "Tools" / "LevelDesign" / "map_catalog_fingerprints.py"
CATALOG = "Assets/Data/Maps/MapRuntimeCatalog.asset"

GUID_MAP = "11111111111111111111111111111111"
GUID_MODES = "22222222222222222222222222222222"
GUID_PRESET = "33333333333333333333333333333333"
GUID_SCENE = "44444444444444444444444444444444"
GUID_CATALOG = "55555555555555555555555555555555"
GUID_OTHER = "66666666666666666666666666666666"

STALE = "0" * 64


def meta(guid):
    return f"fileFormatVersion: 2\nguid: {guid}\n"


def catalog(content=STALE, arsenal=STALE):
    return ("%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!114 &11400000\nMonoBehaviour:\n"
            "  m_Name: MapRuntimeCatalog\n"
            f"  _modes: {{fileID: 11400000, guid: {GUID_MODES}, type: 2}}\n"
            "  _content:\n"
            f"  - Map: {{fileID: 11400000, guid: {GUID_MAP}, type: 2}}\n"
            "    ScenePath: Assets/Scenes/Test.unity\n"
            f"    ContentFingerprint: {content}\n"
            f"    ArsenalFingerprint: {arsenal}\n")


class MapCatalogFingerprintTests(unittest.TestCase):
    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.repo = Path(self._tmp.name)
        self.git("init", "-q")
        self.git("config", "user.email", "test@example.invalid")
        self.git("config", "user.name", "test")
        self.git("config", "core.autocrlf", "false")
        files = {
            "Assets/Scenes/Test.unity": "%YAML 1.1\n--- !u!1 &1\nGameObject:\n  m_Name: A\n",
            "Assets/Scenes/Test.unity.meta": meta(GUID_SCENE),
            "Assets/Data/Maps/Test.asset": f"%YAML 1.1\nMonoBehaviour:\n  arsenalPreset: {{fileID: 11400000, guid: {GUID_PRESET}, type: 2}}\n",
            "Assets/Data/Maps/Test.asset.meta": meta(GUID_MAP),
            "Assets/Data/Modes.asset": "%YAML 1.1\nMonoBehaviour:\n  m_Name: Modes\n",
            "Assets/Data/Modes.asset.meta": meta(GUID_MODES),
            "Assets/Data/Preset.asset": "%YAML 1.1\nMonoBehaviour:\n  m_Name: Preset\n",
            "Assets/Data/Preset.asset.meta": meta(GUID_PRESET),
            "Assets/Data/Other.asset": "%YAML 1.1\nMonoBehaviour:\n  m_Name: Other\n",
            "Assets/Data/Other.asset.meta": meta(GUID_OTHER),
            CATALOG: catalog(),
            CATALOG + ".meta": meta(GUID_CATALOG),
        }
        for path, text in files.items():
            self.write(path, text)
        # Каталог приводится к актуальному штатным --fix и фиксируется как база.
        self.assertEqual(0, self.run_script("--fix").returncode)
        self.git("add", "-A")
        self.git("commit", "-qm", "base")

    def tearDown(self):
        self._tmp.cleanup()

    def git(self, *args):
        return subprocess.run(["git", "-C", str(self.repo), *args], check=True,
                              stdout=subprocess.PIPE, stderr=subprocess.PIPE).stdout

    def write(self, path, text, newline="\n"):
        file = self.repo / path
        file.parent.mkdir(parents=True, exist_ok=True)
        file.write_bytes(text.replace("\n", newline).encode("utf-8"))

    def run_script(self, *args):
        return subprocess.run([sys.executable, str(SCRIPT), "--repo", str(self.repo), *args],
                              stdout=subprocess.PIPE, stderr=subprocess.PIPE)

    def hook_check(self):
        return self.run_script("--check", "--source", "index", "--staged-only")

    def test_fixed_catalog_passes(self):
        text = (self.repo / CATALOG).read_text(encoding="utf-8")
        self.assertNotIn(STALE, text, "--fix должен заменить оба отпечатка")
        self.assertEqual(0, self.run_script("--check", "--source", "index").returncode)

    def test_unrelated_commit_skips(self):
        self.write("README.md", "текст\n")
        self.git("add", "README.md")
        self.assertEqual(0, self.hook_check().returncode)

    def test_asset_outside_catalog_skips(self):
        self.write("Assets/Data/Other.asset", "%YAML 1.1\nMonoBehaviour:\n  m_Name: Changed\n")
        self.git("add", "Assets/Data/Other.asset")
        self.assertEqual(0, self.hook_check().returncode)

    def test_scene_change_without_catalog_is_rejected(self):
        self.write("Assets/Scenes/Test.unity", "%YAML 1.1\n--- !u!1 &1\nGameObject:\n  m_Name: B\n")
        self.git("add", "Assets/Scenes/Test.unity")
        result = self.hook_check()
        self.assertEqual(1, result.returncode)
        self.assertIn("Assets/Scenes/Test.unity: ContentFingerprint", result.stderr.decode("utf-8"))

        self.assertEqual(0, self.run_script("--fix").returncode)
        self.git("add", CATALOG)
        self.assertEqual(0, self.hook_check().returncode)

    def test_preset_change_marks_arsenal(self):
        self.write("Assets/Data/Preset.asset", "%YAML 1.1\nMonoBehaviour:\n  m_Name: Preset2\n")
        self.git("add", "Assets/Data/Preset.asset")
        result = self.hook_check()
        self.assertEqual(1, result.returncode)
        self.assertIn("ArsenalFingerprint", result.stderr.decode("utf-8"))

    def test_check_reads_index_not_worktree(self):
        # Несохранённая в индексе правка сцены в коммит не уходит — проверка её не видит.
        self.write("Assets/Scenes/Test.unity", "%YAML 1.1\n--- !u!1 &1\nGameObject:\n  m_Name: C\n")
        self.assertEqual(0, self.run_script("--check", "--source", "index").returncode)
        self.assertEqual(1, self.run_script("--check", "--source", "worktree").returncode)

    def test_crlf_yaml_hashes_as_lf(self):
        # Как MapRunPreflight.FileContentHash: у Unity YAML пары CR LF сводятся к LF.
        self.write("Assets/Scenes/Test.unity", "%YAML 1.1\n--- !u!1 &1\nGameObject:\n  m_Name: A\n", newline="\r\n")
        self.assertEqual(0, self.run_script("--check", "--source", "worktree").returncode)


if __name__ == "__main__":
    unittest.main()
