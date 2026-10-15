# Качество кистей и хватов MEF

**Задача/владелец:** `hand-rig-quality`, эксперт `avatar-grip`. **Обновлено:** 2026-10-15 (приёмка save и переход к extraction-run).
**Worktree:** `F:/CodexWorktrees/avatar-grip/Vr_Battlegrounds_ai`, ветка `codex/hands-rig-quality`.
Проверено после fetch/rebase 2026-10-15: `origin/dev 5435b3488`, HEAD до приёмки `ea2b4a7b6`, ветка впереди на 3 коммита.
Принятая правка `53b512cf6` (ранее `a9db66196` / `1dca4d3f5` / `4a11f5391`) — исправление `_assetId` в Grip Extraction;
**Unity Apply Viper PASS** на базе 97a320c88; новый прогон683: **56 PASS / 1 FAIL / 57**, полный Result сохранён.
Идентификаторы и цепочки поз PASS; FAIL — пустой список отключения своей головы у старого Cyborg.
677 — init failure, 680 и старые635/647 UNKNOWN сохранены; пользователь принял вливание save 2026-10-15;
публикация выполняется штатно. Остальные два коммита — передача и решения пользователя.

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

1. **Влить принятую правку `53b512cf6`** (GripRecordWriter: после `SaveAsPrefabAsset` —
   `NetworkAssetIdNormalizer.Normalize` + сверка `_assetId` на диске с `NetworkIdentity.AssetGuidToUint`).
   Apply Viper647 PASS, идентификаторы и цепочки поз683 PASS; пользователь: «тогда вливаем и переходим к run extraction?».
   Единственный FAIL старого Cyborg передан avatar-ik (7525), не переобъявляется PASS.
   Fast-forward в origin/dev и FIFO request-base; это **условие weapon-system 6388**.
2. **MKR9, основная точка, левая рука — кисть мимо рукояти** (подтверждено в шлеме; это и F4 у MEF).
   Разобрать зеркалирование источника для левой руки в `PackClipGripSource`, перезаписать.
3. **`grip-extraction-run`** (зарегистрирован, planned): R2 — остальные 17 точек паков → MEF_cyborg_hands;
   R3 — записи Cyborg → MEF_cyborg_hands (SDK-оружие, магазины, патроны, планшет, жетон); R4/R5 — по
   решению пользователя. Порядок прохода: apply с опорой по ладони + анализ → только `Recommended`
   подсказки (curl, сдвиг по нормали; руку «мимо рукояти» не догибать) → второй apply → листы ревью → шлем.
   Перед begin: zone-ack avatar-ik на `MEF_cyborg_hands/**`; до коммита — список и diff weapon-system.
4. Убрать `MEF_sdk_hands` из `Assets/Data/Teams/Terrorists_Team.asset` (решение 2026-10-14; реестр аватаров и
   префабы оставить; проверить `TeamAvatarsTests`; правка через Unity, зона avatar-ik — уведомить).
5. После: `wrist-gap` (зазор 2–3 см + материал «уступает кисти»), `grip-authoring` (ручные точки
   подушечек как источник Grip Extraction), palm-validation, dynamic-probes, mef-correction.

## Возобновление Codex — 2026-10-14

- Хаб: `hand-rig-quality`, owner `avatar-grip`; девять этапов `merged`.
  `grip-save-validation`: после release683 получена приёмка пользователя 2026-10-15;
  Apply PASS на прежней базе; свежие EditMode 56/57, Cyborg own-head FAIL остаётся отдельным дефектом.
  Приёмка касается сохранения `_assetId`; публикация и запись merged выполняются по штатному маршруту.
  `grip-extraction-run` и последующие этапы остаются `planned`.
  `grip-extraction-run` ещё не выполнялся. Общий mode off; очередь и аренды Unity сохранены.
- Старый маршрут Claude очищен (epoch 15). Свой Codex route опубликован после свежего desktop listing
  (epoch 21, `codex-app-threads`, thread `01a13af7-d0e0-7382-b1f7-c0f78057e8ed`).
  Межзадачная доставка — canonical inbox; native-уведомление дополняет его и не означает ACK.
- Network подтвердил событием 6647: общий помощник сохранения и патч Mirror остаются после
  `network-sync-hardening`; локальная нормализация не ждёт их реализации.
