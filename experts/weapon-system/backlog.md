Обновлено 2026-10-11. Кандидаты вне хаба; порядок — приоритет; ? — нужно решение пользователя.

1. `ledger-last-revision` — `WeaponLedgerReader` доверяет `_last` без сверки ревизии: фиксация до `Drain` теряет патрон/гильзу; доверять при `_last.Revision == commit.ExpectedRevision` — `Docs/weapons/roadmap.md` «Риск».
2. `sight-writer` ? — три писателя `Assets/Prefabs/Weapons/SightReview/` (writer, `ManualSightCalibrationAuthoring.Save`, `SightGameplayReviewBuilder`); свести в writer, до решения Save/Apply стенда не выполнять — `Docs/weapons/sight-calibration.md` п. 7.1, 8.
3. `roadmap-sync` — roadmap и Readme задачи в origin/dev без цепочки use-context из хаба; сверить при ближайшем docs-этапе — хаб.
4. `waves-f5` ? — Revolver, R08, SDK Shotgun на машину (рекомендация A; `PumpGrabFollow` + корпус; бюджет треугольников R08) — roadmap п. 2, `tasks/weapon-system/Details.md` «Этап waves-f».
5. `haptics-swap` — `WeaponHapticOutput` → `HapticService.Play` после `haptics-system/sdk-routing` (manipulation) — roadmap п. 7.
6. `low-ammo` — слой звука «патроны кончаются», слышат все; решение есть — roadmap п. 8, `Docs/weapons/decisions.md` «Механика».
7. `kinemation-clips` — 9 стволов с одинаковыми клипами оттяжки/возврата; нарезать конвейером `Tools/Audio` — roadmap п. 9.
8. `handspack-shot-sound` ? — красный тест «общий звук выстрела у стволов HandsPack», владелец не определён — roadmap «Известные красные тесты».
9. `ammo-pack` — пачка патронов (спидлоадер, коробка), идея пользователя — roadmap п. 4.
10. `revolver-manual` — ручное заряжание револьвера по патрону, идея пользователя — roadmap п. 5.
11. `pocket-grab-priority` — наблюдать: у кармана рука берёт пистолет за поддержку вместо магазина; правило `GrabRules` — через manipulation — roadmap.
12. `sight-open-questions` — WeaponRegistry и дубли `*_SightReview`, Viper, SniperRifle, MKR9, Approved/Apply, zoom, eyebox — `Docs/weapons/sight-calibration.md` п. 7.2–7.9.
