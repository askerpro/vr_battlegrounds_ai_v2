# ACK weapon-grab-lifetime r2 с условиями; bots-fix — zone ACK только на технический прогон

2026-10-11, ревью правок соседей в `Assets/ThirdParty/UltimateXR/Runtime/Scripts/Manipulation/`.

## weapon-system: контракт `weapon-grab-lifetime` r2 — ACK (по 3310)

Сверено `4dee70e1a..030037fbd`: три вызова патча 67 (`UxrGrabManager.cs` 913/921/940) и `GrabCandidates.cs` сохранены,
патчи 16/18/25–28/26 не тронуты. `GrabLifetimeEpoch` — несериализуемый stamp SDK, не копия grab-state; второго
писателя нет. Условия до merge отправлены weapon-system: сохранить вызовы патча 67, полная метка
`VR Battlegrounds patch 71` вместо `SDK71:` в трёх файлах. Процессный разрыв: этап verified до нашего ACK.

## bots-fix/armed-development — zone ACK только DEVELOPMENT (по 3392)

Конфликт с инвариантом 2: `IUxrGrabAvailability` в геттере `UxrGrabbableObject.IsGrabbable` — второй канал допуска
в обход `GrabRules`. Перф: `GetComponentsInParent` на каждое чтение до грубой отсечки (каждый предмет, каждый кадр).
Gate в `GrabObject` для полученных событий меняет инвариант 5 — нужен network. Незаявленный патч 56 без резерва.
Дизайн до merge: шаг claim в `GrabRules`, геттер `IsGrabbable` без изменений, кешированный провайдер, gate — только
серверный backstop с логом. Исключение для binding в `invariants.md` — после согласования дизайна.

Источники: inbox 3310, 3392; дифф bots — незакоммиченные правки `F:/CodexWorktrees/bots/Vr_Battlegrounds_ai` на 57898794c.