- Пять существующих EditMode fixtures выполнены в683 после runtime f6387f9 и явной подготовки Unity:
  NetworkAssetId 1/1, UxrUniqueIdOnDisk 1/1, Stability 2/2, AvatarHandPoseChain 13/13,
  RegisteredAvatarBody 39/40. FAIL: `Своя_голова_выключается` для `PlayerControllersCyborgAvatar` —
  `UxrMirrorAvatar.LocalDisabledGameObjects` пуст. Это результат EditMode, не наблюдение в шлеме.
  Cleanup PASS; DONE/received, result `ccf4a155`, paths/unexpected пусты. [Итоги](changelog/2026-10-15-tests-683.md).
- MKR9: статически подтверждено отражение противоположной руки по неверной оси корпуса (Body X
  направлена вверх оружия). Unity numeric A/B PASS: main Left сдвиг 208,0405 мм/180°, support Right
  97,4297 мм/180°; шесть controls неизменны, runtime/world и pose/current 0°, 34 asset/meta hashes
  совпали. Контакт/BakeMesh/Play/Quest ещё не проверены; изменённые хваты не сохранены.
  [Локальный разбор](reports/resume-2026-10-14/mkr9-diagnosis.md).
- Узкий `grip-save-validation`: ticket 628 отменён без аренды; ticket **635 DONE/received**,
  input `1e89792b`, result `c4f463d5`, `paths=[]`, `unexpected=[]`; editor освобождён.
  Avatar-ik zone-ack подтверждён событием 6727 (две позы), weapon-system — 6843 (Viper).
  Оба ACK узкие: неизменные байты трёх ассетов, идентификаторы и существующие проверки;
  на широкий `grip-extraction-run` они не распространяются.
  Встроенная Unity MCP-сессия вернула `Session terminated` на resources/list. Общий hub отвечает;
  новый [клиент штатного прокси](tools/mcp-client.py) прошёл initialize/tools-list (48 инструментов),
  без вмешательства в shared infra. **Unity Measure Viper PASS:** две точки, четыре руки,
  максимумы align/поз/runtime-world — 0 мм/0°, байты трёх ассетов совпали; `_assetId=1889608562`.
  [Локальный отчёт](reports/resume-2026-10-14/save-validation/save-validation.json).
  Пакет [tools/validate-grip-save.cs.txt](tools/validate-grip-save.cs.txt)
  по умолчанию выполняет Measure; Apply разрешён только после обоих свежих ACK.
- Решение по TeamData передано avatar-ik (6689/6729): владелец готовит отдельный этап после своего
  текущего checkpoint, префабы и реестр остаются. Наша заявка этот ассет не меняет.
- В Grip Extraction реализован только источник `pack`; для R3 (`avatar-record`) адаптер ещё нужен.
  Он должен сохранить независимо заданные обе кисти Cyborg; нынешний одно-ручный `GripSample`
  зеркалит вторую и теряет authored differences. [Source prerequisites](reports/resume-2026-10-14/r3-source-prerequisites.md).
  Уведомление 6897 от grab-authority: четыре известных coverage failure двух магазинов у
  MEF_sdk_hands_Player/Cyborg сохраняются; это результат чужого worker641, не проверка нашего WIP.
  Частично сверена карта инструментов; `reviewed_sha` не повышен до полной сверки зоны.
- Порядок `grip-save-validation → grip-extraction-run` зарегистрирован (ревизия 41), прежний database-lock
  не удерживает локальную работу. Общий хаб обслуживает maintainer; readiness 6805 обработан.
- **EditMode UNKNOWN:** ответ `run_tests` потерян при disconnect/reload до выдачи job_id.
  Native guard подтвердил `tests/compile/import/play=false`, test-probe отсутствует; это cleanup,
  не результат тестов. Новый запуск не выполнялся. Infra_issue **6809** передан сопровождающему;
  далее — read-only native SessionState, затем новая проверка после установления исхода.
- Следующий пакет: witness загруженного normalizer, native metadata собственного тестового запроса,
  Apply принятого Viper и числовая A/B-проба MKR9 на временных клонах. Изменённый хват MKR9 не сохраняется;
  неизвестный тестовый job не повторяется. Проба статически компилируется против worker DLL (exit 0),
  Ticket **647 DONE/received**, input `ac5adbe7`, base `97a320c88`, result `aefb9fc6`, paths/unexpected=[].
  Compiled normalizer witness=true; Apply Viper PASS (4 руки, 3 byte hashes unchanged, assetId1889608562).
  Fresh EditMode request647 после Apply также потерял ответ до job_id. Native до/после: current job empty,
  пять metadata jobs старше запроса, собственного нового job нет. Итог UNKNOWN, retry/clear_stuck не было.
  Native cleanup PASS: testsRunning=false, probe отсутствует. Worker освобождён; повторный infra_issue
  **6972** направлен agent-coordination-protocol. Отчёты `reports/resume-2026-10-14/`, [итоги](changelog/2026-10-14-save-validation.md).
