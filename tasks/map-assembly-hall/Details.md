# Цех-кузница: звуковая атмосфера

Подробный анализ задачи; статус и план — в [Readme.md](Readme.md).
Замысел карты и подбор визуальных ассетов уже описаны в
`Docs/level-design/maps/automated-assembly-hall.md` и
`Docs/level-design/maps/automated-assembly-hall-decoration-plan.md`; здесь только звук.

Проверка источников 2026-10-06. Звуки не скачивались и на слух не проверялись:
перед импортом каждую позицию прослушать в Unity или шлеме.

**Принцип.** Звуки не играют все одновременно: тихий фон (1–2 петли), точечные петли
на ближайших объектах и отдельные события по таймеру с весами, интервалами,
кулдаунами и группами взаимоисключения. Эмбиент не заглушает шаги и выстрелы игроков.

**Источники.** В проекте эмбиента для цеха нет; в `EnvironmentPackCatalog` аудио нет.
Основа — купленная библиотека Universal Sound FX
(`F:\UnityProjects\_SoundLibrary\Universal Sound FX`, `Docs/sound-library.md`).
Её петли короткие (1–11 с), поэтому длинный фон берётся с Freesound (CC0).
Сварки и ковки в библиотеке нет.

## Universal Sound FX: подходящие файлы

| Файл | Длит. | Каналы | Роль |
|---|---|---|---|
| `MACHINES/Factory/MACHINE_Factory_Machine_Heavy_Slow/Medium/Light_loop` | 8 с | моно | Цех: гул машин, 3D-точки |
| `MACHINES/Factory/MACHINE_Boiling_Molten_Metal_Furnice_loop`, `ELEMENTS/Fire/FIRE_Hot_Furnice_Smooth_loop`, `HVAC/HEATING/HEATING_Furnace_Fire_01…04_loop` | 3–6 с | моно | Цех: печь |
| `MACHINES/Factory/MACHINE_Press_Machine_*_loop`, `MACHINE_Press_Machine_Crunch` | 1–2 с | моно | Цех: пресс |
| `ROBOTICS/Servos/ROBOTIC_Servo_*` (89 шт.) | 0,5–1,3 с | моно | Цех: манипуляторы |
| `INDUSTRIAL/INDUSTRIAL_Conveyor_Belt_01…15_loop` | ~5 с | моно | Цех: конвейер |
| `ENGINES_MOTORS_GENERATORS/GENERATOR_Steam_01/02_loop`, `MACHINES/Industrial/MACHINE_Air_Compressor_*_Air_Leak` | 2–5 с | моно | Цех: пар, пневматика |
| `HAZARDS_TRAPS/HAZARD_Chains_Cogs_01_loop`, `FOLEY/CHAINS/CHAIN_Pull_01_loop` | 2–5 с | моно | Цех: каретки на рельсе |
| `ELECTRICITY/ELECTRICITY_Sparks_*` | 0,4 с | моно | Цех: искры |
| `ALARMS/Digital/ALARM_Military_Large_Echo_loop`, `ALARMS/Air_Raid/*` | — | — | Цех: сирена линии |
| `AMBIENCES/SciFi/AMBIENCE_SciFi_Large_Space_Hangar_Deep_Smooth_loop`, `AMBIENCE_SciFi_Lava_Molten_Deep_loop`, `AMBIENCE_SciFi_Hum_01…07_loop` | 4–11 с | стерео | Цех: мрачный тон помещения |


### Базовый фон

