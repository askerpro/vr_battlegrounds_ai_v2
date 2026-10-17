# SceneDetail → GPU diagnostics: границы и данные v1

2026-10-15, ответ scene-detail на perf event8625. Статус: предложение точного
контракта для согласования и регистрации perf, **не реализованный API**, не contract-ack.
Инструменты, включая диагностический экспорт, остаются у performance; автор сцены
задаёт тестовую конфигурацию, проверяет связи и затем анализирует результаты.

## Подтверждённый источник

Accepted core `74bd42f5760b5e3f04d2e886a5685a2f52ff3d1d`, сцена Lobby.
Пять локальных файлов сверены Git blob-ID с этим commit: Profile, Applier,
Editor SceneDetailSetup, LobbyDetailProfile.asset и Lobby.unity — совпадение 5/5.
Ни HEAD/index основного checkout, ни чужой checkout не изменялись. Для реализации
использовать свою принятую базу origin/dev, содержащую core, не копировать main WIP.

| Файл | Git blob accepted core |
|---|---|
| Assets/Scripts/Maps/Detail/SceneDetailProfile.cs | 141aa8dc821cdface1e481e0cc3b5880bd886b83 |
| Assets/Scripts/Maps/Detail/SceneDetailApplier.cs | 031b42fadeea4897c97ac236dce6e1f98d3aeb01 |
| Assets/Editor/VR_Battlegrounds/Maps/Detail/SceneDetailSetup.cs | 7041402cd45a7787343be182c3dc16d9d2e65e89 |
| Assets/Scenes/Lobby/LobbyDetailProfile.asset | 019a73c8046b58faa4f5147c2d00b72a0e3d50c1 |
| Assets/Scenes/Lobby.unity | 2ef2552c7507eaeb74d0eca9377a6ec8d2164166 |

## Что уже доступно

`SceneDetailApplier.Current`, `Profile`, `Live` — публичные getters.
`SetData(profile, LodEntry[], RendererEntry[])` — authoring mutation, не snapshot.
`Apply()` — действующий writer; диагностика его не вызывает.
LodEntry: group/distance; RendererEntry: renderer/distance/size. Сами `_lods`,
`_renderers`, `_originalLods`, `_baseEnabled` и `_baseTextureSettings` — private.
`SceneDetailDebugSection.Snapshot()` возвращает строку для перестройки UI,
зависит от Development; не содержит объектов, исходных уровней или resource identity.
`SavedPath(scene)` указывает JSON сохранения; автоматического runtime load этого
JSON сейчас нет. `SceneDetailSetup.Collect()` меняет записи/texture groups/сцену:
для диагностического экспорта его вызывать нельзя.

Editor read-only exporter может читать сериализованные `_profile/_lods/_renderers`
через SerializedObject, original LOD/resource IDs — из edit-time сцены. На устройстве
`GetLODs()` после Apply даёт уже обрезанный список, исходный индекс из него теряется.
Reflection private-полей, runtime AssetDatabase и догадки по именам не нужны.

## Минимальный runtime accessor: предлагаемый точный API

`public bool TryCaptureDiagnostics(out SceneDetailDiagnosticsSnapshot snapshot,
out string unavailableReason)` у SceneDetailApplier. Вызывать на Unity main thread,
в момент начала/конца измерительной фазы, не каждый кадр. При отсутствии живого
профиля/инициализации/владения Current возвращает false и явную причину.

Snapshot содержит отсоединённые копии значений/массивов, Unity refs только для
сопоставления с build manifest. Ничего не вызывает Apply/SetData/SetLODs/ForceLOD,
не переключает renderer, quality, профиль, сцену, свет или текстуры. Getter не
должен передавать наружу изменяемые внутренние массивы или Live как writable DTO.

- status, scene/applier/profile refs, копия live midStart/farStart/mid/far;
- groups: LODGroup ref, cached arenaEdgeDistance, применённая полоса и Clamped rule,
  исходные уровни из `_originalLods` (порог/fade/renderer refs), текущие effective
  `GetLODs()`; effectiveIndex → originalIndex = index + clamp(lodSkip,0,count−1);
- managed renderers: ref, baseline enabled из `_baseEnabled`, current enabled,
  cached arenaEdgeDistance/size для записей мелочи; без догадки о причине видимости;
