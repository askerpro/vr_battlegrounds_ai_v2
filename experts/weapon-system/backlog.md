Обновлено 2026-10-11. Кандидаты вне хаба; порядок — приоритет; ? — нужно решение пользователя.

1. `author-local-feedback` — подсказка, сухой щелчок и отказ идут автору по роли; бот = обычный аватар (решение пользователя 2026-10-12, bots-fix 4190): на хосте автор ствола бота — хост, человек увидит подсказку на оружии бота. Добавить условие `UxrAvatarMode.Local` пользователя, как у вибрации (`WeaponHapticOutput`). До armed runtime bots-fix — ответ 4198.
2. `ledger-last-revision` — `WeaponLedgerReader` доверяет `_last` без сверки ревизии: фиксация до `Drain` теряет патрон/гильзу; доверять при `_last.Revision == commit.ExpectedRevision` — `Docs/weapons/roadmap.md` «Риск».
3. `recoil-hand-follow` — при отдаче кисть должна всегда сопровождать ствол и повторять его движение. Сейчас SDK-отдача одной рукой смещает корень ствола относительно руки (`UxrFirearmWeapon.RootGrabbable_ConstraintsApplied`), и в шлеме ощущается, что ствол скользит в ладони (замечено на PPK 2026-10-11 при приёмке use-context). Задача пользователя. Отдача наша; кисть до запястья ведёт avatar-ik, позы — avatar-grip: нужен согласованный дизайн, второго писателя позы кисти не допускать.
4. `sight-writer` ? — три писателя `Assets/Prefabs/Weapons/SightReview/` (writer, `ManualSightCalibrationAuthoring.Save`, `SightGameplayReviewBuilder`); свести в writer, до решения Save/Apply стенда не выполнять — `Docs/weapons/sight-calibration.md` п. 7.1, 8.
5. `roadmap-sync` — roadmap и Readme задачи в origin/dev без цепочки use-context из хаба; сверить при ближайшем docs-этапе — хаб.
6. `waves-f5` ? — Revolver, R08, SDK Shotgun на машину (рекомендация A; `PumpGrabFollow` + корпус; бюджет треугольников R08) — roadmap п. 2, `tasks/weapon-system/Details.md` «Этап waves-f».
7. `haptics-swap` — `WeaponHapticOutput` → `HapticService.Play` после `haptics-system/sdk-routing` (manipulation) — roadmap п. 7.
8. `low-ammo` — слой звука «патроны кончаются», слышат все; решение есть — roadmap п. 8, `Docs/weapons/decisions.md` «Механика».
9. `kinemation-clips` — 9 стволов с одинаковыми клипами оттяжки/возврата; нарезать конвейером `Tools/Audio` — roadmap п. 9.
10. `handspack-shot-sound` ? — красный тест «общий звук выстрела у стволов HandsPack», владелец не определён — roadmap «Известные красные тесты».
11. `ammo-pack` — пачка патронов (спидлоадер, коробка), идея пользователя — roadmap п. 4.
12. `revolver-manual` — ручное заряжание револьвера по патрону, идея пользователя — roadmap п. 5.
13. `pocket-grab-priority` — наблюдать: у кармана рука берёт пистолет за поддержку вместо магазина; правило `GrabRules` — через manipulation — roadmap.
14. `sight-open-questions` — WeaponRegistry и дубли `*_SightReview`, Viper, SniperRifle, MKR9, Approved/Apply, zoom, eyebox — `Docs/weapons/sight-calibration.md` п. 7.2–7.9.
