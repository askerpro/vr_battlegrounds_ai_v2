#!/usr/bin/env python3
"""Проверка и исправление отпечатков MapRuntimeCatalog без Unity.

Каталог карт (Assets/Data/Maps/MapRuntimeCatalog.asset) хранит для каждой карты
ContentFingerprint (сцена + MapData + реестр режимов) и ArsenalFingerprint (пресет арсенала).
Сборка пересчитывает их (MapCatalogBuildStep) и при устаревшем каталоге оставляет diff в
build-checkout, а следующий Build-Game.ps1 -Ref отказывает. Алгоритм — точная копия
MapRunPreflight.ContentFingerprint / ArsenalFingerprint / AssetHash: SHA-256 содержимого файла
и его .meta, у Unity YAML пары CR LF сводятся к LF.

Режимы:
  --check            (по умолчанию) сверить каталог; код 1 — есть устаревшие записи.
  --fix              переписать устаревшие отпечатки в рабочем файле каталога.
  --source index     читать файлы из индекса (то, что уйдёт в коммит) — так зовёт pre-commit.
  --source worktree  читать рабочие файлы (по умолчанию для --fix).
  --staged-only      выйти сразу, если в индексе нет изменений входов каталога (быстрый путь хука).

Коды: 0 — каталог актуален или проверять нечего; 1 — устаревшие записи; 2 — ошибка.
"""

from __future__ import annotations

import argparse
import hashlib
import re
import subprocess
import sys
from pathlib import Path

CATALOG = "Assets/Data/Maps/MapRuntimeCatalog.asset"

YAML_HEADER = b"%YAML"
META_HEADER = b"fileFormatVersion:"

GUID_RE = re.compile(r"guid:\s*([0-9a-f]{32})")
ENTRY_RE = re.compile(
    r"^  - Map: \{fileID: \d+, guid: (?P<map>[0-9a-f]{32}), type: \d+\}\n"
    r"    ScenePath: (?P<scene>[^\n]*)\n"
    r"    ContentFingerprint: (?P<content>[0-9a-f]*)\n"
    r"    ArsenalFingerprint: (?P<arsenal>[0-9a-f]*)\n",
    re.M,
)
MODES_RE = re.compile(r"^  _modes: \{fileID: \d+, guid: ([0-9a-f]{32}), type: \d+\}", re.M)
PRESET_RE = re.compile(r"^  arsenalPreset: \{fileID: (\d+)(?:, guid: ([0-9a-f]{32}))?", re.M)


class CatalogError(Exception):
    pass


