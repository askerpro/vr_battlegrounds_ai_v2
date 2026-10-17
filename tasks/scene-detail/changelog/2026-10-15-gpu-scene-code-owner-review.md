# Owner-review SceneDetail GPU evidence: checkpoint016fd8

2026-10-15, ответ на perf event8785. **Блокирующих замечаний в проверенном
статическом scope не найдено.** Структура пригодна как индекс сцены и read-only
снимок original/effective для последующей атрибуции. Это не приёмка кода пользователем,
не аппаратный PASS и не завершение всей GPU-диагностики.

Источник — immutable checkpoint `016fd8f05832a59f46f7a041af82926865291edd`,
прочитанный через общий Git object store основного checkout; физический WIP perf
не копировался. Сверены три Git blob:

| Файл | Blob |
|---|---|
| Assets/Scripts/Maps/Detail/SceneDetailApplier.cs | 7504df1d8751afb22bff28308598580d367e3a5c |
| Assets/Scripts/Maps/Detail/SceneDetailDiagnosticsSnapshot.cs | 802fb4a8462e4205acf25107b61fbc10975dc360 |
| Assets/Editor/VR_Battlegrounds/Performance/GpuSceneManifestExporter.cs | b2308136ef8c382e88861788a36725b8e22764f2 |

Principal проверил accessor/DTO; отдельный read-only reviewer проверил exporter
и immutable контракт r1. Principal просмотрел ключевые участки экспортёра и уточнил
путь заполнения runtime/edit-time distance records. Изменения writer/lifecycle
в Applier отсутствуют: diff добавляет216 строк, удалений0. `git diff --check`
для трёх путей checkpoint — exit0. Это статическая проверка, не compilation.

## Подтверждённое кодом

- Applier64–77: собственный active Current, профиль/live и совпадающий кэш обязательны;
  при отсутствии — false/null и причина. Нет Apply/SetData/SetLODs/ForceLOD внутри
  диагностического пути. Исходные методы применения/возврата не переписаны.
- Applier85–115,221–275: original/effective копируются, включая renderer arrays.
  Mapping проверяет clamped skip, длину, threshold/fade, порядок и renderer identity.
  При несовпадении весь mapping=-1, причина и incomplete; индекс0 после skip
  не выдаётся за исходный LOD0. Наличие в effective не объявляет observed draw.
- Applier117–172: повторные managed renderer records сохранены, baseline/current
  различаются, потерянные ссылки и отсутствующие texture settings не скрываются.
- Exporter304–337,568–575: authored/edit-time отделён от runtime original/effective,
  неоднозначные записи сохраняются массивами. observed/expected/projection неизвестны.
  Runtime CaptureDetail138–149 завершается до Edit-time заполнения distances152–169:
  одна штатная runtime запись не получает ложную неоднозначность от повторного чтения.
- Exporter237–300,435–452,590–599: material slots/submeshes/shared resources сохраняют
  связи. Static-batch или ненулевой start без подтверждённого диапазона — unresolved,
  не весь combined mesh, приписанный одному renderer. Это structural metadata, не draws.
- Exporter403–419,476–488,526–556: resource GUID/localID, dependency hash и SHA256
  source bytes различены. Edit-time GlobalObjectId проверяется; runtime identity
  явно runLocal и не подменяет authoring. Неизвестные joins требуют manifest.
- Exporter341–370,578–587: missing runtime records остаются в snapshot; отсутствующий
  renderer даёт currentEnabled=null и причину, не выдуманное выключение.
- Exporter375–400: свет/baking/shadow settings доступны как структура; неизвестные
  caster/atlas/capture связи помечены. Unity writer calls в exporter не обнаружены;
  файловая запись ограничена новым отчётом через CreateNew.

## Практическая полезность и открытые проверки

Автор сможет найти объект/ресурс и проверить смену доступных LOD, правила и эффект
на структуру. Это ещё не доказательство выбранного камерой меша или GPU-стоимости.
Для Quest необходимо отдельное подтверждённое runtime→authoring/build-manifest
сопоставление и capture mesh/resource IDs. Static-batch mapping остаётся ограниченным,
пока не появится фактическое доказательство диапазона.

Ожидается фактический Unity export от perf после его аренды758/receive: compile,
состав/полнота scene.json и отсутствие изменений сцены до/после. Результат этой
аренды здесь не подтверждён. Ожидается затем normal Q3 A/A и matched A/B отдельно
от detailed captures, по ранее согласованному test profile; снимок enabled/effective
не заменяет эти измерения.

Новые тесты, probes и исполняемые ожидания не создавались/не запускались. Unity,
ADB, сборки и GPU-захваты здесь не выполнялись. Код/assets/сцены не менялись;
основные HEAD и index сохранены. После изменения этих blob потребуется адресное
повторное ревью соответствующих изменений, а не перенос текущего вывода автоматически.
