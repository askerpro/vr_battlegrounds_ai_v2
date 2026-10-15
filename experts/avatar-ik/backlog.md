Обновлено 2026-10-11. Кандидаты вне хаба; порядок — приоритет.

1. `hand-writer-contract` — контракт порядка писателей кисти с avatar-grip/manipulation (список — AGENT.md «Invariants») — `expert-map.md` «Границы между соседями».
2. `bots-fix-overlap` — с bots: `equipment-foundation` пишет `UxrArmIKSolver.cs` (SDK62), `clip-cutover` — `Player/Avatars/**`, NPC без Body/Arm/Leg IK; запросы legs-ik 124, 934 без ответа — хаб, legs-ik Details «Координация».
3. `sdk-patch-numbers` — патч ног 50 не в реестре (51 занят UniqueId), 35/37 без записи; `patch-reserve` + changelog — legs-ik `changelog/2026-10-06-sdk-ultimatexr-50-legs-pose-mixer.md`.
4. `t50-field-acceptance` — приёмка калибровки этапа 4: метки, смена тела/карты, переподключение, отказ замера, хост + клиент в XR — `Docs/tasks/T-50-stage4-plan.md` «Приёмка и итоговые проверки».
5. `renderer-ownership` — направление A черновика (явный состав тела, контракт editor-сборщиков), B → network; решение пользователя — `tasks/avatar-renderer-regression/architecture/Design.md` (checkout 2cf1).
6. `legacy-legs-cleanup` — убрать `LegsGrounding`/`BoneLocalPose`, ожидания тестов префабов после приёмки ног; `game-variant.md` §5 и troubleshooting «Голова вдавлена в плечи» устарели — `Docs/avatar-animation.md` «Сборка и границы миграции».
7. `heavy-head-hide` — Heavy/старые Military: пустой `Local Disabled Game Objects`, обход `EyesForwardOffset` ~0.11; заполнить по `game-variant.md` §3а — troubleshooting «Вижу свою голову изнутри».
8. `avatar-workbench-acceptance` — приёмка редактора аватара, Blender/SDK-маршрут, 8 отказов prefab-тестов (4 ног, Cyborg → PlayerBase, 3 кармана) — `Docs/avatar-editor-workbench.md` «Проверки и приёмка».
9. `uxr-frame-input-kb` — KB по UxrManager (порядок кадра), Devices, Locomotion; из `uxr-core-kb` manipulation (карта ред. 2) — `Docs/UltimateXR/architecture.md`, `Docs/UltimateXR/locomotion.md`.
