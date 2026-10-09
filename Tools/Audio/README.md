# Tools/Audio — анализ и нарезка звуков

`audio_cut.py` — CLI для PCM WAV (8/16/24/32 бит, float 32). Нужны только Python 3 и numpy; ffmpeg и sox не
используются. Исходники не изменяются. Правила импорта и проверка в Unity — `Docs/sound-library.md`,
раздел «Нарезка и проверка звуков».

## Анализ

```powershell
python Tools/Audio/audio_cut.py analyze Assets/Audio/SFX/Weapons/Kinemation/SRM12/*.wav
python Tools/Audio/audio_cut.py analyze <wav> --window-ms 10 --search 0.30:0.50
python Tools/Audio/audio_cut.py analyze <wav...> --json
```

Команда печатает длину, каналы, частоту, разрядность, пик и RMS в dBFS. Также выводит огибающую: один символ на окно
`--window-ms`, пробел — пауза, `#` — пик. Ниже перечислены паузы между звучащими участками и точки разреза — самое
тихое 5-мс окно каждой паузы. В конце печатается sha256.
- «ПОЧТИ ТИШИНА» — пик ниже `--silence-dbfs` (по умолчанию −40 dBFS). Если такой файл есть, exit code равен 1.
- Пауза — окна тише пика огибающей на `--gap-db` (30 дБ) и не короче `--min-pause-ms` (20 мс). Порог
  относительный: так громкий и тихий клипы размечаются одинаково.
- `--search a:b` находит самое тихое место в диапазоне, когда нужная пауза короче порога.

## Нарезка

По рецепту (основной путь):

```powershell
python Tools/Audio/audio_cut.py cut --recipe Tools/Audio/recipes/srm12-bolt.json
python Tools/Audio/audio_cut.py cut --recipe Tools/Audio/recipes/srm12-bolt.json --check   # сверить без записи
```

Разовая нарезка аргументами — для проб; результат, который остаётся в проекте, оформляется рецептом:

```powershell
python Tools/Audio/audio_cut.py cut <src.wav> --start 0.045 --end 0.378 --out <dst.wav> [--fade-ms 5] [--normalize-dbfs -1]
```

- На обоих краях делается линейный фейд, по умолчанию 5 мс: без него на стыке слышен щелчок.
  `--normalize-dbfs` поднимает пик до заданного уровня.
- Выход пишется в формате источника (тот же chunk `fmt`). Посторонние chunk (LIST и т. п.) не переносятся.
- Если выход уже есть и отличается, без `--force` он не перезаписывается. Совпадающий файл не трогается.
  Запись в исходный файл запрещена.
- Результат детерминирован: повторный прогон рецепта даёт байт-в-байт тот же файл. `--check` это проверяет.

## Самопроверка

```powershell
python -m unittest discover -s Tools/Audio/tests -p "test_*.py"
```

Файлы проекта не меняются (пробы — во временной папке): все рецепты `recipes/*.json` сверяются через `cut --recipe ... --check`, `--check` ловит отличающийся и
отсутствующий выход и ничего не пишет, `analyze` распознаёт «почти тишину» исходника SRM12 и пропускает нарезку.
Запускать после правки `audio_cut.py` или рецепта.

## Рецепт

`Tools/Audio/recipes/<имя>.json`, пути от корня репозитория:

```json
{
  "note": "зачем нарезка",
  "fade_ms": 5,
  "jobs": [{
    "source": "Assets/.../X.wav",
    "source_sha256": "<полный sha256 из analyze>",
    "cut_note": "откуда точка разреза: вывод analyze, паузы, минимум",
    "outputs": [
      { "path": "Assets/.../X_Part1_Cut.wav", "start": 0.045, "end": 0.378, "note": "что внутри" },
      { "path": "Assets/.../X_Part2_Cut.wav", "start": 0.378, "end": 0.70, "normalize_dbfs": -1 }
    ]
  }]
}
```

`source_sha256` защищает от тихой подмены исходника: если источник изменился, рецепт падает, и точки разреза надо
пересмотреть. `fade_ms` и `normalize_dbfs` задаются на уровне рецепта, задания или выхода; приоритет у выхода.

## Добавить рецепт

1. `analyze --window-ms 10` по исходнику; при необходимости `--search` вокруг ожидаемой паузы.
2. Записать рецепт: точки разреза, обрезку тишины в начале (задержка отклика) и обоснование в `cut_note`.
3. `cut --recipe ...`, затем `analyze` по выходам: ни один не должен быть «почти тишиной».
4. Выходы класть рядом с исходником с суффиксом `_Cut`. Старые файлы не удалять.
5. В Unity: импорт (`.meta` создаёт Unity, GUID руками не генерировать), затем
   `Tools/VR Battlegrounds/Audio/Apply SFX Import Settings` или
   `VrBattlegrounds.EditorTools.Audio.SfxImportSettings.Apply("<путь клипа или папки>")`.
6. Назначить клипы через писателя данных (для оружия — `WeaponSystemAuthoring.ClipOverrides`) и прогнать preflight.

## Рецепты

| Рецепт | Что делает |
|---|---|
| `recipes/srm12-bolt.json` | SRM12: `BoltForward.wav` (весь цикл) → `BoltBack_Cut` 0,045–0,378 с и `BoltForward_Cut` 0,378–0,70 с; разрез в паузе 0,350–0,405 с (минимум −62 dBFS) |
