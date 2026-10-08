"""Git должен сохранять бинарные Unity .asset побайтово при checkout."""

from pathlib import Path
import struct
import subprocess
import unittest


ROOT = Path(__file__).resolve().parents[2]


def git(*args):
    return subprocess.check_output(["git", "--no-optional-locks", "-C", str(ROOT), *args])


def binary_unity_assets():
    paths = git("ls-files", "-z", "--", "Assets/**/*.asset").decode("utf-8").split("\0")
    result = []
    for path in filter(None, paths):
        file = ROOT / path
        if not file.is_file():
            continue
        with file.open("rb") as stream:
            header = stream.read(48)
        if len(header) < 20 or header.startswith(b"%YAML"):
            continue
        version = struct.unpack_from(">I", header, 8)[0]
        if 5 <= version <= 22 and header[16] in (0, 1):
            result.append(path)
    return result


class UnityBinaryAssetGitTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.assets = binary_unity_assets()
        if not cls.assets:
            raise AssertionError("Не обнаружены бинарные Unity .asset; проверка не должна быть пустой")

    def test_binary_assets_do_not_force_text_conversion(self):
        failures = []
        for path in self.assets:
            output = git("check-attr", "-z", "text", "filter", "--", path).decode("utf-8").split("\0")
            attrs = {output[i + 1]: output[i + 2] for i in range(0, len(output) - 1, 3)}
            if attrs.get("text") not in ("unset", "auto"):
                failures.append(f"{path}: text={attrs.get('text')}; бинарник будет обработан как текст")
        self.assertFalse(failures, "\n".join(failures))

    def test_checkout_preserves_non_lfs_binary_blob(self):
        failures = []
        checked = 0
        for path in self.assets:
            attrs = git("check-attr", "filter", "--", path).decode("utf-8").strip()
            if attrs.endswith(": lfs"):
                continue  # LFS blob — указатель; его целостность проверяется средствами LFS.
            original = git("cat-file", "blob", f"HEAD:{path}")
            checkout = git("cat-file", "--filters", f"HEAD:{path}")
            checked += 1
            if original != checkout:
                failures.append(f"{path}: Git blob {len(original)} байт, checkout {len(checkout)} байт")
        self.assertGreater(checked, 0, "Нет бинарных Git blobs для проверки checkout")
        self.assertFalse(failures, "\n".join(failures))

    def test_local_serialized_file_sizes_match_headers(self):
        failures = []
        for path in self.assets:
            raw = (ROOT / path).read_bytes()
            version = struct.unpack_from(">I", raw, 8)[0]
            declared = struct.unpack_from(">Q", raw, 24)[0] if version >= 22 else struct.unpack_from(">I", raw, 4)[0]
            if len(raw) != declared:
                failures.append(f"{path}: файл {len(raw)} байт, заголовок {declared} байт")
        self.assertFalse(failures, "\n".join(failures))


if __name__ == "__main__":
    unittest.main(verbosity=2)