def git(root: Path, *args: str, check: bool = True) -> bytes:
    proc = subprocess.run(["git", "-C", str(root), "-c", "core.quotepath=off", *args],
                          stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    if check and proc.returncode != 0:
        raise CatalogError(f"git {' '.join(args)}: {proc.stderr.decode('utf-8', 'replace').strip()}")
    return proc.stdout


class Source:
    """Откуда читать файлы: индекс (то, что коммитится) или рабочая копия."""

    def __init__(self, root: Path, kind: str):
        self.root = root
        self.kind = kind
        self._guid_paths: dict[str, str] = {}
        self._batch = None

    def close(self) -> None:
        if self._batch is not None:
            self._batch.stdin.close()
            self._batch.wait()
            self._batch = None

    def _read_index(self, path: str) -> bytes:
        # Один процесс git cat-file на все чтения: по процессу на файл хук шёл секунды.
        if self._batch is None:
            self._batch = subprocess.Popen(["git", "-C", str(self.root), "cat-file", "--batch"],
                                           stdin=subprocess.PIPE, stdout=subprocess.PIPE)
        self._batch.stdin.write(f":{path}\n".encode("utf-8"))
        self._batch.stdin.flush()
        header = self._batch.stdout.readline().decode("utf-8", "replace").split()
        if len(header) < 3 or header[1] != "blob":
            raise CatalogError(f"Content.Source.Missing:{path} (нет в индексе)")
        data = self._batch.stdout.read(int(header[2]))
        self._batch.stdout.read(1)  # перевод строки после содержимого
        return data

    def read(self, path: str) -> bytes:
        if self.kind == "index":
            return self._read_index(path)
        file = self.root / path
        if not file.is_file():
            raise CatalogError(f"Content.Source.Missing:{path}")
        return file.read_bytes()

    def resolve_guids(self, guids: set[str]) -> None:
        """GUID → путь ассета по .meta (один git grep на все GUID)."""
        missing = sorted(g for g in guids if g not in self._guid_paths)
        if not missing:
            return
        pattern = "^guid: (" + "|".join(missing) + ")$"
        args = ["grep", "-E", "-o", pattern]
        if self.kind == "index":
            args.insert(1, "--cached")
        else:
            args.insert(1, "--untracked")
        out = git(self.root, *args, "--", "*.meta", check=False).decode("utf-8", "replace")
        for line in out.splitlines():
            meta, _, rest = line.partition(":")
            m = GUID_RE.search(rest)
            if m and meta.endswith(".meta"):
                self._guid_paths[m.group(1)] = meta[: -len(".meta")]

    def path_of(self, guid: str) -> str:
        self.resolve_guids({guid})
        path = self._guid_paths.get(guid)
        if path is None:
            raise CatalogError(f"Content.Source.Missing: нет ассета с guid {guid}")
        return path


def file_content_hash(data: bytes) -> str:
    if data.startswith(YAML_HEADER) or data.startswith(META_HEADER):
        data = data.replace(b"\r\n", b"\n")
    return hashlib.sha256(data).hexdigest()


def text_hash(value: str) -> str:
    return hashlib.sha256(value.encode("utf-8")).hexdigest()


def asset_hash(src: Source, path: str) -> str:
    return text_hash("asset-content-v2\n" + file_content_hash(src.read(path)) + "\n"
                     + file_content_hash(src.read(path + ".meta")))


def content_fingerprint(src: Source, scene: str, map_path: str, modes_path: str) -> str:
    parts = ["map-content-v2", asset_hash(src, scene), asset_hash(src, map_path), asset_hash(src, modes_path)]
    return text_hash("\n".join(parts))


def arsenal_fingerprint(src: Source, map_path: str) -> tuple[str, str | None]:
    """Отпечаток пресета арсенала карты и путь пресета (None — пресета нет, отпечаток пустой)."""
    m = PRESET_RE.search(src.read(map_path).decode("utf-8", "replace"))
    if m is None or m.group(1) == "0" or m.group(2) is None:
        return "", None
    preset = src.path_of(m.group(2))
    return text_hash("arsenal-content-v2\n" + asset_hash(src, preset)), preset


def analyse(src: Source):
    """Записи каталога: (match, ожидаемый content, ожидаемый arsenal, входы) и текст каталога."""
    text = src.read(CATALOG).decode("utf-8")
    text_lf = text.replace("\r\n", "\n")
    modes = MODES_RE.search(text_lf)
    if modes is None:
        raise CatalogError(f"{CATALOG}: не найдено поле _modes")
    entries = list(ENTRY_RE.finditer(text_lf))
    if not entries:
        raise CatalogError(f"{CATALOG}: не найдено ни одной записи _content")

    src.resolve_guids({modes.group(1)} | {e.group("map") for e in entries})
    modes_path = src.path_of(modes.group(1))

    results = []
    for e in entries:
        map_path = src.path_of(e.group("map"))
        scene = e.group("scene")
        inputs = {CATALOG, scene, scene + ".meta", map_path, map_path + ".meta", modes_path, modes_path + ".meta"}
        content = content_fingerprint(src, scene, map_path, modes_path) if scene else ""
        arsenal, preset = arsenal_fingerprint(src, map_path)
        if preset:
            inputs |= {preset, preset + ".meta"}
        results.append((e, content, arsenal, inputs))
    return text_lf, results


def staged_paths(root: Path) -> set[str]:
    out = git(root, "diff", "--cached", "--name-only", "--no-renames", "-z")
    return {p for p in out.decode("utf-8", "replace").split("\0") if p}


def main(argv: list[str]) -> int:
    for stream in (sys.stdout, sys.stderr):
        stream.reconfigure(encoding="utf-8", errors="replace")

    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument("--check", action="store_true")
    mode.add_argument("--fix", action="store_true")
    parser.add_argument("--source", choices=("index", "worktree"))
    parser.add_argument("--staged-only", action="store_true")
    parser.add_argument("--repo", default=".")
    args = parser.parse_args(argv)

    root = Path(git(Path(args.repo), "rev-parse", "--show-toplevel").decode().strip())
    kind = args.source or ("worktree" if args.fix else "index")

    staged = None
    if args.staged_only:
        staged = staged_paths(root)
        # Быстрый путь: входы каталога — только сериализованные ассеты Unity и их .meta.
        if not any(p.endswith((".unity", ".asset", ".meta")) for p in staged):
            return 0

    src = Source(root, kind)
    try:
        text_lf, results = analyse(src)
    except CatalogError as ex:
        print(f"map-catalog: {ex}", file=sys.stderr)
        return 2
    finally:
        src.close()

    if staged is not None and not any(inputs & staged for _, _, _, inputs in results):
        return 0

    stale = [(e, c, a) for e, c, a, _ in results
             if e.group("content") != c or e.group("arsenal") != a]
    if not stale:
        return 0

    if args.fix:
        fixed = text_lf
        for e, c, a in reversed(stale):
            block = e.group(0)
            new_block = (block.replace(f"ContentFingerprint: {e.group('content')}\n", f"ContentFingerprint: {c}\n")
                              .replace(f"ArsenalFingerprint: {e.group('arsenal')}\n", f"ArsenalFingerprint: {a}\n"))
            fixed = fixed[: e.start()] + new_block + fixed[e.end():]
        (root / CATALOG).write_bytes(fixed.encode("utf-8"))
        for e, _, _ in stale:
            print(f"map-catalog: обновлён отпечаток {e.group('scene')}")
        print(f"map-catalog: проверьте и добавьте в коммит: git add {CATALOG}")
        return 0

    print("map-catalog: MapRuntimeCatalog устарел — сборка пересчитает отпечатки и оставит diff в build-checkout:", file=sys.stderr)
    for e, c, a in stale:
        what = []
        if e.group("content") != c:
            what.append("ContentFingerprint")
        if e.group("arsenal") != a:
            what.append("ArsenalFingerprint")
        print(f"  {e.group('scene')}: {', '.join(what)}", file=sys.stderr)
    print("Исправить без Unity:\n"
          f"  python Tools/LevelDesign/map_catalog_fingerprints.py --fix && git add {CATALOG}", file=sys.stderr)
    return 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
