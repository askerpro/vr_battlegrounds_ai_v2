# Качество кистей и хватов MEF

**Задача/владелец:** `hand-rig-quality`, эксперт `avatar-grip`. **Обновлено:** 2026-10-14 (передача Codex).
**Worktree:** `F:/CodexWorktrees/avatar-grip/Vr_Battlegrounds_ai`, ветка `codex/hands-rig-quality`.
Последнее влитое: `origin/dev 26129282b`. В ветке поверх него — WIP `6e0c34e4b` (исправление `_assetId`
в Grip Extraction, **в Unity не собиралось**) и коммит передачи. Продолжать только по файлам задачи.

## Цель

Кисти и хваты аватаров: MEF и новый `MEF_cyborg_hands` (кисть Cyborg в скелете MEF — основной путь
по решению пользователя). Анализатор контакта Hand Pose Fit даёт грубые нарушения и листы ревью;
Grip Extraction Tools переносит хват с кисти на кисть; финальная доводка поз — за человеком.

## Состояние на 2026-10-14

Влито (этапы `merged`): static-analyzer, expert-deployment, expert-registration, mef-grip-audit,
contact-metrics, hand-graft-exp, sdk-hands-seam, cyborg-grips (`d00f53667`), grip-extraction (`e16b86b82`).
- **Аватар `MEF_cyborg_hands`** — третий скин террористов (`Assets/Prefabs/Player/Experimental/MEF_cyborg_hands/`,
  Base GUID `71b74b0d…`). Записи хвата на 42 префабах; 30 поз оружия перекодированы с пястными
  (`HandPoses/`), базовые позы и поза по умолчанию — Cyborg. Шлем: «обе проблемы исправлены».
- **Grip Extraction Tools** (`Assets/Editor/VR_Battlegrounds/Avatars/GripExtraction/`, вход
  `GripExtraction.RunJson`): источник — клипы паков, цель — профиль (MEF / MEF_cyborg_hands), опора —
  запястье или центр ладони. R0 PASS. Пилот на Scar, MP5K, AK105, MKR9, SRM12, Viper влит;
  шлем: «стало гораздо лучше, всё сделанное можно выпускать».
[Подробно](Details.md#grip-extraction-tools-и-план-извлечения-хватов--2026-10-13-решение-пользователя).

## Дальше — следующий шаг

1. **Собрать и проверить WIP `6e0c34e4b`** (GripRecordWriter: после `SaveAsPrefabAsset` —
   `NetworkAssetIdNormalizer.Normalize` + сверка `_assetId` на диске с `NetworkIdentity.AssetGuidToUint`).
   Аренда worker: компиляция → `RunJson` apply на одном стволе пилота (результат должен совпасть с
   влитым, `_assetId` канонический) → EditMode `NetworkAssetIdOnDiskTests`, `UxrUniqueId`, `MEF_cyborg_hands`.
   Затем влить отдельным этапом/в `grip-extraction-run` — **условие weapon-system 6388**.
2. **MKR9, основная точка, левая рука — кисть мимо рукояти** (подтверждено в шлеме; это и F4 у MEF).
   Разобрать зеркалирование источника для левой руки в `PackClipGripSource`, перезаписать.
3. **`grip-extraction-run`** (зарегистрирован, planned): R2 — остальные 17 точек паков → MEF_cyborg_hands;
   R3 — записи Cyborg → MEF_cyborg_hands (SDK-оружие, магазины, патроны, планшет, жетон); R4/R5 — по
   решению пользователя. Порядок прохода: apply с опорой по ладони + анализ → только `Recommended`
   подсказки (curl, сдвиг по нормали; руку «мимо рукояти» не догибать) → второй apply → листы ревью → шлем.
   Перед begin: zone-ack avatar-ik на `MEF_cyborg_hands/**`; до коммита — список и diff weapon-system.
4. После: `wrist-gap` (зазор 2–3 см + материал «уступает кисти»), `grip-authoring` (ручные точки
   подушечек как источник Grip Extraction), palm-validation, dynamic-probes, mef-correction.

## Ждёт пользователя

- Оставить ли `MEF_sdk_hands` (кисть BigHands) вторым скином террористов или убрать — вопрос открыт.
- Запуск патча Mirror у network (`_assetId`/`sceneId` в preview-сцене, событие 6429) — по решению пользователя.
- R4 (заменить хваты MEF посадкой по ладони) и R5 (покрытие Cyborg/BigHands) — решение после R2/R3.
- Удалить вручную `C:\renders` (агент удалять вне workspace не может).

## Решения пользователя

- 2026-10-12: «анализ даёт грубые нарушения и удобные скрины… финальная настройка за человеком».
- 2026-10-12: пересадка кисти SDK в один скелет — основной подход; у команды минимум один аватар.
- 2026-10-13: «рука киборга гораздо лучше big hands» — основа `MEF_cyborg_hands`; шов у манжеты — позже.
- 2026-10-13: «да текущее вливаем фикс по манжетам сделаем позже — сейчас важнее дать новому аватару хваты».
- 2026-10-13: универсальный инструмент — «конвеер общий а киборг меф и оригинальная рука из сдк являются
  параметрами»; название «grip extraction tools + план по экстракшну»; опора — центр ладони клипа пака.
- 2026-10-13: «стало гораздо лучше… всё, что сделано уже можно выпускать»; MKR9 левая мимо — подтверждено.
- 2026-10-14: `_assetId` — «все 3» предложения владельцам; пилот починить вручную и влить, не ждать фикса.

## Риски и ловушки

- `PrefabUtility.LoadPrefabContents`/`SaveAsPrefabAsset` сбрасывает `NetworkIdentity._assetId` (→0),
  у вариантов аватара — `__prefabGuid`/`__isInPrefab` UltimateXR; сторож по загруженному ассету этого
  не видит. После записи сверять diff и гонять `NetworkAssetIdOnDiskTests`.
- Позы без пястных на кисти с пястными в рантайме SDK выкручивают пальцы (179°) — писать позы на риге
  цели (Grip Extraction делает), проверять «рантайм против редактора».
- Анализатор не видит неправдоподобную кисть и «мимо рукояти» — смотреть листы ревью глазами.
- Красные тесты, не от этой задачи: телепорт на BotCombatStand, `CorpseTests` у MEF_cyborg_hands
  (кости пальцев после пересадки), `GrabPoseCoverageTests` (намеренно), `HandsPack/KinemationHandPoseTests`
  на MEF (левая рука) — см. бэклог.
- Play агентам — только через PlayLaunch; аренды ≤20 мин, цикл продления ограничен 90 мин.

## Координация (Codex)

Входящие: `python Tools/agents/coordination.py inbox --owner avatar-grip --unread --as "<свой адрес>"`;
ожидание — `wait-inbox --owner avatar-grip --as "<свой адрес>"` фоновой командой. Вопросы пользователю
в Codex — явным сообщением с пометкой «ЖДЁТ ОТВЕТА» и дублем в «Ждёт пользователя» выше.
Обещания другим агентам — `experts/avatar-grip/backlog.md` (раздел «Обязательства»).

[Details](Details.md) · [plan.json](plan.json) · [Роль эксперта](expert/avatar-grip-expert.md) ·
[Инструкция](tool.md) · журнал `experts/avatar-grip/journal/`.
