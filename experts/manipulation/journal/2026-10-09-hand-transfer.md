# Своя вторая рука не перехватывает огнестрел: ManipulationConstraints, принято в шлеме

`f3dc728642ce6dd9a6132257cc323a20028734d1` (2026-10-09, в origin/dev). `ManipulationConstraints` —
выключатель `AllowHandTransfer` (по умолчанию выключен, решение пользователя) и область — огнестрел;
`TwoHandGrabPolicy` только механика. Тесты `GunTwoHandGrabTests`: перехват запрещён, выключатель возвращает.
Принято пользователем в шлеме 2026-10-09 (по тексту коммита).

Запись сделана этапом weapon-system `waves-f` (его writes включали `GrabRules.cs`, `ManipulationConstraints.cs`,
`GrabbableHierarchyCache.cs`) — пример правки в зоне хвата соседом; дальнейшие правки — через manipulation.

Источники: `git show f3dc72864`; `tasks/weapon-system/changelog/2026-10-09-waves-f.md`; `tasks/weapon-system/plan.json` этап `waves-f`.
