# Контракт haptics-api: вибрация для WeaponSystem

Владелец: haptics-system. Потребитель: weapon-system (этап D и далее). **Ревизия 2** (2026-10-08, предложена; ревизия 1 —
согласована сообщениями 2026-10-07). Причина пересмотра — решение пользователя: клип SDK `UxrHapticClip` с общей формой
вместо закрытого перечня сигналов ([Details.md](../Details.md), п. 0). Обоснование контракта — Details, п. 4.2.

## Предоставляет haptics-system

- Клип — `UltimateXR.Haptics.UxrHapticClip` (SDK-патч 66): ссылка на ассет-форму `UxrHapticWaveform` (общий реестр
  `Assets/Data/Haptics/Waveforms/`: `RecoilPistol`, `RecoilRifle`, `RecoilShotgun`, `DoublePulse`, `TriplePulse`, `Click`…) и
  параметры точки интеграции: сила, приоритет (`Low/Normal/High/Critical`), пауза повтора, кулдаун, вторая рука.
- `VrBattlegrounds.Haptics.HapticService.Play(UxrHapticClip clip, UxrGrabber hand, HapticHandRole role = Primary, float gain = 1)`.
  Рука чужого аватара, бота или null — no-op; клип без формы — no-op. `Secondary` умножает силу на `SecondaryHandGain` клипа.
- Интерфейс игрового кода `IWeaponRecoilHapticsOwner { bool OwnsRecoilHaptics { get; } }` и гашение отдачи SDK для ствола,
  чей хост объявил `true` (этап `sdk-routing`). Пока `false` или интерфейса нет — отдача SDK звучит как раньше.

## Обязуется weapon-system

- Клипы отказа и отдачи — поля `UxrHapticClip` в данных WeaponSystem (профиль/набор откликов ствола): наш drawer рисует форму,
  силу, приоритет и пробу в Play. Отказы — приоритет `High` (форма `DoublePulse`, сбой — `TriplePulse`), отдача и ход
  механизма — `Normal`.
- `WeaponFeedbackExecutor` зовёт только `HapticService.Play(clip, руку)`; прямые вызовы мотора запрещены (`HapticOwnershipTests`).
- Рука: отказы — рука на спуске; ход механизма — рука на ручке; отдача — рука на спуске (`Primary`) и вторая рука на том же
  оружии (`Secondary`). Вызов только у автора и не при replay.
- `OwnsRecoilHaptics` постоянен для экземпляра ствола (из данных), во время игры не переключается.
- Удаляя или переводя `AutomaticWeaponSlideFeedback`, `WeaponAttemptFeedback`, `WeaponChamberingReminder`,
  `BarrelObstruction`, убрать их строку из `HapticOwnershipTests.Pending`.

## Изменения против ревизии 1

- `HapticSignalId` и каталог `HapticCatalog` удалены: вместо Id — `UxrHapticClip` в данных ствола.
- Сигнатура `Play(clip, hand, role, gain)` вместо `Play(id, hand, gain, role)`.

## Открыто

- Где именно в данных ствола лежат клипы — решает weapon-system на этапе D.
