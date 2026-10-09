# Библиотека звуков (Universal Sound FX)

Пак **вне проекта**: `F:\UnityProjects\_SoundLibrary\Universal Sound FX` (1,6 ГБ, 10 101 wav). В `Assets` его нет
(решение пользователя 2026-09-29): 10 тыс. файлов — минуты импорта, копия в `Library`, шум в поиске и риск попасть в
git/Plastic. В сборку всё равно ушло бы только то, на что есть ссылки.

## Как брать звук

1. Найти в библиотеке (каталог ниже, имена говорящие; `_RR1…RRn` — варианты одного звука для чередования).
2. Скопировать **wav вместе с `.meta`** в `Assets/Audio/SFX/<категория>/` — GUID сохранится, повторный импорт не плодит
   дублей.
3. Импорт под Quest: Force To Mono, Load Type — Decompress On Load для коротких (< 1 с), Compressed In Memory для длинных;
   Vorbis ~70 %.
4. Повесить на событие кодом/компонентом и закрепить тестом, как якоря (`AnchorSoundCoverageTests`).

## Формат

44,1 кГц, 16 бит; ~88 % моно (стерео — амбиенты, музыка, часть взрывов). Длительность: медиана 0,9 с, 90 % короче 4 с.
Под VR готовы почти как есть — в отличие от пака KINEMATION (стерео 48 кГц, 5–10 с).

## Каталог (что относится к игре)

| Папка | Файлов | Что внутри |
|---|---|---|
| `WEAPONS/Firearms/Fire_First_Person_Shooter_FPS` | 19 стволов × 3 варианта | выстрелы: штурмовые 01–03(b), пистолеты FS92 / P30L, дробовики 01–04, снайперские 01–02 (+глушитель), ПП 01–02 |
| `WEAPONS/Firearms/Handling` | ~40 | FS92: вставка/выемка магазина (полного/пустого), затвор, курок, сухой спуск, предохранитель; патроны из коробки |
| `WEAPONS/Firearms/Reloading` | ~28 | `RELOAD_Chamber`, `Clicks`, `Pump`, `Mechanical` ×15, рычажная перезарядка, сухой выстрел |
| `WEAPONS/Firearms/Casings` | ~17 | гильзы 9 мм, .45, 7,62, дробь — падение на твёрдое |
| `IMPACTS/Metal` | 49 | падения металла на твёрдое (`Objects_Drop`, `Tool_Drop`, `Rod_Drop`, `Pipe_Drop`), звон (`Cling_*`), дребезг |
| `IMPACTS/Bullets` | 39 | попадания пуль |
| `DOORS_GATES_DRAWERS` | 122 | металлические двери/люки, sci-fi тяжёлые двери с замком, ворота |
| `FOLEY/DRAWERS` | 20 | ящики: `DRAWER_Open/Close/Slide`, `Interact_Metal` |
| `LOCKS_KEYS` | 35 | `LOCK_Metal_Sliding` ×10 (засов), защёлки, навесной замок |
| `MECHANICS` | 55 | `Metal_Mechanism` ×30, цепи, завод |
| `ELEVATORS_LIFTS` | 64 | подъём/опускание: `Sequence_NN_Start/Movement_loop/Stop` |
| `USER_INTERFACES` | 391 | клики (`Clicks_Taps` ×99), `Mechanical` ×22, бипы, уведомления, ошибки, появление/исчезание |
| `NOTIFICATIONS`, `ALARMS` | 166 | сигналы (фазы раунда, таймер) |
| `HUMAN/Footsteps` | 1177 | шаги по поверхностям |
| `VOICES`, `GORE_SPLATS` | 1494 | крики, стоны, попадания по телу |
| `EXPLOSIONS` | 270 | для гранат |
| `FABRIC_CLOTHING` | 38 | движения ткани, молнии — карманы, разгрузка |

## Что уже взято (2026-09-29)

`Assets/Audio/SFX/Arsenal/` — стена (решётка, полки, засов), жетон; `Impacts/` — падение стволов и магазинов;
`Ambience/` — `AMBIENCE_City_Street_Calm_Day_loop`. Выбор — по названию и длительности, не на слух: не понравится —
заменить клип в `StandardArsenalWall` (`ArsenalWallSounds`), `DogTagPanel`, `ImpactSoundInstaller`, `Environment.prefab/Ambience`.

## Попадания пуль по поверхностям

Пары префабов находятся в `Assets/Prefabs/Weapons/Effects/`:

| Поверхность | Частицы | Декаль и звук |
|---|---|---|
| Бетон | `Impact_Concrete` — прежний `Impact_Default`, пыль и каменная крошка | `ImpactDecal_Concrete` — прежний `ImpactDecal_Default`, прежний след и звук SDK `ShotImpact` |
| Дерево | `Impact_Wood` — щепки Particle Pack и коричневая пыль | `ImpactDecal_Wood` — текстуры `BulletDecalWood`, `IMPACT_Wood_Stick_On_Wood_Post_01_mono` |
| Металл | `Impact_Metal` — искры Particle Pack и небольшой выброс пыли | `ImpactDecal_Metal` — текстуры `BulletDecalMetal`, `IMPACT_Bullet_Metal_01_mono` |

GUID бетонной пары сохранены: существующие ссылки оружия продолжают указывать на неё. Дерево и металл — варианты
бетонных префабов. Частицы одноразовые, с самостоятельным burst, без демо-мишени, коллайдеров и субэмиттеров;
материалы URP без Soft Particles. Размер корня частиц — прежние ×0,6. Материалы новых декалей локальные, текстуры
из Particle Pack; затухание использует `_BaseColor`. Бетонный визуал и звук сохранены.

