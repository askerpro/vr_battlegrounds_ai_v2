# haptics-system — единая система вибрации

Обновлено 2026-10-09. Владелец: Claude Code `vr-battlegrounds-ai-f4`, worktree `F:/CodexWorktrees/haptics/Vr_Battlegrounds_ai`,
ветка `haptics`, снимок для проверки — `haptics-review`.

## Цель и мотивация

У вибромоторов контроллеров один владелец — `HapticService`; остальной код просит сыграть клип `UxrHapticClip` с общей формой.
Классы с приоритетом, вибрация только на руке локального игрока. Раньше писателей мотора было 15, а на Quest каждый
вызов обрывает текущую вибрацию: слабое гасило сильное, отказ не отличался от отдачи, карман «не чувствовался».

## Статус

| Этап | Состояние |
|---|---|
| `clips-interaction` — клип с формой (SDK-патч 66), роли, `InteractionHaptics`, окно «Вибрация» | принят пользователем 2026-10-09 (worker 226, шлем 228); вливание |
| `pocket-removal` — снять `PocketHaptics` с аватаров, `AvatarLoadoutTests` | после влития первого этапа; порядок — с bots-fix, hand-rig-quality, legs-ik |
| `sdk-routing` — перехват вибрации SDK (патч 65), форма в компонентах SDK | не начат |
| `item-clicks` — клики хвата 6 префабов на общий клип | после `sdk-routing`, по согласованию с WeaponSystem |
| `sources` — гибель, стена, часы, жест отладки | не начат |
| `behavior-tests`, `docs-transfer` | после приёмки |

## Архитектура

**Утверждено 2026-10-08:** клип SDK `UxrHapticClip` (патч 66) с общей формой `UxrHapticWaveform`, роли `HapticRoles`, общая
`InteractionHaptics` вместо `PocketHaptics` — [Details.md, п. 0](Details.md).

- `HapticService.Play/Begin(UxrHapticClip)` → `HapticMixer`: слышен высший приоритет, `Normal` (физика) — максимум,
  непрерывный `Low` под высшим на паузе. Мотор пишет только `UnityXRHapticDevice`; сторож — `HapticOwnershipTests`.
- Настройка проекта, у игрока настроек нет; подбор — окно «Вибрация» и drawer клипа в Play, правка действует сразу.
- Вибрации SDK перехватит SDK-патч 65; отдача SDK гасится по стволу через `IWeaponRecoilHapticsOwner`.

Подробно — [Details.md](Details.md); постановка — [task.md](task.md); исследование — [research.md](research.md).

## Синхронизация

- WeaponSystem — контракт [haptics-api](contracts/haptics-api.md) revision 1 в хабе (клип вместо Id) согласован 2026-10-09.
- SDK-патчи UltimateXR: **65** — перехват (в постановке 57), **66** — форма в клипе ([запись](changelog/2026-10-08-sdk-66.md)).
- Предложение правила «один владелец мотора» и перенос в Docs — сопровождающему правил и владельцам областей (Details, п. 12).

## Проверка

Факты — [clips-interaction](changelog/2026-10-09-clips-interaction.md), [SDK-патч 66](changelog/2026-10-08-sdk-66.md); история — [первый срез](changelog/2026-10-08-service-pocket-tuning.md).

## Следующий шаг

Влить `clips-interaction`, сообщить SHA сопровождающему (publish-base) и weapon-system (патч 66). Затем согласовать
архитектуру следующего этапа: `GrabberReadiness` (состояние руки — единственный источник «может взять/положить») и пакет
отклика `ManipulationFeedback` (вибрация, звук, объект, событие; порядок «предмет → якорь → роль якоря → общая роль»).
