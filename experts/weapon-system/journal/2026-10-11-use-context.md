# Этап use-context принят и вливается одним пакетом с foundation bots-fix

Пользователь принял механику в шлеме 2026-10-11 (аренда 425). Пакет: наш own21 + foundation bots-fix
`93f67ded` (`WeaponEquipmentBinding`) + метки SDK71 по условию manipulation. Раздельно не вливается:
префабы PPK/AK105 ссылаются на Binding, Binding реализует наши SDK70/72.

Решения пользователя: совместный пакет, итоговый коммит собирает weapon-system, zone ACK bots-fix
на `armed-development` и `equipment-foundation`. Сетевое ревью network 3597 — ACK; условия 3–5 у bots-fix
до clip-cutover. Новая задача backlog `recoil-hand-follow` — кисть сопровождает ствол при отдаче.

Подробности и проверки: `tasks/weapon-system/changelog/2026-10-11-use-context.md`.