На объект с коллайдером или его родителя добавить `UxrOverrideImpactDecal` и назначить нужный `ImpactDecal_*`
в `Decal To Use`. SDK выберет след и звук по свойству объекта. Громкость всех трёх — 0,5; новые клипы импортированы
моно, Vorbis 70 %, Decompress On Load. Подбор новых звуков — по названию и длительности; качество требует
прослушивания в Unity/шлеме.

**Предел штатного SDK:** `UxrOverrideImpactDecal` меняет только декаль и её звук. Частицы `Impact_*` назначаются
отдельно в `UxrProjectileSource` → тип выстрела → `Prefab Instantiate On Impact`. Автоматического выбора
частиц по поверхности пока нет; готовые варианты частиц не меняют это поведение. Для разных частиц на разных
объектах нужен отдельный согласованный маршрут выбора эффекта в SDK.

## Нарезка и проверка звуков

Исходники паков бывают испорчены: в файле почти тишина, весь цикл слит в один файл или под двумя именами лежат
одинаковые байты. Чинить нарезкой, а не правкой исходника.

- **Анализ и нарезка** — `Tools/Audio/audio_cut.py` (Python + numpy, только PCM WAV). `analyze` показывает уровень,
  огибающую, паузы и точки разреза. `cut --recipe` режет по JSON-рецепту из `Tools/Audio/recipes/`, где записана
  точка разреза и её обоснование. Повтор рецепта даёт те же байты. Подробности — `Tools/Audio/README.md`.
- **Результат** кладётся рядом с исходником с суффиксом `_Cut`; старые файлы остаются. `.meta` создаёт Unity.
- **Импорт** — `Tools/VR Battlegrounds/Audio/Apply SFX Import Settings` по выделенным клипам или папкам либо
  `SfxImportSettings.Apply(paths)` из `execute_code`. Утилита ставит Force To Mono и Vorbis 0,7. Load Type —
  Decompress On Load для клипов короче 1 с, иначе Compressed In Memory. Переопределение Android приводится к тем
  же значениям. Повторный вызов клипы не переимпортирует.
- **Проверка оружия** — preflight `WeaponSystemAuthoring` по каждому разрешённому звуку ствола:
  - пик ниже −40 dBFS («почти тишина») — ошибка, миграция не пройдёт;
  - `ActionBack` и `ActionForward*` побайтно одинаковы (один ассет или копии) — предупреждение.
  Уровень WAV читается из файла (`SfxClipLevel`), а не через `AudioClip.GetData`. Причины: `GetData` работает только
  при Decompress On Load, отдаёт сигнал после Vorbis и нормализации импорта и прячет дефект исходника.
  Не-WAV (mp3) проверяется через `GetData`, если клип распакован.
- **Подмена клипа ствола** — только через `WeaponSystemAuthoring.ClipOverrides` (имя корня → поле `_audio` → клип),
  затем `MigrateWeapons(...)`. Руками в префаб не ставить: следующий проход писателя вернёт прежнее.

Известное (2026-10-09): у SRM12 `BoltBack.wav` почти тишина (пик −51 dBFS), а `BoltForward.wav` содержит весь цикл.
Он нарезан рецептом `srm12-bolt.json`, SRM12 переключён на `_Cut`. У AK105, Herrington, MKR9, Mk14, TR15 и Viper
`BoltBack.wav` и `BoltForward.wav` побайтно одинаковы. Это предупреждение preflight, звук пока не заменён.

## Кандидаты под известные дыры (пользователь, 2026-09-29)

| Событие | Кандидаты |
|---|---|
| Стена арсенала открывается / закрывается | `ELEVATORS_LIFTS/ELEVATOR_Sequence_NN_Start…Stop` (решётка едет), `DOORS_GATES_DRAWERS/DOOR_Sci-Fi_Heavy_Metal_Close_Lock`, `LOCKS_KEYS/LOCK_Metal_Sliding_*` (засов в конце) |
| Полка выдвигается / задвигается | `FOLEY/DRAWERS/DRAWER_Slide`, `DRAWER_Open`/`DRAWER_Close`, `DRAWER_Interact_Metal` |
| Жетон снят / повешен | `LOCKS_KEYS/LOCK_Metal_Clasp`, `CLASP_Metal_Gate_Mechanism_Unclasp`, `IMPACTS/Metal/IMPACT_Metal_Cling_Bright` |
| Оружие упало на пол | `IMPACTS/Metal/IMPACT_Metal_Objects_Drop_Hard_Surface`, `IMPACT_Metal_Tool_Drop_Hard_Surface`, `IMPACT_Metal_Rod_Drop_Hard_Surface` (длинное) — 2–3 варианта |
| Магазин упал | `IMPACTS/Metal/IMPACT_Metal_Soft_Plate`, `IMPACT_Metal_Sheet_Subtle` |
| Гильзы | `WEAPONS/Firearms/Casings/CASING_*_Hard_Surface` |
| Магазин в оружие / из оружия | `Handling/HANDLING_B_FS92_9mm_Insert_Full_Magazine`, `Remove_Loaded_Magazine` / `Remove_Empty_Magazine` |
| Сухой спуск | `Handling/…Dry_Fire_Press_Trigger_Safety_Off`, `Reloading/RELOAD_Dry_Fire` |
| Выстрелы | `Fire_First_Person_Shooter_FPS`: штурмовые 01–03 (SCAR/AK/M16), ПП 01–02 (Uzi/MP5K), пистолеты FS92/P30L, снайперские 01–02, дробовики 01–04 |
