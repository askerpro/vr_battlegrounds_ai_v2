# Скачивание клипов Mixamo

PowerShell-скрипты (у Bash-инструмента агентов нет сети). Справочник по клипам и их импорту —
[`Docs/avatar-animation.md`](../../Docs/avatar-animation.md#клипы).

## Токен

1. Войти на mixamo.com в Chrome, выбрать любой клип, нажать Download.
2. DevTools → Network → запрос `export` → правый клик → Copy → **Copy as cURL (cmd)**.
3. Сохранить в `mixamo.txt` в корне проекта. Файл исключён из git локально (`.git/info/exclude`) — **не коммитить**:
   в нём действующий токен Adobe. Токен живёт около суток; после работы файл удалить.

## Скрипты

| Скрипт | Что делает |
|---|---|
| `Find-Clips.ps1 -Query 'Walking Backwards'` | варианты клипов: имя, описание, длительность (одноимённых много) |
| `Find-Clips.ps1 -Query rifle -Type MotionPack` | пакеты и их id |
| `Get-Clip.ps1 -Name … -Description … -OutFile … [-Mirror]` | один клип |
| `Get-Pack.ps1 -PackId … -Prefix … -OutDir …` | пакет по клипу, без прыжков/смертей |
| `Get-ClipsById.ps1 -ListFile clips.txt -Prefix Sit_ -OutDir tmp\mixamo_sit` | клипы по id продукта (ссылки `…/products/<id>?…`, `-Ids` или файл со ссылками); имя файла — имя клипа + 8 знаков id |

Все клипы — **без «In Place»** (с root motion), без скина, FBX 2019, 30 fps, без прореживания ключей.
Качать в `tmp/` (вне Assets), затем переносить в `Assets/Art/Animations/Locomotion/<набор>/` под замком редактора и
запускать утилиту импорта клипов (см. справочник).

Параметры клипа (Speed, Posture, Overdrive…) уходят в экспорт по одному значению на пару, точкой. Если клип с
несколькими параметрами падает «Unknown error while generating motion» — проверить строку `params` в `MixamoCommon.ps1`.

Клипы-переходы (присед → сидение и т.п.) импортировать без цикла: `AvatarMixamoLocomotionSetup.EnsureClips(папка, loop: false)` —
Loop Pose подмешивает конец к началу. Кандидаты сидения — `Assets/Art/Animations/Locomotion/SitCandidates/`, приседа — `.../CrouchCandidates/` (не `Sit/`:
оттуда генератор берёт позу сидения контроллера), сравнение — стенд `Clip Scrub Stand` (выпадающие списки папок).

Обратного времени у Mixamo нет (Mirror там — лево ↔ право). Клип вставания → «садится»:
`Tools/VR Battlegrounds/Avatars/Reverse Selected Clips` (`AnimationClipReverse`) — копия `<имя>_Reverse.anim` рядом.