- Policy7160 обработана: worker/broker/MCP infra_issue сразу в хаб, затем внутреннее уведомление
  проверенному чату сопровождающего без дополнительного вопроса пользователю. По issue6972
  native notification доставлена `сопровождающий` (`01a13af0-1b49-75c0-9258-37dcbe9ddab2`).
  Recovery664 завершён (7202). Worker_ready7257 обработан: билет сопровождающего667 DONE,
  база B54673ba4 восстановлена; unity-mcp SDK patch2 проверен на native prebuild24,9с/полном Result.
  Это инфраструктурная проверка; прежние UNKNOWN635/647 сохраняются.
- **Отказ resource listing:** после readiness штатный `list_mcp_resources(server="unityMCP")`
  в 2026-10-15 01:15 МСК всё ещё вернул `Mcp error32600: Session terminated`.
  Infra_issue7266 направлен в хаб и доставлен проверенному чату сопровождающего.
  [Отказ](reports/native-mcp-session-2026-10-15.md). Первоначальный вывод о полном отказе MCP
  исправлен: в новой попытке штатные tools/call read_console/execute_code/run_tests/get_test_job работали.
  Ошибка resources/list не запрещает доступные независимые Unity операции.
- Policy7257: самостоятельно не чинить и не обходить infra/worker/broker/proxy/hooks/MCP.
  Ремонт — только явно назначенный сопровождающий. Временный `tools/mcp-client.py` после
  этой политики не запускается. Read-only status/log/help и независимая avatar-подготовка
  разрешены. [Маршрут MKR9 contact-проверки](reports/mkr9-contact-package-2026-10-15.md) подготовлен
  read-only: actual BakeMesh(false), публичные FromGeometry/RenderEditorView, одинаковые камеры,
  mapping/scale self-roundtrip и right control. Одноразовый адаптер ещё не написан/не исполнен;
  ассеты не менялись.
  Собственные gameplay acceptance и zone ACK сохраняются.
- **Попытка по прямому поручению пользователя — 677:** input23557f15, base8eb877915,
  DONE/received, result4f1df746, paths/unexpected=[]. Новый job `8c540cf8a16c4c4e8ded80aba0e707ac`
  получен штатным run_tests. Первый get_test_job — reconnect timeout120с/7попыток; после свежего
  guard повторный status-poll того же ID успешен: terminal `failed`, `completed=0`, `total=null`,
  `result=null`, `Test job failed to initialize (tests did not start within timeout)`.
  Это зарегистрированный сбой инициализации, не результаты игровых assertions и не replay635/647.
  Native cleanup PASS: testsRunning=false, probeAbsent=true; worker/admission освобождены.
  Infra_issue7387 отправлен в хаб и native сопровождающему. [Итоги](changelog/2026-10-15-tests-677.md).
- **Ещё одна прямая пользовательская попытка — 680:** inputd62864c7, base8eb877915,
  DONE/received, resultb1d17468, paths/unexpected=[]. Штатные request/claim/begin/guard/preflight PASS.
  run_tests тех же пяти fixtures потерял ответ: plugin sessionb83fb05d-aaa2-4f17-a125-e6da328f4278
  disconnected while awaiting command_result, job_id отсутствует. UNKNOWN, hint retry не выполнялся.
  Native guard/cleanup PASS: tests/compile/import/Play=false, probeAbsent=true. Worker/admission освобождены.
  Infra_issue7446 в хабе и подтверждённом native канале сопровождающего. [Итоги](changelog/2026-10-15-tests-680.md).

## Ждёт пользователя

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
- 2026-10-14: `MEF_sdk_hands` — убрать из команды террористов (основа — `MEF_cyborg_hands`; префабы оставить как эксперимент).
- 2026-10-14: патч Mirror у network — после их текущих работ (network-sync-hardening); до него нормализуем у себя.

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
при недоступном native discovery маршрут очищается, чужой адрес не используется. Вопросы пользователю
в Codex — через доступный инструмент запроса ввода, с фиксацией незакрытого решения выше.
Обещания другим агентам — `experts/avatar-grip/backlog.md` (раздел «Обязательства»).

[Details](Details.md) · [plan.json](plan.json) · [Роль эксперта](expert/avatar-grip-expert.md) ·
[Инструкция](tool.md) · журнал `experts/avatar-grip/journal/`.
