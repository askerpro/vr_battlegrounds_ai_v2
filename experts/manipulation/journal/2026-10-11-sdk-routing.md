# Вибрации UltimateXR идут через HapticService (SDK-патч 65, этап sdk-routing), принято в шлеме

`7f0d19986e96112149cbb5265f1a0ca29a873d60` в origin/dev (хаб: `haptics-system/sdk-routing` merged; база worker —
тикет 443). Zone-ack avatar-ik 3707 (4 условия), ревью диффа `Devices/` одобрено 3991.

- Решение пользователя 2026-10-11: клипы SDK без формы не глушить — запасные формы по типу в
  `Resources/SdkHapticFallbacks.asset`, чужие префабы не правятся (факт: формы не было ни у одного клипа SDK).
- Патч 65: `UxrHapticRouting` + перехват первой строкой в 4 методах `Devices/`. `SdkHapticRouter` принимает каждый
  запрос; чужой аватар — тишина; Raw/Rumble — продлеваемый голос со сроком; `Stop` — только голоса маршрутизатора;
  отдачу гасит `IWeaponRecoilHapticsOwner` (контракт `haptics-api`, интерфейс отдан weapon-system).
- Шлем (тикет 437): выстрел, клики, UI, гул готовности не обрывается — «вроде все ок». Тесты после приёмки
  (тикет 440): `SdkHapticRouterTests` 16, `SdkHapticFallbacksTests` 3, проверка 4 точек в `HapticOwnershipTests`;
  EditMode 44/44, AndroidCompileGate PASS.
- Урок: расширить `writes` работающего этапа нельзя — `stop` → `register` → `begin` (новый токен).

Источники: `tasks/haptics-system/changelog/2026-10-11-sdk-65.md`, `Docs/manipulation/architecture.md` §6.
