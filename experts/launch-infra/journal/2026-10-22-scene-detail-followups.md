# scene-detail-followups: хвосты ревью launch-infra scene-detail, 2026-10-22

Run-QuestE2E: KEYCODE_SLEEP — в конце finally после logcat/pull/diagnostic stage (Wi-Fi adb во сне надёжен ~5 мин);
readlink-проба storage — первой внутри try (отказ идёт через finally, run.json пишется). Build-Game: `gpuSceneIdentity`
в pair manifest и записях профилей (recovery берёт значение из исходного pair). Tools/e2e/.gitattributes удалён
(корневой покрывает). Docs/testing.md: путь `<storage>/Android/data/<pkg>/files` вместо /sdcard.
Rev3 gpu-launch-inputs (discoveryOnly, not-applicable ffr/cpuLevel/gpuLevel на Windows) — в contracts задачи, владельцу
quest-perf-pipeline отправлен перенос. Проверено: парсер PowerShell, Tools/release тесты; на шлеме не запускалось.
Риск: сон последним в finally не выполнится при необработанном исключении выше в finally.
