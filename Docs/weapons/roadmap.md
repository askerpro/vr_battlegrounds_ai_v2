# Очередь этапов

Единственный источник очереди: `tasks/weapon-system/Readme.md` ссылается сюда. Новый этап — строка здесь и в
`tasks/weapon-system/plan.json`; ? — нужно решение пользователя.

| # | Этап | Суть | Состояние |
|---|---|---|---|
| — | drive | Машина управляет оружием; Herrington, FABARM | влит 5285ceb7 |
| — | waves-f | F1–F4, дефолты отклика, конвейер звуков, частичный ход, запрет перехвата | влит 5af1c7ef |
| — | ejection | Вылет живого патрона и гильзы (визуал, видят все, слой Ignore Raycast); категория «снайперская винтовка»; повтор инициализации учёта | влит 722d14df |
| — | expert-kb | База знаний `Docs/weapons/` и профиль эксперта | влит cd497223 |
| — | handover | Передача эксперту: проект ejection-manual в Git, скрипты в `tasks/weapon-system/tools/`, очередь здесь | этот этап |
| 1 | ejection-manual | Гильза ручного цикла (FABARM, SRM12 = DT SRS, SniperRifle = AX-50): признак SpentCaseInChamber в учёте SDK, патч 69 (зарезервирован) | спроектирован, решения владельца есть — `tasks/weapon-system/ejection-manual.md` |
| 2 | waves-f5 ? | Revolver, R08, SDK Shotgun. Рекомендация A: барабан = магазин, NoAction + AutoOnMagazineInsert; SDK Shotgun — `PumpGrabFollow` + корпус (варианты — `tasks/weapon-system/Details.md`, «F5»). Револьверы сейчас без вылета гильз | ждёт решения |
| 3 | use-context | `WeaponUseContext` для ботов (`weapon-use-context@1`), SDK-патч 63 (отдача корня у NPC) | ждёт `weapon-equipment-binding` от bots-fix (foundation на 2026-10-09 не принят, активацию контракта bots-fix сообщит событием) |
| 4 | ammo-pack | Пачка патронов (спидлоадер, коробка) | идея пользователя |
| 5 | revolver-manual | Ручное заряжание револьвера по патрону | идея пользователя |
| 6 | trigger-e | Спуск в машину (SDK-патч 64); вибрация отдачи у WeaponSystem | план |
| 7 | haptics-swap | `WeaponHapticOutput` → `HapticService.Play` | после sdk-routing haptics-system |
| 8 | low-ammo | Слой «патроны кончаются», слышат все | решение есть |
| 9 | kinemation-clips | 9 стволов с одинаковыми клипами оттяжки/возврата — нарезать | предупреждение preflight |
| 10 | acceptance-g | Приёмка на двух клиентах; документация (gameplay, troubleshooting, захват — Issue 13) | после trigger-e, use-context |
| 11 | cleanup-h | Удалить AWSF, WMV, Reminder, абстрактный `WeaponReadinessController`, `WeaponShadowSettings`, пустое `_actionReturnPartial` у SRM12, черновик `MKR9_NativeProbe` | последний |

Риск (найдено 2026-10-09, проект ejection-manual): `WeaponLedgerReader` берёт «до» фиксации (`ChamberBefore`, позже
`SpentBefore`) из `_last`; фиксация в кадре загрузки снимка до `Drain` даст пропуск одного патрона/гильзы. Защита —
доверять `_last` только при `_last.Revision == commit.ExpectedRevision`. Отдельной правкой.

Риск (найдено 2026-10-09, `sight-calibration.md` п. 8): у обзорных копий `SightReview/*` три писателя —
`WeaponSystemAuthoring`, `ManualSightCalibrationAuthoring.Save` и `SightGameplayReviewBuilder`. Нарушает инвариант
«один writer префабов». Решить вместе с вопросом «как вписать сведение в WeaponSystemAuthoring»; до решения ручной
стенд Save/Apply не выполнять.

Наблюдать: рука у кармана рядом с пистолетом может взять его за поддержку вместо магазина; если мешает —
приоритет предмету в кармане (правило в `GrabRules`).

Известные красные тесты вне оружия (не наши): позы хвата Cyborg/DogTag/Tablet, позы рук относительно пака
(hand-rig-quality), карты, бюджет треугольников R08 (наш — решить в F5), общий звук выстрела у стволов HandsPack.
