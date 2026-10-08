# Оружие: единая WeaponSystem — задача и статус

Документ задачи направления «оружие» (протокол `.agents/rules/agent_coordination.md`). Ведёт владелец задачи
при каждом изменении статуса: принятое решение, влитый этап, начатая или законченная работа. Здесь итоги и план;
подробности — в технических документах по ссылкам.

**Обновлено:** 2026-10-08

| Документ | О чём |
|---|---|
| [weapon-system-refactor-plan.md](weapon-system-refactor-plan.md) | План рефакторинга WeaponSystem: архитектура, таблица переходов, этапы, решения S1–S4 (п. 5.2) |
| [weapon-ledger-single-source.md](weapon-ledger-single-source.md) | Учёт без копий физического состояния (этап C2), проба сетевой ошибки |
| [haptics-research.md](haptics-research.md), [haptics-system-task.md](haptics-system-task.md) | Система вибрации — отдельная задача `haptics`, контракт для этапа D |
| [weapon-readiness-feedback-design.md](weapon-readiness-feedback-design.md) | Предыдущая архитектура готовности оружия (этапы 1–3) — то, что сейчас заменяется |

## Цель и мотивация

Всё поведение огнестрела — затвор, патронник, спуск, поза, звук, вибрация, отклик — решает одна машина состояний
`WeaponStateMachine`. Учёт патронов один, в SDK; у каждого канала вывода один исполнитель. Сейчас за одно и то же
отвечают несколько компонентов (`WeaponReadinessController`, `AutomaticWeaponSlideFeedback`, `WeaponMechanismVisuals`,
`WeaponChamberingReminder`, `WeaponAttemptFeedback`, `UxrShotgunPump`), и они правят состояние друг за другом.
Из-за этого были рассинхрон позы, двойные звуки и сетевая ошибка учёта (C2).

## Границы

- Пишет: `Assets/Scripts/Weapons/**` (кроме `Equipment/**` — у bots-fix), SDK `Mechanics/Weapons/UxrFirearm*` и
  `UxrWeapon.Custom.cs`, префабы и данные оружия на волнах F, сборщики `Editor/VR_Battlegrounds/Gameplay/`,
  тесты `Assets/Tests/EditMode/Weapons/**`.
- Не пишет: `WeaponEquipmentBinding` (bots-fix), сервис вибрации `VrBattlegrounds.Haptics` (haptics),
  стену арсенала (arsenal-generator), общий сетевой код — только по согласованию.

## Архитектура

[План рефакторинга](weapon-system-refactor-plan.md), п. 2–4: чистая машина (`Weapons/Core`, без Unity) → датчики →
порт учёта SDK → исполнители позы, звука, отклика. Контекст использования оружия — `WeaponUseContext.Resolve`:
VR-рука (хват SDK) или экипировка NPC (`WeaponEquipmentBinding`), взаимоисключающие
(`.agent-state/coordination/weapon-system-to-bots-full-clips.md`).

## Сейчас

| Работа | Состояние | Чего ждёт |
|---|---|---|
| Правка машины S1/S2/S4 (этап `machine-s124`) | готова, не влита; Android PASS, тесты оружия 16/16 | проверки тени в шлеме: ноль расхождений на Herrington и FABARM, кроме Н6 → вливания |
| Контекст использования оружия (этап `use-context`): VR-рука или экипировка NPC, право автоматики ботов; отключение физической отдачи корня SDK в режиме экипировки NPC | контракт с bots-fix согласован; заменяет временный G1 (не влит) | проверенного `WeaponEquipmentBinding` от bots-fix → реализация → проверка на стенде ботов |

## Сделано

