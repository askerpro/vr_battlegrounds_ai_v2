# Поздний клиент получает жизнь и смерть в снимке UXR (T-34, частично: ярус C не прогнан)

`cfbbcb27439eeba2295817c8fc4469130f2eeb54` (T-34): патч SDK 29 — жизнь `UxrActor` входит в снимок состояния; стражи класса
`StateSnapshotCoverageTests` (до патча 3/3 красные) и `RpcCarriesNoStateTests` (реестр RPC с причинами);
ворота Android PASS.

Не сделано: сценарий яруса C `late-join-state-parity` — применение снимка у настоящего второго процесса и гонка
`Life = 0` раньше снимка подтверждены только чтением кода. Статус T-34 в `Docs/tasks/README.md` — 🔧.

Источники: `Docs/tasks/T-34-late-join-state-snapshot.md` «Сделано (2026-09-29)», «Открытые вопросы»;
`Docs/UltimateXR/known-issues.md` Issue 25.
