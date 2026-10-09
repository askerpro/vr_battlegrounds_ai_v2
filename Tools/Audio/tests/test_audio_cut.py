"""Самопроверка audio_cut.py: рецепты сверяются без записи, расхождение и отсутствие выхода ловятся, тишина распознаётся.

Запуск из корня репозитория: python -m unittest discover -s Tools/Audio/tests -p "test_*.py"
"""
import json
import subprocess
import sys
import tempfile
import unittest
import wave
from pathlib import Path

import numpy as np

AUDIO = Path(__file__).resolve().parents[1]
TOOL = AUDIO / "audio_cut.py"
REPO = AUDIO.parents[1]
RECIPES = sorted((AUDIO / "recipes").glob("*.json"))
SRM12 = REPO / "Assets/Audio/SFX/Weapons/Kinemation/SRM12"


def run(*args):
    return subprocess.run([sys.executable, str(TOOL), *map(str, args)], capture_output=True, text=True,
                          encoding="utf-8", cwd=REPO)


def write_wav(path: Path, samples: np.ndarray, rate: int = 44100):
    pcm = (np.clip(samples, -1, 1) * 32767).astype("<i2")
    with wave.open(str(path), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(rate)
        w.writeframes(pcm.tobytes())


def outputs(recipe: Path):
    data = json.loads(recipe.read_text(encoding="utf-8"))
    return [REPO / out["path"] for job in data["jobs"] for out in job["outputs"]]


class RecipeCheckTests(unittest.TestCase):
    def test_рецепты_проекта_совпадают_с_выходами_и_не_пишут(self):
        self.assertTrue(RECIPES, "в Tools/Audio/recipes нет рецептов")
        for recipe in RECIPES:
            with self.subTest(recipe=recipe.name):
                before = {p: p.stat().st_mtime_ns for p in outputs(recipe)}
                result = run("cut", "--recipe", recipe, "--check")
                self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
                self.assertNotIn("ОТЛИЧАЕТСЯ", result.stdout)
                self.assertEqual(before, {p: p.stat().st_mtime_ns for p in outputs(recipe)}, "--check не должен писать")

    def test_check_ловит_расхождение_и_отсутствие_без_записи(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            t = np.arange(4410) / 44100
            write_wav(root / "src.wav", 0.5 * np.sin(2 * np.pi * 440 * t))
            stale, absent = root / "stale_Cut.wav", root / "absent_Cut.wav"
            stale.write_bytes(b"not the cut")
            recipe = root / "r.json"
            recipe.write_text(json.dumps({"fade_ms": 5, "jobs": [{"source": str(root / "src.wav"), "outputs": [
                {"path": str(stale), "start": 0.0, "end": 0.05},
                {"path": str(absent), "start": 0.05, "end": 0.1}]}]}), encoding="utf-8")

            result = run("cut", "--recipe", recipe, "--check")
            self.assertEqual(result.returncode, 1, result.stdout + result.stderr)
            self.assertIn("ОТЛИЧАЕТСЯ", result.stdout)
            self.assertIn("НЕТ ФАЙЛА", result.stdout)
            self.assertEqual(stale.read_bytes(), b"not the cut", "--check не перезаписывает")
            self.assertFalse(absent.exists(), "--check не создаёт выходы")

            # Нарезка детерминирована: запись, затем --check совпадает.
            self.assertEqual(run("cut", "--recipe", recipe, "--force").returncode, 0)
            self.assertEqual(run("cut", "--recipe", recipe, "--check").returncode, 0)


class LevelTests(unittest.TestCase):
    def test_analyze_ловит_почти_тишину_пака_и_пропускает_нарезку(self):
        self.assertEqual(run("analyze", SRM12 / "SRM12_BoltBack.wav").returncode, 1, "исходник пака — почти тишина")
        result = run("analyze", SRM12 / "SRM12_BoltBack_Cut.wav", SRM12 / "SRM12_BoltForward_Cut.wav")
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)


if __name__ == "__main__":
    unittest.main()
