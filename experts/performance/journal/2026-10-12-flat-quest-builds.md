# Quest-сборки GameBuilder с 2026-10-11 плоские — их перф-цифры недействительны

2026-10-12. Блокер launch-infra (5050): Quest/Test-сборки `GameBuilder` (в том числе пары `StartPairBuild`) выходят
без `com.oculus.intent.category.VR` — на шлеме чёрное 2D-окно, хотя OpenXR-сессия поднимается; device-smoke был
формально GREEN.

- Подтверждено независимо по APK пары perf-stress-scenario (ревизия d51aa3a9ecf7, 2026-10-12 04:23, `aapt dump
  xmltree`): нет VR-категории и `com.oculus.supportedDevices`. `OculusSettings`: TargetQuest2 1, TargetQuest3 0
  (backlog `quest3-target`).
- Любые перф-числа Quest со сборок GameBuilder с 2026-10-11 до исправления — недействительны. Прежние замеры
  (2026-09-28/29, ручная сборка) это не затрагивает, но режим совместимости Quest 2 для них не исключён.
- Этап `perf-stress-scenario/scenario`: прогон на шлеме — после исправления сборки и пересборки пары.
