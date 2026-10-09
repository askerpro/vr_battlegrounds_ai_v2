#!/usr/bin/env python3
"""Анализ и нарезка PCM WAV для звуков игры (Tools/Audio/README.md).

Только стандартная библиотека и numpy: исходники проекта — несжатый WAV, ffmpeg/sox не нужны.

  analyze <wav...>        длина, формат, пик/RMS в dBFS, «почти тишина», огибающая, паузы, точки разреза
  cut --recipe <json>     нарезка по рецепту (Tools/Audio/recipes/); --check — сверить выходы без записи
  cut <wav> --start --end --out   нарезка одного отрезка аргументами

Исходники не изменяются. Существующий выход без --force не перезаписывается. Результат детерминирован:
повторный прогон рецепта даёт байт-в-байт тот же файл.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import struct
import sys
from dataclasses import dataclass
from pathlib import Path

import numpy as np

REPO_ROOT = Path(__file__).resolve().parents[2]
FORMAT_PCM = 1
FORMAT_FLOAT = 3
FORMAT_EXTENSIBLE = 0xFFFE
ENVELOPE_CHARS = " .:-=+*#"  # от тишины (< порога) к пику огибающей


# ── WAV ─────────────────────────────────────────────────────────────────────────────

@dataclass
class Wav:
    fmt_chunk: bytes      # тело chunk 'fmt ' как есть — выход пишется в том же формате
    format_tag: int       # 1 — PCM, 3 — float (из SubFormat для EXTENSIBLE)
    channels: int
    rate: int
    bits: int
    samples: np.ndarray   # float64, форма (кадры, каналы), диапазон [-1, 1]

    @property
    def frames(self) -> int:
        return self.samples.shape[0]

    @property
    def duration(self) -> float:
        return self.frames / self.rate


def read_wav(path: Path) -> Wav:
    data = path.read_bytes()
    if data[:4] != b"RIFF" or data[8:12] != b"WAVE":
        raise ValueError(f"{path}: не RIFF/WAVE")
    pos, fmt, body = 12, None, None
    while pos + 8 <= len(data):
        cid, size = data[pos:pos + 4], struct.unpack_from("<I", data, pos + 4)[0]
        chunk = data[pos + 8:pos + 8 + size]
        if cid == b"fmt ":
            fmt = chunk
        elif cid == b"data":
            body = chunk
        pos += 8 + size + (size & 1)
    if fmt is None or body is None:
        raise ValueError(f"{path}: нет chunk fmt/data")
    tag, channels, rate, _, align, bits = struct.unpack_from("<HHIIHH", fmt, 0)
    if tag == FORMAT_EXTENSIBLE and len(fmt) >= 26:
        tag = struct.unpack_from("<H", fmt, 24)[0]
    body = body[:len(body) - len(body) % align]
    if tag == FORMAT_FLOAT and bits in (32, 64):
        raw = np.frombuffer(body, dtype="<f4" if bits == 32 else "<f8").astype(np.float64)
    elif tag == FORMAT_PCM and bits == 8:
        raw = (np.frombuffer(body, dtype=np.uint8).astype(np.float64) - 128.0) / 128.0
    elif tag == FORMAT_PCM and bits in (16, 32):
        raw = np.frombuffer(body, dtype="<i2" if bits == 16 else "<i4").astype(np.float64) / float(2 ** (bits - 1))
    elif tag == FORMAT_PCM and bits == 24:
        b = np.frombuffer(body, dtype=np.uint8).reshape(-1, 3).astype(np.int32)
        v = b[:, 0] | (b[:, 1] << 8) | (b[:, 2] << 16)
        v = np.where(v >= 1 << 23, v - (1 << 24), v)
        raw = v.astype(np.float64) / float(1 << 23)
    else:
        raise ValueError(f"{path}: формат {tag}/{bits} бит не поддерживается")
    return Wav(fmt, tag, channels, rate, bits, raw.reshape(-1, channels))


def encode(wav: Wav, samples: np.ndarray) -> bytes:
    """Квантование в формат источника: округление к ближайшему, насыщение — детерминировано."""
    flat = samples.reshape(-1)
    if wav.format_tag == FORMAT_FLOAT:
        body = flat.astype("<f4" if wav.bits == 32 else "<f8").tobytes()
    elif wav.bits == 8:
        body = np.clip(np.rint(flat * 128.0 + 128.0), 0, 255).astype(np.uint8).tobytes()
    elif wav.bits in (16, 32):
        scale = float(2 ** (wav.bits - 1))
        body = np.clip(np.rint(flat * scale), -scale, scale - 1).astype("<i2" if wav.bits == 16 else "<i4").tobytes()
    else:  # 24
        v = np.clip(np.rint(flat * (1 << 23)), -(1 << 23), (1 << 23) - 1).astype(np.int32)
        v = np.where(v < 0, v + (1 << 24), v).astype(np.uint32)
        body = np.stack([v & 0xFF, (v >> 8) & 0xFF, (v >> 16) & 0xFF], axis=1).astype(np.uint8).tobytes()
    fmt = wav.fmt_chunk + (b"\0" if len(wav.fmt_chunk) & 1 else b"")
    pad = b"\0" if len(body) & 1 else b""
    riff = b"WAVE" + b"fmt " + struct.pack("<I", len(wav.fmt_chunk)) + fmt + b"data" + struct.pack("<I", len(body)) + body + pad
    return b"RIFF" + struct.pack("<I", len(riff)) + riff


# ── Анализ ──────────────────────────────────────────────────────────────────────────

def dbfs(value: float) -> float:
    return float(round(20.0 * np.log10(value), 1)) if value > 0 else -999.0


def window_rms(wav: Wav, window_s: float) -> np.ndarray:
    mono = np.sqrt(np.mean(wav.samples ** 2, axis=1))  # RMS по каналам кадра
    n = max(1, int(round(window_s * wav.rate)))
    count = (wav.frames + n - 1) // n
    padded = np.zeros(count * n)
    padded[:wav.frames] = mono
    return np.sqrt(np.mean(padded.reshape(count, n) ** 2, axis=1))


def analyze(path: Path, silence_dbfs: float, window_ms: float, gap_db: float, min_pause_ms: float,
            search: tuple[float, float] | None) -> dict:
    wav = read_wav(path)
    peak = float(np.max(np.abs(wav.samples))) if wav.frames else 0.0
    rms = float(np.sqrt(np.mean(wav.samples ** 2))) if wav.frames else 0.0
    env = window_rms(wav, window_ms / 1000.0)
    env_db = np.array([dbfs(v) for v in env])
    top = env_db.max() if len(env_db) else -999.0
    # Пауза — окна тише пика огибающей на gap_db (относительный порог: клипы разной громкости).
    quiet_floor = top - gap_db
    envelope = "".join(
        ENVELOPE_CHARS[0] if d < quiet_floor else ENVELOPE_CHARS[min(len(ENVELOPE_CHARS) - 1, 1 + int((d - quiet_floor) / gap_db * (len(ENVELOPE_CHARS) - 1)))]
        for d in env_db)
    # Точки разреза — минимум тонкой огибающей (5 мс) внутри каждой паузы между звучащими участками.
    fine_s = 0.005
    fine = window_rms(wav, fine_s)
    fine_db = np.array([dbfs(v) for v in fine])
    loud = fine_db >= top - gap_db
    pauses, cuts = [], []
    start = None
    for i, is_loud in enumerate(list(loud) + [True]):
        if not is_loud and start is None:
            start = i
        elif is_loud and start is not None:
            if start > 0 and i < len(loud) and (i - start) * fine_s * 1000 >= min_pause_ms:
                seg = fine_db[start:i]
                k = start + int(np.argmin(seg))
                pauses.append({"from": round(start * fine_s, 3), "to": round(i * fine_s, 3), "min_dbfs": float(fine_db[k])})
                cuts.append(round((k + 0.5) * fine_s, 3))
            start = None
    result = {
        "file": str(path), "duration_s": round(wav.duration, 3), "channels": wav.channels, "rate": wav.rate, "bits": wav.bits,
        "float": wav.format_tag == FORMAT_FLOAT, "peak_dbfs": dbfs(peak), "rms_dbfs": dbfs(rms),
        "near_silence": bool(dbfs(peak) < silence_dbfs), "silence_threshold_dbfs": silence_dbfs,
        "envelope_window_ms": window_ms, "envelope": envelope, "pauses": pauses, "cut_points_s": cuts,
        "sha256": hashlib.sha256(path.read_bytes()).hexdigest(),
    }
    if search:
        a, b = (int(round(t / fine_s)) for t in search)
        a, b = max(0, a), min(len(fine_db), max(a + 1, b))
        k = a + int(np.argmin(fine_db[a:b]))
        result["search"] = {"range_s": list(search), "quietest_s": round((k + 0.5) * fine_s, 3), "dbfs": float(fine_db[k])}
    return result


def print_analysis(r: dict) -> None:
    flag = "  ПОЧТИ ТИШИНА" if r["near_silence"] else ""
    print(f"{r['file']}\n  {r['duration_s']} с, {r['channels']} кан, {r['rate']} Гц, {r['bits']} бит"
          f"{' float' if r['float'] else ''}; пик {r['peak_dbfs']} dBFS, RMS {r['rms_dbfs']} dBFS{flag}")
    print(f"  огибающая/{r['envelope_window_ms']:g} мс: |{r['envelope']}|")
    for p in r["pauses"]:
        print(f"  пауза {p['from']:.3f}–{p['to']:.3f} с, минимум {p['min_dbfs']} dBFS")
    if r["cut_points_s"]:
        print("  точки разреза, с: " + ", ".join(f"{c:.3f}" for c in r["cut_points_s"]))
    if "search" in r:
        s = r["search"]
        print(f"  самое тихое в {s['range_s'][0]}–{s['range_s'][1]} с: {s['quietest_s']:.3f} с ({s['dbfs']} dBFS)")
    print(f"  sha256 {r['sha256'][:16]}")


# ── Нарезка ─────────────────────────────────────────────────────────────────────────

def render(wav: Wav, start: float, end: float | None, fade_ms: float, normalize_dbfs: float | None) -> bytes:
    a = int(round(start * wav.rate))
    b = wav.frames if end is None else int(round(end * wav.rate))
    if not 0 <= a < b <= wav.frames:
        raise ValueError(f"отрезок {start}–{end} с вне файла длиной {wav.duration:.3f} с")
    seg = wav.samples[a:b].copy()
    n = min(int(round(fade_ms / 1000.0 * wav.rate)), seg.shape[0] // 2)
    if n > 0:
        ramp = (np.arange(n, dtype=np.float64) / n)[:, None]
        seg[:n] *= ramp
        seg[-n:] *= ramp[::-1]
    if normalize_dbfs is not None:
        peak = float(np.max(np.abs(seg)))
        if peak > 0:
            seg *= (10.0 ** (normalize_dbfs / 20.0)) / peak
    return encode(wav, seg)


def resolve(path: str) -> Path:
    p = Path(path)
    return p if p.is_absolute() else REPO_ROOT / p


def write_output(out: Path, data: bytes, sources: set[Path], force: bool, check: bool) -> str:
    digest = hashlib.sha256(data).hexdigest()
    if out.resolve() in sources:
        raise ValueError(f"{out}: выход совпадает с исходником — исходники не изменяются")
    if check:
        if not out.exists():
            return f"НЕТ ФАЙЛА {out}"
        same = hashlib.sha256(out.read_bytes()).hexdigest() == digest
        return f"{'совпадает' if same else 'ОТЛИЧАЕТСЯ'} {out} sha256 {digest[:16]}"
    if out.exists() and not force:
        if out.read_bytes() == data:
            return f"без изменений {out} sha256 {digest[:16]}"
        raise FileExistsError(f"{out}: файл есть и отличается; перезапись — только с --force")
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_bytes(data)
    return f"записан {out} sha256 {digest[:16]}"


def run_recipe(recipe_path: Path, force: bool, check: bool) -> bool:
    recipe = json.loads(recipe_path.read_text(encoding="utf-8"))
    ok = True
    for job in recipe["jobs"]:
        src = resolve(job["source"])
        expected = job.get("source_sha256")
        actual = hashlib.sha256(src.read_bytes()).hexdigest()
        if expected and expected != actual:
            raise ValueError(f"{src}: исходник изменился (sha256 {actual[:16]}, в рецепте {expected[:16]}) — пересмотреть точки разреза")
        wav = read_wav(src)
        for out in job["outputs"]:
            fade = out.get("fade_ms", job.get("fade_ms", recipe.get("fade_ms", 5.0)))
            norm = out.get("normalize_dbfs", job.get("normalize_dbfs"))
            data = render(wav, float(out["start"]), out.get("end"), float(fade), norm)
            line = write_output(resolve(out["path"]), data, {src.resolve()}, force, check)
            ok &= not line.startswith(("НЕТ", "ОТЛИЧАЕТСЯ"))
            print(line)
    return ok


# ── CLI ─────────────────────────────────────────────────────────────────────────────

def parse_range(text: str) -> tuple[float, float]:
    a, b = text.split(":")
    return float(a), float(b)


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest="command", required=True)

    an = sub.add_parser("analyze", help="отчёт об уровне, огибающей и паузах")
    an.add_argument("files", nargs="+")
    an.add_argument("--silence-dbfs", type=float, default=-40.0, help="пик ниже — «почти тишина» (по умолчанию −40)")
    an.add_argument("--window-ms", type=float, default=50.0, help="окно огибающей (по умолчанию 50 мс)")
    an.add_argument("--gap-db", type=float, default=30.0, help="пауза — тише пика огибающей на столько дБ (по умолчанию 30)")
    an.add_argument("--min-pause-ms", type=float, default=20.0, help="минимальная длина паузы (по умолчанию 20 мс)")
    an.add_argument("--search", type=parse_range, help="найти самое тихое место в диапазоне «0.35:0.45» (с)")
    an.add_argument("--json", action="store_true", help="вывод JSON")

    cu = sub.add_parser("cut", help="нарезка по рецепту или по аргументам")
    cu.add_argument("source", nargs="?", help="исходный WAV (без --recipe)")
    cu.add_argument("--recipe", help="JSON-рецепт из Tools/Audio/recipes/")
    cu.add_argument("--start", type=float, default=0.0)
    cu.add_argument("--end", type=float)
    cu.add_argument("--out")
    cu.add_argument("--fade-ms", type=float, default=5.0, help="линейные фейды на краях (по умолчанию 5 мс)")
    cu.add_argument("--normalize-dbfs", type=float, help="нормализовать пик до уровня, например −1")
    cu.add_argument("--force", action="store_true", help="перезаписать существующий выход")
    cu.add_argument("--check", action="store_true", help="только сверить существующие выходы с расчётом")

    args = parser.parse_args(argv)
    if args.command == "analyze":
        results = [analyze(resolve(f), args.silence_dbfs, args.window_ms, args.gap_db, args.min_pause_ms, args.search) for f in args.files]
        if args.json:
            print(json.dumps(results, ensure_ascii=False, indent=1))
        else:
            for r in results:
                print_analysis(r)
        return 1 if any(r["near_silence"] for r in results) else 0
    if args.recipe:
        return 0 if run_recipe(resolve(args.recipe), args.force, args.check) else 1
    if not args.source or not args.out:
        parser.error("cut: нужен --recipe либо исходник и --out")
    src = resolve(args.source)
    data = render(read_wav(src), args.start, args.end, args.fade_ms, args.normalize_dbfs)
    line = write_output(resolve(args.out), data, {src.resolve()}, args.force, args.check)
    print(line)
    return 1 if line.startswith(("НЕТ", "ОТЛИЧАЕТСЯ")) else 0


if __name__ == "__main__":
    try:
        sys.stdout.reconfigure(encoding="utf-8")
        sys.stderr.reconfigure(encoding="utf-8")
    except AttributeError:
        pass
    try:
        sys.exit(main(sys.argv[1:]))
    except (ValueError, FileExistsError, FileNotFoundError, KeyError) as error:
        print(f"ошибка: {error}", file=sys.stderr)
        sys.exit(2)