1. **Тон помещения** — 2D: [dersinnsspace «Industrial factory working 03»](https://freesound.org/people/dersinnsspace/sounds/439401/) (2:51, CC0) или [Lewente «Industrial ambience»](https://freesound.org/people/Lewente/sounds/393398/). Под ними тихим слоем `AMBIENCE_SciFi_Large_Space_Hangar_Deep_Smooth_loop` — мрачность в духе «Матрицы».
2. **Локальные 3D-петли на объектах**, радиус 8–20 м, одновременно звучат только ближайшие 4–6: печи (`HEATING_Furnace_Fire_*`, `FIRE_Hot_Furnice_Smooth`), конвейеры (`INDUSTRIAL_Conveyor_Belt_*`), станки (`MACHINE_Factory_Machine_*`).

### События

| Событие | Источники | Длит. | Интервал | Пространственность | Ограничения |
|---|---|---|---|---|---|
| Цикл манипулятора | `ROBOTIC_Servo_*`; [4ntony Robotic Arm](https://freesound.org/people/4ntony/sounds/204801/), [peridactyloptrix Servo](https://freesound.org/people/peridactyloptrix/sounds/188821/), [Audionauten](https://freesound.org/people/Audionauten/sounds/448375/) | 1–6 с | 8–25 с на руку | 3D на руке, синхронно с анимацией | Звучат только ближайшие 2 руки; не громче шагов |
| Сварка и искры | [squashy555 Arc Welding](https://freesound.org/people/squashy555/sounds/256915/), [SpeedY welder 4](https://freesound.org/people/SpeedY/sounds/62925/), [unfa MIG](https://freesound.org/people/unfa/sounds/264827/), [kev_durr Sparks](https://freesound.org/people/kev_durr/sounds/396470/), `ELECTRICITY_Sparks_*` | 2–8 с | 20–60 с | 3D у сварочного поста, синхронно с VFX | Треск в полосе шагов: ≤ −10 dB |
| Удар пресса или молота | `MACHINE_Press_Machine_Crunch`; [mark646](https://freesound.org/people/mark646/sounds/209219/), [Andriejus](https://freesound.org/people/Andriejus/sounds/866648/), [adamcreeper Forging](https://freesound.org/people/adamcreeper/sounds/780503/), [ldezem blacksmith](https://freesound.org/people/ldezem/sounds/386393/) | 1–3 с, серия 2–4 удара | 30–90 с | 3D у пресса | Похоже на взрыв или выстрел: ставить далеко и ритмично |
| Каретка на подвесном рельсе | `HAZARD_Chains_Cogs_01_loop`, `CHAIN_Pull_01_loop`; [craigsmith Overhead Crane](https://freesound.org/people/craigsmith/sounds/438133/), [kyles hoist chains](https://freesound.org/people/kyles/sounds/453383/), [RutgerMuller chains](https://freesound.org/people/RutgerMuller/sounds/155859/) | 5–15 с | 20–60 с | Сплайн по рельсу, лёгкий доплер | Одна каретка за раз |
| Пар и пневматика | [brunoboselli pressure release](https://freesound.org/people/brunoboselli/sounds/457294/), [wubitog hiss](https://freesound.org/people/wubitog/sounds/234782/), [nuckan hydraulics](https://freesound.org/people/nuckan/sounds/212941/), `GENERATOR_Steam_*` | 1–4 с | 15–40 с | 3D на клапанах | Широкополосный шум маскирует шаги: коротко и тише |
| Сигнал смены цикла | [qubodup Factory Warning Buzzer](https://freesound.org/people/qubodup/sounds/832414/), [Latranz Industrial Alarm](https://freesound.org/people/Latranz/sounds/520200/), [kyles alarm sequence](https://freesound.org/people/kyles/sounds/450130/), `ALARM_Military_Large_Echo_loop` | 2–6 с | 120–300 с | 3D на громкоговорителе или тихо 2D | Тембр не как у сигналов раунда; не во время отсчёта |
| Далёкий удар металла | `IMPACTS/Metal/*` с реверберацией; [RICHERlandTV Factory Atmosphere](https://freesound.org/people/RICHERlandTV/sounds/240134/) | 1–3 с | 10–30 с | 3D в глубине цеха | Не ближе 15 м к игроку |
| Выпуск металла из печи | `MACHINE_Boiling_Molten_Metal_Furnice_loop` + `THRUSTER_Deep_Burning_Air_*` | 8–15 с | 180–360 с | 3D у печи | Кульминация; не одновременно с сиреной |

### Кандидаты

| Источник | Лицензия | Содержимое | Роль |
|---|---|---|---|
| Universal Sound FX (локально) | Куплен | Печи, прессы, сервоприводы, конвейер, пар, цепи, искры, sci-fi гул | Основа цеха (только 3D-точки) |
| [dersinnsspace 439401](https://freesound.org/people/dersinnsspace/sounds/439401/), [Lewente](https://freesound.org/people/Lewente/sounds/393398/), [neilraouf Factory_Ambiance](https://freesound.org/people/neilraouf/sounds/431480/), [fimrod Large Warehouse](https://freesound.org/people/fimrod/sounds/278987/) | CC0 ✔ | 22 с – 2:51 | Тон помещения |
| [jodybruchon «Metal workshop»](https://freesound.org/people/jodybruchon/sounds/475217/) | CC0 ✔ | 11:15; горелки, сварка, болгарки | Нарезка событий |
| [szegvari Steampunk Factory](https://freesound.org/people/szegvari/sounds/580633/), [Kostrava abandoned warehouse](https://freesound.org/people/Kostrava/sounds/240895/) | CC0 ✔ | 1:39 / 3:29 | Мрачные слои |
| [thefilmbakery Blacksmith](https://freesound.org/people/thefilmbakery/sounds/137836/), [ldezem blacksmith](https://freesound.org/people/ldezem/sounds/386393/), [adamcreeper Forging](https://freesound.org/people/adamcreeper/sounds/780503/) | CC0 ✔ | 7,6 с – 17 мин | Кузня, удары молота |
| [mark646 press](https://freesound.org/people/mark646/sounds/209219/), [Andriejus](https://freesound.org/people/Andriejus/sounds/866648/), [artinstitute](https://freesound.org/people/artinstitute/sounds/468094/) | CC0 ✔ | 0,6 с – 2:14 | Пресс |
| [squashy555 Arc Welding](https://freesound.org/people/squashy555/sounds/256915/), [SpeedY welder 4](https://freesound.org/people/SpeedY/sounds/62925/), [unfa MIG](https://freesound.org/people/unfa/sounds/264827/) | CC0 ✔ | 4,8–19,7 с | Сварка |
| [kev_durr Arc Welding](https://freesound.org/people/kev_durr/sounds/396511/) / [Sparks](https://freesound.org/people/kev_durr/sounds/396470/) | CC BY 4.0 / CC0 ✔ | 8,6 / 11,8 с | Сварка (атрибуция) и искры |
| [4ntony](https://freesound.org/people/4ntony/sounds/204801/), [peridactyloptrix](https://freesound.org/people/peridactyloptrix/sounds/188821/), [Audionauten](https://freesound.org/people/Audionauten/sounds/448375/) | CC0 ✔ | 0,8–80 с | Манипуляторы |
| [craigsmith](https://freesound.org/people/craigsmith/sounds/438133/), [kyles hoist](https://freesound.org/people/kyles/sounds/453383/), [RutgerMuller](https://freesound.org/people/RutgerMuller/sounds/155859/) | CC0 ✔ | 52 с – 2:23 | Подвесной рельс |
| [Sonniss GDC 2015](https://archive.org/details/SonnissGameAudioGDC): «Eiravaein Works — Ilmarinen» (кузница), «Julian Ray — Background Machinery», «Kpow — Industrial Disquiet», «RDGSFX003 — Industrial_Warehouse», «Robobiotics» (сервоприводы) | Royalty-free, коммерция ✔ | Молот, наковальня, шипение металла, машины, сервоприводы | **Лучший бесплатный набор для цеха** |
| [SoundFellas «Factory Plants and Industrial Machines Ambience Kit»](https://assetstore.unity.com/packages/audio/ambient/noise/factory-plants-and-industrial-machines-ambience-kit-48594) | $64, Asset Store EULA | 2,4 ГБ | Платно, если бесплатного не хватит |
| [Epic Sounds «Industrial and Construction Ambience Loops»](https://assetstore.unity.com/packages/audio/sound-fx/industrial-and-construction-ambience-loops-389082) | $49 | 4,0 ГБ, 2026 | Платно; больше стройка, чем цех |
| [ZapSplat «Foundry Sound Effects»](https://www.zapsplat.com/sound-effect-packs/foundry-sound-effects/) | Заявлено: бесплатно с атрибуцией / Gold без; страница 403 | 32 звука литейной | Проверить лицензию вручную |

**Стартовый набор.** Тон — dersinnsspace (90 с) и тихий SciFi Hangar. 3D-петли печей, конвейера и станков — из
библиотеки. События: сервоприводы (библиотека + 4ntony), сварка (squashy555, SpeedY, искры), пресс (mark646 +
`Press_Machine_Crunch`), рельс (craigsmith, kyles), пар (brunoboselli), сигнал (qubodup), металл (`IMPACTS/Metal`).
Дополнительно скачать Sonniss GDC 2015 («Ilmarinen», «Background Machinery»).
С атрибуцией (CC BY 4.0): [InspectorJ — Machine Factory](https://freesound.org/people/InspectorJ/sounds/385943/) (1:14, фон),
[Benboncan — Anvil & Hammer](https://freesound.org/people/Benboncan/sounds/103630/) (удар по наковальне).

### Платные

| Источник | Цена, лицензия | Содержимое | Статус |
|---|---|---|---|
| [Stormwave — Sci-Fi Manufacturing SFX](https://assetstore.unity.com/packages/audio/sound-fx/sci-fi-manufacturing-sound-effects-pack-215007) | $4,99 | 35,5 МБ, sci-fi производство | Проверено. **Оправданная первая покупка** — по духу «Матрицы» |
| [Stormwave — Robotic Servo SFX](https://assetstore.unity.com/packages/audio/sound-fx/robotic-servo-sound-effects-pack-103244) | $8,99 | 34 МБ, сервоприводы | Проверено |
| [Vadi — Industrial Machines SFX](https://assetstore.unity.com/packages/audio/sound-fx/industrial-machines-sfx-pack-310844) | $25,35 | 2,5 ГБ, станки, мастерская | Проверено |
| [Epic Sounds — Industrial and Construction Ambience Loops](https://assetstore.unity.com/packages/audio/sound-fx/industrial-and-construction-ambience-loops-389082) | $49 | 4,0 ГБ | Проверено; пригодится и для стройки |
| [SoundFellas — Factory Plants Ambience Kit](https://assetstore.unity.com/packages/audio/ambient/noise/factory-plants-and-industrial-machines-ambience-kit-48594) | $64 | 2,4 ГБ, 106 записей: залы, печи, линии | Проверено |
| [Soundbits — The Blacksmith](https://sonniss.com/product/blacksmith) | ~$21 по акции (обычно $42) | 800 файлов, 4 ГБ: наковальня, цепи, металл | Заявлено |
| [ZapSplat — Foundry](https://www.zapsplat.com/sound-effect-packs/foundry-sound-effects/) | ~£9,99 или бесплатно поштучно с атрибуцией | 32 звука литейного цеха | Заявлено (403) |
| [BOOM Library — Mechanicals](https://www.boomlibrary.com/sound-effects/mechanicals/) | от ~$139 | Механика | Заявлено, избыточно |


## Реализация в Unity и Quest (общая для серии карт)


Предложение, не утверждённая архитектура.

**Структура.**
- `AmbienceProfile` (ScriptableObject) на сцену: петли фона и список `AmbientEvent { clips[], weight, cooldown, minInterval..maxInterval, spatial: 2D/Point/Path, spawnPoints/spline, volumeRange, pitchRange, exclusiveGroup, blockedByCombat }`.
- `AmbienceDirector` — один на сцену, локальный `MonoBehaviour`. Звук косметический, сеть не нужна: каждый клиент играет свою случайную последовательность. Общие для всех события (один и тот же пролёт вертолёта) — по желанию, через `[ClientRpc]` с seed и временем старта.

**Планировщик.**
- Тикер раз в 1–2 с. Когда прошло «окно тишины», выбрать событие взвешенным рандомом из тех, у которых истёк кулдаун и свободна группа.
- Группы: «Тяжёлое» (вертолёт, грузовик, гудок, выпуск печи, сирена) — одно за раз; «Фон» (птицы, чайки, радио) — до двух.
- После тяжёлого события пауза 20–40 с. Один клип не повторять подряд; pitch ±5 %, громкость ±2 dB.

**Голоса (Quest 2/3).** На эмбиент не больше 8 реальных голосов: 1–2 петли фона, до 4 ближайших 3D-петель,
до 2 событий; остальное — оружию, шагам и голосовому чату. `AudioSource.priority` эмбиента 160–200.
Дальние петли выключать по дистанции (проверка раз в 0,5 с).

**Импорт.**
- Длинные петли фона (> 10 с, стерео): Streaming, Vorbis ~50–60 %; стримов одновременно 1–2.
- 3D-петли 2–15 с: Compressed In Memory, Vorbis ~70 %, Force To Mono.
- Короткие события (< 3 с): Decompress On Load, моно.
- Длинные события (вертолёт): Compressed In Memory, моно, Preload Audio Data.
- Все 3D — моно. Готовые стерео-пролёты играть 2D или свести в моно и вести по сплайну.
- 96/192 кГц и 24/32 бит пересчитать в 44,1 кГц / 16 бит до импорта.

**Пространственность.** 3D: Spatial Blend 1, Logarithmic Rolloff, Min distance 3–10 м.
Пролёт: Doppler 0,5–1, движение по `SplineContainer` или Lerp между точками за 15–40 с.
Meta XR Audio spatializer — только на крупных событиях, не на фоне.

**Микшер.** Группа `Ambience` (`Bed`, `Events`) отдельно от `Weapons`, `Footsteps`, `Voice`.
Дакинг от `Weapons`: −6…−10 dB, attack 50 мс, release 1–2 с. Во время боя тяжёлые события не стартуют,
интервалы ×2. Фон −20…−26 dBFS RMS; на группе срезать 2–4 кГц на 2–3 dB, чтобы шаги и выстрелы читались.

**Мелочи.** Фон «дышит»: медленная модуляция громкости ±2 dB с периодом 20–40 с. Готовые стерео-пролёты
в 3D не ставить: получается двойной доплер и неверная панорама. Последовательности (кран: мотор → бипы → удар)
делать одной корутиной внутри события. Снапшот «Combat» выключает события, которые игрок может принять
за игровые сигналы: далёкую стрельбу, артиллерию, сирены, бипы. Дебаг-переключатели (показать точки и сплайны,
запустить событие X) — в EditorPrefs и меню `Tools/VR Battlegrounds/Debug`, не в git.

**Атрибуция.** CC BY (klankbeeld, juskiddink, InspectorJ, Benboncan, Airborne80, kev_durr Arc Welding) — в титры и файл кредитов.
CC0, Sonniss и Universal Sound FX атрибуции не требуют.


## Отсеянные звуки


| Что | Почему |
|---|---|
| [jgrzinich forest gunfire](https://freesound.org/s/397370/), [SoundLover16 Steel works](https://freesound.org/people/SoundLover16/sounds/430895/), Orange Free Sounds foghorn | CC BY-NC — коммерция запрещена |
| BBC Sound Effects (RemArc) | Только некоммерческое использование |
| mil0001 Helicopter Flyby | Синтез из голоса, нереалистично |
| freetousesounds Hong Kong Port как фон | Ночная запись; только нарезка событий |
| Бандлы Sonniss 2016–2024 | Состав не проверен (Cloudflare); бандла 2025 не было |
| BigSoundBank «Gulls on the Harbor», ZapSplat | 403, лицензия не проверена |
| Universal Sound FX `BATTLES/Medieval_*` | Средневековье, не к лобби |
| `AMBIENCE_City_Street_Calm_Day_loop` в лобби | Город, не лес и стрельбище — заменить |
