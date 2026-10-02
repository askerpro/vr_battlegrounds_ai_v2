"""Контракт инвентаризации: пути архивов считаются без распаковки ассетов."""

import gzip
import importlib.util
import io
from pathlib import Path
import tarfile
import tempfile
import unittest

spec = importlib.util.spec_from_file_location(
    "inspect_asset_packages", Path(__file__).resolve().parents[1] / "inspect_asset_packages.py"
)
inventory = importlib.util.module_from_spec(spec)
spec.loader.exec_module(inventory)


class PackageInventoryTests(unittest.TestCase):
    def test_paths_and_gzip_metadata(self):
        paths = ["Assets/Модели/Wall.FBX", "Assets/Prefabs/Wall.prefab",
                 "Assets/Maps/Test.unity", "Assets/Scripts/Test.cs"]
        raw = io.BytesIO()
        with tarfile.open(fileobj=raw, mode="w") as archive:
            for index, path in enumerate(paths):
                data = path.encode("utf-8") + b"\x00"
                entry = tarfile.TarInfo(f"guid{index}/pathname")
                entry.size = len(data)
                archive.addfile(entry, io.BytesIO(data))
            entry = tarfile.TarInfo("guid0/asset")
            entry.size = 4
            archive.addfile(entry, io.BytesIO(b"mesh"))
        compressed = gzip.compress(raw.getvalue())
        extra = b"FX\x02\x00ok"
        header = bytearray(compressed[:10])
        header[3] |= 4  # FEXTRA магазина Unity
        with tempfile.TemporaryDirectory() as directory:
            for content in (compressed, bytes(header) + len(extra).to_bytes(2, "little") + extra + compressed[10:]):
                with self.subTest(extra_metadata=content != compressed):
                    package = Path(directory) / "sample.unitypackage"
                    package.write_bytes(content)
                    result = inventory.inspect(package)
                    self.assertEqual(result["entries"], 4)
                    self.assertEqual(result["extensions"], {".fbx": 1, ".prefab": 1, ".unity": 1, ".cs": 1})
                    self.assertEqual(result["model_and_asset_paths"], paths[:1])
                    self.assertEqual(result["prefab_paths"], paths[1:2])
                    self.assertEqual(result["scene_paths"], paths[2:3])
                    self.assertEqual(result["script_paths"], paths[3:4])


if __name__ == "__main__":
    unittest.main()
