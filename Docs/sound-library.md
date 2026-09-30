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