- texture groups: имя, наличие, baseline settings и фактические current settings;
  глобальный mip limit и lodBias записать как контекст, не менять;
- момент снимка/сцена, объём и полнота; отсутствующие записи отмечать явно.

`original` здесь означает сохранённое в Awake состояние до собственного Apply;
edit-time authoring и runtime original сверять по manifest. `enabled`, `isVisible`
и присутствие в effective списке не доказывают выбранный камерой LOD/draw.
`observed` остаётся null до подтверждения capture. Общий renderer нескольких
уровней/групп сохраняет все связи, не сворачивается в одну ложную принадлежность.
Ресурсные GUID/localID/contentHash и GlobalObjectId даёт Editor build manifest;
runtime InstanceID — только локальный ключ с run/build identity. Light/capture
атрибуция относится к perf exporter, не к SceneDetail writer.

## Точный предлагаемый scope

Perf реализует в **своём checkout** после записи расширенного stage/контракта:

- `Assets/Scripts/Maps/Detail/SceneDetailApplier.cs`: только описанный read-only
  accessor, без изменения Apply/Restore/профилей/lifecycle;
- новый `Assets/Scripts/Maps/Detail/SceneDetailDiagnosticsSnapshot.cs` и его meta:
  DTO снимка; никаких writer/MonoBehaviour, файлового вывода или GPU-метрик;
- Editor exporter под `Assets/Editor/VR_Battlegrounds/Performance/` и runtime
  package/phase bridge в собственном performance scope; формат scene.json —
  gpu-package-v1, версия и schema mapping фиксируются perf;
- данные тестового профиля из соседнего JSON принадлежат автору; создание временной
  копии/применение в запуске только штатным вариантом через существующий Applier,
  после согласования сценария с launch. Нет нового reader/writer профиля в диагностике.

SceneDetailSetup.cs, Lobby.unity, постоянный LobbyDetailProfile.asset, texture-meta,
UI, Quality и SDK для такого accessor **не требуются** и scope не получают.
Это architectural zone consent на перечисленный read-only подход, не технический
immutable grant и не scope admission. Если схема/пути меняются — согласовать diff.
План perf должен сохранить владение инструментами performance вместо назначения
реализации exporter автору сцены; автор принимает связи/данные и делает правки сцены.

## Чистая пара и кандидат

[lod-ab-control-v1.json](lod-ab-control-v1.json) — декларация, **не готовый runtime
input**. Оба варианта: midStart10, mid.lodSkip0, far.lodSkip9, все textureBias0 и
cullSize0. Только farStart A25/B20. Исключения/textureFolders и все остальные поля
профиля/сцены сохраняются. Перед A/A/A/B фаза должна подтвердить реальные значения,
одинаковые texture-group settings и enabled мелочи; не применять одновременно UI.
Выбранная граница касается 40 записей LODGroup из690 в [20;25) м. Это список
**смены правила**, не доказанная видимость, смена mesh или стоимость каждой группы.
Для effective delta нужен snapshot оригинальных/оставшихся LOD всех690 групп.
Эта пара использует отдельный контрольный профиль и не считается сравнением
производственного профиля без изменений с оптимизированным: исходные texture/cull
намеренно одинаково отключены в обоих вариантах для разделения причин.

Кандидат GapFillFootSlope/SM_Cliff_03, group1035138807, distance24.958735,
четыре LOD, crossfadeOFF. center-north — лишь исходная точка; фиксированный вид
из арены на bounds кандидата сохраняется с реальными pose/projection. Если исходный
LOD уже последний/объект вне захвата, выбрать другой допустимый вид/кандидат,
не использовать ForceLOD и не объявлять observed по правилу.

## Готовность и проверки

Сейчас готовы контракт, авторская декларация A/B, полный статический список40,
проверка accepted source5/5. Новый API/exporter, сцена в сборке и GPU A/B не готовы.
После реализации: compilation + применимые существующие проверки, read-only diff
снимков без side effects, authoring→original/effective→captured mesh. Новые gameplay
tests до пользовательской проверки не писать. Нужны source/build/manifest hashes,
phase/view readiness, ordinary Q3 A/A и ≥3 пар с detailedOFF отдельно от capture.
Нет повторной пользовательской приёмки или build/capture разрешения из этого документа.
