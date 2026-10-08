# Клип с формой, роли и общая вибрация взаимодействий

Дата: 2026-10-09. Задача: haptics-system. Этап: clips-interaction. Тип: feature + sdk (патч 66).
Статус: проверено на worker и в шлеме, принято пользователем 2026-10-09.

## Результат

- Клип SDK `UxrHapticClip` с формой `UxrHapticWaveform` — единственный тип точки интеграции ([SDK-патч 66](2026-10-08-sdk-66.md)).
  `HapticService.Play/Begin(UxrHapticClip)`; прежние каталог, `HapticSignalId` и экран-инспектор каталога удалены.
- Формы — `Assets/Data/Haptics/Waveforms/` (Steady, Click, DoublePulse, TriplePulse, RecoilPistol/Rifle/Shotgun, Death).
- `Resources/HapticRoles.asset`: готовность карманов своего аватара — Steady, сила 0.08 (подобрана пользователем), Low;
  остальные роли пустые. `HapticOverride` — свои клипы якоря или предмета.
- `InteractionHaptics` — общая система вместо `PocketHaptics`: готовность любого якоря (`AnchorReadiness` вместо
  `PocketReadiness`) и хват/укладка/отпускание своей рукой. `PocketHaptics` — пустая оболочка до этапа `pocket-removal`.
- Редактор: drawer `UxrHapticClip` во всех инспекторах (форма, ползунки, картинка, проба), инспектор формы, окно
  `Tools/VR Battlegrounds/Haptics/Вибрация`, запись форм и ролей при выходе из Play.
- Сторож `HapticOwnershipTests`: список исключений ведёт haptics-system (контракт haptics-api), добавлен временный адаптер
  weapon-system `WeaponHapticOutput.cs`; устаревание проверяется только для строк haptics-system. `HapticWaveformTests` —
  формы и роли.
- Контракт `haptics-api` revision 1 согласован в хабе (ACK weapon-system и haptics-system).

## Проверка

- Worker, билет 226, база 048b9206: AndroidCompileGate PASS; EditMode Haptics + AvatarLoadoutTests + MenuDesignRules +
  MenuWiring — 93/94; единственный отказ `Телепорт_попадает_в_пол_карт(Optimized_MEF_Player)` — повреждённый
  `LightingData` TestMap2, вне этапа.
- Шлем, worker билет 228 (Play через Quest Link): вибрация готовности карманов (взять/положить) подтверждена пользователем.
- Найдено и вынесено в следующий этап: вибрации «рука может взять предмет» для свободного предмета пока нет.
