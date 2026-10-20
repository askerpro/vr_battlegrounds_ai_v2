# Машиночитаемый индекс сцены v1

18.10.2026. Потребитель — агент, анализирующий сцену. HTML не входит в требования.
Реализация: Tools/Perf/gpu_analyze.py и gpu_diagnostics/scene_data.py.

`inspect-scene <scene.json> --out <новый-каталог>` создаёт:

- `manifest.json`: kind=vrb.scene-index, schemaVersion=1, captureId, sourceSha256,
  словарь files с относительными фиксированными путями, SHA256 и размерами bytes.
- `raw/scene.json`: точные исходные bytes; все поля и неизвестные значения сохранены.
- `entities.jsonl`: одна запись на object/renderer/resource/lod/light. Поля kind/id/
  path/name/type/sourcePointer/data. data содержит исходную запись без преобразования
  authored/original/effective/observed. sourcePointer — JSON pointer в raw/scene.json.
- `relations.jsonl`: sourceId/targetId/relation/sourcePointer/resolved. Направления:
  object→renderer/parent, renderer→mesh/material, material→shader/texture,
  LOD→object/renderer (раздельно authored/original/effective), light→object/cookie.
  У LOD pointer сохраняет номер уровня. Это подмножество известных связей; остальные
  исходные ссылки сохранены в data/raw. resolved означает наличие обеих записей в
  индексе, а не доказанную GPU-атрибуцию.
- `summary.json`: инвентарь, ограничения, исходные условия/observations, структурные
  кандидаты. Первые25 по каждому отдельному признаку; полное количество указано,
  исходные записи доступны через entities. null не превращается в0.

`scene-query <package> [--id ID] [--search TEXT] [--kind KIND] [--full]` возвращает
JSON с items/total/offset/limit/nextOffset. По умолчанию data не печатается; --full
возвращает исходные поля выбранной записи. Поиск — по ID, name, path и type без
учёта регистра. `scene-links <package> ID` возвращает входящие/исходящие связи.
Пагинация: --limit1–100 (default25), --offset>=0. Перед выдачей сверяется SHA256
читаемого JSONL с manifest. Результат содержит sourceSha256 и fileSha256.

Все результаты этого формата имеют status=structural-only. Кандидаты имеют
evidence=structuralCandidate, gpuTimeMs=null, собственные unit/meaning/nextStep.
Mesh vertexCount относится ко всему shared resource; texture width×height — к mip0
одной поверхности, без оценки VRAM; passCount — к объявленным passes материала;
число shader references — к материалам. Эти числа не складываются в GPU-время.
Unknown: actual draw/pass, видимость и выбранный камерой LOD, texture residency,
shadow casters/atlas cost. GPU bottleneck и эффект исправления доказываются отдельно
runtime/capture/A/B пакетами с проверенными условиями.

Первый реальный индекс: reports/scene-machine-20261018, из исторического r2 экспорта
15Oct, SHA256106787b4a09483364ff4d1e39c6d25c05cd5871f039761b9a34c9cf32375d7cb.
Его дата и источник не подменяются текущим состоянием сцены или runtime.
