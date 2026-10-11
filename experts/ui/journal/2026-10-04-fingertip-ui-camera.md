# Касание планшета: лучи UxrFingerTip (патч 40) и камера UI после спавна бота (патч 41)

Коммит `ff21402b4721317cef2abffe762a2e06aab868ef` (2026-10-04), уточнение доков `17d62fe0f` (2026-10-03).
Причина отказа касаний: `UxrManager.Avatar_Enabled()` назначал всем `UxrCanvas` камеру временно
Local-аватара бота/чужого игрока; после перевода в `UpdateExternally` канвас оставался с выключенной
чужой камерой. Патч 41: каноническая `UxrAvatar.LocalAvatarCamera`, привязка обновляется до
SDK-ввода. Патч 40: opt-in `UxrFingerTip.RayVisualizationEnabled`, дальность общая с вводом
(`FingerTipMinHoverDistance`, планшет 5 см); диагностика `DebugFingerTipRays`/`DebugFingerTipSurface`
из раздела «Отладка».

Проверено (по источникам): причина подтверждена снимком живого Play Mode; 84/84 изолированных
preview-сценария, preview-регрессия камеры 13/13 PASS, AndroidCompileGate PASS. Окружение — unknown.
Не проверено: жесты и касание в шлеме (отдельный этап приёмки). Патч 41 затрагивает ядро
`UxrManager` — зона `manipulation`.

Источники: `Docs/UltimateXR/known-issues.md` «Визуализация касания пальцем и лазер из руки», «После
спавна бота планшет перестаёт принимать касания»; `Docs/UltimateXR/sdk-patches.md` «Патч 40», «Патч 41»;
`Docs/ui-menu-architecture.md` «Касание пальцем и диагностика попадания»; `git show ff21402b4`.
