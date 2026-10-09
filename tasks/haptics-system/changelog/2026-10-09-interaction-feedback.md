# Отклик взаимодействий: префаб-GO, конфиг по умолчанию, один исполнитель

Дата: 2026-10-09. Задача: haptics-system. Этап: interaction-feedback. Тип: feature.
Статус: принято пользователем в шлеме 2026-10-09 (карманы, пол, арсенал, гнездо магазина); вторая рука — по его решению вдвое тише.

## Результат

- `HapticPlayer` — проигрыватель клипа на GO отклика; `InteractionFeedbackConfig` (`Resources`) — отклик по умолчанию;
  `InteractionFeedbackOverride` — свой отклик якоря/предмета; `NetworkedFeedback` — слот для событий чужого игрока;
  `InteractionFeedback` — единственный исполнитель (кандидат хвата SDK-патча 67 + кандидат якоря SDK, пул экземпляров).
- Префаб `Assets/Prefabs/Feedback/Interaction/Feedback_GrabReady.prefab` (Steady, 0.08, Low) — на все готовности:
  карманы, гнёзда, слоты арсенала, свободные предметы.
- Удалены `HapticRoles`, `HapticOverride`, `InteractionHaptics`, `AnchorReadiness`, `Resources/HapticRoles.asset`.
- Окно «Вибрация» — конфиг и клипы префабов отклика; автозапись конфига при выходе из Play.
- Вторая рука к удерживаемому предмету или его части — роль `Secondary` (`HapticPlayer.Bind(side, role)`,
  `HapticService.Begin/Play(side)` с ролью), гул вдвое тише (`SecondaryHandGain` 0.5 у `Feedback_GrabReady`).
- Тесты: конфиг ссылается только на префабы отклика, их `HapticPlayer` — на формы проекта; покрытие — без исключений;
  `InteractionFeedbackTests` — распознавание второй руки и «вторая рука тише первой».
- Генератор: `tasks/haptics-system/tools/gen_interaction_feedback.py`.

## Проверка

Worker, тикет 247 (база d009709c): компиляция, `AndroidCompileGate` PASS, EditMode `VrBattlegrounds.Tests.Haptics` 20/20.
Покрытие: все якоря и предметы игры с откликом готовности, исключений нет. В шлеме — не проверено.
