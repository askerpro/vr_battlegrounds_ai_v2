# Мультипроцессный стенд разблокирован для ботов: отказ при preview-сценах и исправление гонки descriptor

Принято пользователем 2026-10-13 (этап `vr-test-stand/stand-preview-preflight`, план rev 36).

- Корень ошибок sceneId (bots 5080): preview-сцены префабов оружия в долгоживущем worker-редакторе. На чистом
  редакторе S2C TestMap2 — 0 ошибок. Решение: отказ `PreviewSceneNetworkObjects` в PlayLaunch и обычном Play;
  рестарт/детект между арендами — сопровождающий; патч Mirror — кандидат network.
- Попутно найдена гонка `Temp/VRBattlegroundsTestStand/active.json` (File.Replace против чтения клонов): зависание
  в Stopping и выход клона из прогона. Вероятная причина плавающих LaunchNotReady у bots (5185). Исправлено в
  `StandManifest`/`PlayModeTestStand`; перехват гонки на сбое не воспроизведён — только отсутствие регрессии.
- Архив логов участников по RunId; архив `main` на worker неполный из-за общего Editor.log (infra_issue 5403).
- Открыто: `stand-back-to-back` (старт клонов MPPM вплотную за прошлым прогоном не укладывается в 90 с).

Evidence: `tasks/vr-test-stand/reports/stand-preview-preflight-533/`, `.../stand-manifest-race-539/` (вне Git).
