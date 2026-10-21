# bots-input: курок бота штатным путём, досылание при Initialize (патчи 89, 90), 2026-10-22

Бот стреляет курком через штатный `UxrFirearmWeapon`: ввод держащего аватара (`grabber.Avatar.ControllerInput`)
и Local-гейты по `HasInputHere` (патч 89). Патч 90: `TryInitializeReadiness` досылает без `HasReloaded` —
`prepared = M>0 ∧ ValidateChamberCompletion`; ствол, выданный в кобуру до Start оружия, больше не требует досылания.
Влито в dev c7b8d6ab3 (задача bots-input).

Для зоны:
- Тесты: `InitializePreparedTests` — магазин в гнезде до Start → патронник; пустой магазин и Action вне покоя → нет.
- Лог: `[WeaponSystem] … учёт инициализирован (попытка N) — патронник, где оружие, почему пуст`; сухой щелчок —
  строка с причиной отказа (`LogDryFire`).
- Известный красный вне задачи: `LedgerSpawnReadyTests.Отказ_replay_при_выключенном_учёте_виден_один_раз` устарел
  после 044888e1b (WeaponSystem — provider конфигурации до Awake); в `Tools/e2e/editmode-known-red.json`.
- Рекомендация на будущее: тест InputDriven-курка (наблюдатель без флага не стреляет).

Материалы: tasks/bots-input/changelog/2026-10-20-sdk-89.md, -sdk-90.md.
