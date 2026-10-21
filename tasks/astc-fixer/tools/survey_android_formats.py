"""Обзор Android-форматов текстур проекта по .meta (только чтение).

Считает Android override по форматам и текстуры, которые без сжатия в Default
и без Android override (на Quest соберутся несжатыми). Запуск из корня worktree:
python -X utf8 tasks/astc-fixer/tools/survey_android_formats.py
"""
import collections
import os
import re

FORMAT_NAMES = {
    -1: "auto", 3: "RGB24", 4: "RGBA32", 45: "ETC2_RGB4", 47: "ETC2_RGBA8",
    48: "ASTC_4x4", 49: "ASTC_5x5", 50: "ASTC_6x6", 51: "ASTC_8x8", 52: "ASTC_10x10", 53: "ASTC_12x12",
}
TEXTURE_META = re.compile(r"\.(png|tga|jpg|jpeg|psd|tif|tiff|exr|bmp)\.meta$", re.I)


def platform_block(text, target):
    start = text.find("buildTarget: " + target + "\n")
    if start < 0:
        return ""
    end = text.find("  - serializedVersion", start)
    return text[start:end if end > 0 else start + 800]


def main():
    formats = collections.Counter()
    uncompressed = []
    total = 0
    for root, _, files in os.walk("Assets"):
        for name in files:
            if not TEXTURE_META.search(name):
                continue
            path = os.path.join(root, name).replace(os.sep, "/")
            text = open(path, encoding="utf-8", errors="ignore").read()
            if "TextureImporter" not in text:
                continue
            total += 1
            android = platform_block(text, "Android")
            overridden = re.search(r"overridden: (\d)", android)
            if overridden and overridden.group(1) == "1":
                fmt = int(re.search(r"textureFormat: (-?\d+)", android).group(1))
                formats[FORMAT_NAMES.get(fmt, str(fmt))] += 1
                continue
            default = platform_block(text, "DefaultTexturePlatform")
            compression = re.search(r"textureCompression: (\d)", default)
            if compression and compression.group(1) == "0":
                uncompressed.append(path[: -len(".meta")])
    print("textures", total)
    print("android overrides", formats.most_common())
    print("uncompressed in Default, no Android override:", len(uncompressed))
    by_pack = collections.Counter("/".join(p.split("/")[:5]) for p in uncompressed)
    for pack, count in by_pack.most_common(20):
        print("  ", count, pack)


if __name__ == "__main__":
    main()
