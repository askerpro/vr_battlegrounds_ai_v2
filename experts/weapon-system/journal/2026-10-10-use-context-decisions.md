# Решения пользователя по use-context: политика отдачи через Binding и полный SDK lifetime-порт

Пользователь выбрал: отдачу корня у NPC запрещает `WeaponEquipmentBinding` через SDK-интерфейс
`IUxrFirearmRootRecoilPolicy` (запись в `Docs/weapons/decisions.md` пока только в ветке владельца) и
2026-10-10 — полный SDK lifetime-порт (контракт `weapon-grab-lifetime`, мотивация). Это решения, а не
приёмка: этапы use-context* в хабе verified технически (компиляция, ревью), Unity/Android/шлем/NPC не
проверялись, код не принят. Дата первого решения — unknown (2026-10-09 по строке decisions владельца).

Источники: хаб `weapon-system/use-context-planning`, `use-context-lifetime-planning` (evidence);
контракт `weapon-grab-lifetime`; остальные решения 2026-10-07…09 — `Docs/weapons/decisions.md`.