| Что | Коммит |
|---|---|
| Ручное заряжание дробовиков (FABARM, Herrington): патрон за патроном через окно приёма; выдача патронов карманом и арсеналом бесконечно; один звук вставки задаёт приёмник | e15c53ce |
| HoldOpen у Herrington: затвор открыт после последнего выстрела | 66dfba8e |
| План рефакторинга WeaponSystem принят | 6259ba33 |
| Этап B: машина состояний (чистый C#) + структурные тесты | 573ddab1 |
| Этап C: теневой режим на Herrington/FABARM + дебаг-панель состояния оружия в шлеме (только редактор, `Tools/VR Battlegrounds/Debug/`) | 06624cb9 |
| **Этап C2:** учёт без копии магазина (SDK-патч 53) — сетевая ошибка Herrington исправлена (форк ревизии после смены магазина или отпускания рукояти). Проба RED→GREEN, Android PASS, шлем. Попутно — предупреждение о кинематическом теле при выдаче магазинов арсеналом | 38381e54 |

## План (этапы)

| # | Этап (`id`) | Что даст игроку / проекту | Зависит от |
|---|---|---|---|
| 1 | Правка машины S1/S2/S4 (`machine-s124`) | машина ведёт себя так, как решено в шлеме | — |
| 2 | `WeaponUseContext` (`use-context`) | боты без VR-руки (clip-only) экипируют оружие и стреляют; право автоматики от контекста, не от руки; у NPC нет второго писателя корня (отдача) | `WeaponEquipmentBinding` от bots-fix |
| 3 | **D** — пилот (`pilot-d`): Herrington и FABARM на машине | один владелец позы, звука, отклика; звук оттягивания; отказ по темпу (вибрация + звук); помпа остаётся на месте | 1, 2; API вибрации от `haptics` |
| 4 | **E** — спуск в машину (`trigger-e`, SDK-патч — следующий свободный) | вся логика спуска в одном месте | 3 |
| 5 | **F1–F5** — волны стволов по типу затвора (`waves-f`, все 20) | единое поведение всего оружия; F1 — открытый затвор на пустом у Browning, Viper, TR15 | 4; map-runtime-bootstrap 2, arsenal-generator 3, bots-fix |
| 6 | **G** — приёмка (`acceptance-g`) | постоянные тесты, проверка двумя клиентами (стенд `Docs/test-stand.md`), документация | 5 |
| 7 | **H** — чистка (`cleanup-h`) | удаление старого кода (~2,5 тыс. строк) | 6 |

Идея на D/E: звук «патроны заканчиваются», как в CS2. Слой к звуку выстрела на последних N выстрелах, слышат все,
порог задаётся в профиле ствола.

## Критерии приёмки

- Каждый этап: Android PASS, консоль чистая, тесты оружия зелёные, проверка пользователем в шлеме.
- Тень на этапах до D: ноль неизвестных расхождений. После D — каждый перенесённый ствол без старых компонентов.
- G: два клиента — наблюдатель видит и слышит затвор, HoldOpen после позднего входа, нет двойного выстрела или дроби.

## Решения пользователя

| Тема | Решение |
|---|---|
| S1 | ствол без анимации пустого: на последнем выстреле обычная анимация выстрела |
| S2 | спуск до конца паузы между выстрелами — отрицательная вибрация + звук `UI_Error_Subtle_Deep`, без подсветки (у автоматического оружия нажатие в паузе — выстрел по окончании паузы, как SDK) |
| S3 | закрывается архитектурно (C2), а не условием в машине |
| S4 | отпущенная помпа FABARM остаётся на месте; у ствола признак «пружина / остаётся» |
| Звук оттягивания | чинится на этапе D |
| Число патронов не сошлось | непредвиденная ситуация: `GameLog.WeaponSystem.Error`, игроку игру не ломать |
| Расхождение версий учёта | серверная поправка (В-Л6) |
| Сбой уведомлений учёта | `Error`, стрельба не блокируется (В-Л5) |
| Смена магазина при открытом затворе | нужен новый цикл |
| Порядок | C2 до D; конвейер вливания агентов (`pipeline.md`) подтверждён |
| Вибрация | отдельная задача и агент; пишет сразу под будущую WeaponSystem; отдача — сигнал WeaponSystem |

## Ждёт решения пользователя

Нет.

## Следующие действия

1. Проверка тени в шлеме → вливание `machine-s124`.
2. После проверенного `WeaponEquipmentBinding` (bots-fix) — этап `use-context`, checkpoint для стенда ботов.
3. Этап D.

```coordination
{
  "task_id": "weapon-system",
  "owner": "weapon-system",
  "client": "claude",
  "session_id": "25b51afb-9e6a-4f64-b798-f8d815cab294",
  "worktree": "F:/CodexWorktrees/shotgun-per-shell/Vr_Battlegrounds_ai",
  "base_sha": "5aafcbc4644b02ad391143bbc0392563caa973d9",
  "doc_path": "Docs/tasks/weapon-system.md",
  "title": "Единая WeaponSystem: машина состояний оружия",
  "stages": [
    {
      "id": "machine-s124",
      "writes": [
        "Assets/Scripts/Weapons/Core/**",
        "Assets/Scripts/Weapons/WeaponSystem/**",
        "Assets/Tests/EditMode/Weapons/**",
        "Docs/tasks/weapon-system.md",
        "Docs/tasks/weapon-system-refactor-plan.md",
        "Docs/README.md",
        "Docs/CHANGELOG.md"
      ],
      "after": [],
      "needs": []
    },
    {
      "id": "use-context",
      "writes": [
        "Assets/Scripts/Weapons/WeaponUseContext.cs",
        "Assets/Scripts/Weapons/WeaponReadinessController.cs",
        "Assets/Scripts/Weapons/WeaponSystem/Sensors/WeaponContextSensor.cs",
        "Assets/ThirdParty/UltimateXR/Runtime/Scripts/Mechanics/Weapons/UxrFirearmWeapon.cs",
        "Assets/Tests/EditMode/Weapons/**",
        "Docs/tasks/weapon-system.md",
        "Docs/tasks/weapon-system-refactor-plan.md",
        "Docs/UltimateXR/sdk-patches.md",
        "Docs/README.md",
        "Docs/CHANGELOG.md"
      ],
      "after": [
        {"task": "weapon-system", "stage": "machine-s124", "state": "merged"}
      ],
      "needs": []
    },
    {
      "id": "pilot-d",
      "writes": [
        "Assets/Scripts/Weapons/**",
        "Assets/Prefabs/Weapons/Herrington/**",
        "Assets/Prefabs/Weapons/FabarmSDASS/**",
        "Assets/Editor/VR_Battlegrounds/Gameplay/**",
        "Assets/Audio/SFX/Weapons/**",
        "Assets/Tests/EditMode/Weapons/**",
        "Docs/tasks/weapon-system.md",
        "Docs/tasks/weapon-system-refactor-plan.md",
        "Docs/gameplay.md",
        "Docs/README.md",
        "Docs/CHANGELOG.md"
      ],
      "after": [
        {"task": "weapon-system", "stage": "use-context", "state": "merged"}
      ],
      "needs": []
    },
    {
      "id": "trigger-e",
      "writes": [
        "Assets/ThirdParty/UltimateXR/Runtime/Scripts/Mechanics/Weapons/UxrWeapon.Custom.cs",
        "Assets/ThirdParty/UltimateXR/Runtime/Scripts/Mechanics/Weapons/UxrFirearmWeapon.cs",
        "Assets/Scripts/Weapons/**",
        "Assets/Tests/EditMode/Weapons/**",
        "Docs/tasks/weapon-system.md",
        "Docs/tasks/weapon-system-refactor-plan.md",
        "Docs/UltimateXR/sdk-patches.md",
        "Docs/CHANGELOG.md"
      ],
      "after": [
        {"task": "weapon-system", "stage": "pilot-d", "state": "merged"}
      ],
      "needs": []
    },
    {
      "id": "waves-f",
      "writes": [
        "Assets/Scripts/Weapons/**",
        "Assets/Scripts/Arsenal/WeaponInfo.cs",
        "Assets/Data/Weapons/**",
        "Assets/Prefabs/Weapons/**",
        "Assets/Editor/VR_Battlegrounds/Gameplay/**",
        "Assets/Tests/EditMode/Weapons/**",
        "Docs/tasks/weapon-system.md",
        "Docs/tasks/weapon-system-refactor-plan.md",
        "Docs/README.md",
        "Docs/CHANGELOG.md"
      ],
      "after": [
        {"task": "weapon-system", "stage": "trigger-e", "state": "merged"}
      ],
      "needs": []
    },
    {
      "id": "acceptance-g",
      "writes": [
        "Assets/Tests/EditMode/Weapons/**",
        "Docs/tasks/weapon-system.md",
        "Docs/gameplay.md",
        "Docs/troubleshooting.md",
        "Docs/README.md",
        "Docs/CHANGELOG.md"
      ],
      "after": [
        {"task": "weapon-system", "stage": "waves-f", "state": "merged"}
      ],
      "needs": []
    },
    {
      "id": "cleanup-h",
      "writes": [
        "Assets/Scripts/Weapons/**",
        "Assets/Editor/VR_Battlegrounds/Gameplay/**",
        "Assets/Editor/VR_Battlegrounds/Weapons/**",
        "Assets/Tests/EditMode/Weapons/**",
        "Docs/tasks/weapon-system.md",
        "Docs/README.md",
        "Docs/CHANGELOG.md"
      ],
      "after": [
        {"task": "weapon-system", "stage": "acceptance-g", "state": "merged"}
      ],
      "needs": []
    }
  ]
}
```
