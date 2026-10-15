# DevTools: JSON-профиль и проверка совместимости automation-сцен

2026-10-15. Пользователь принял проверенный объём и поручил вливание.

Настройки BotCount проверяются независимым чтением checkout-local JSON вместо устаревшего
EditorPrefs; тест изолирует профиль и восстанавливает SessionState/environment. Release-тест
проверяет фактический список BuildSceneResolver. Явный запрос automation-сцены с компонентом
из Editor-only assembly отклоняется с именами сцены, assembly и script; обычные runtime-сцены
доступны по прежним правилам. BotCombatStand остаётся Editor/MPPM стендом, scene/asmdef не меняются.

В собственной worker lease727 получен ожидаемый RED: resolver5/4PASS/1FAIL. После finish добавлен
generic guard. Полный namespace DevTools731:129/129PASS,0failed/skipped, job
18b567b0dff84e33b368a83985eec52a. Cleanup и finish успешны,
R945281d39131b6e6455d2c2b5c11a4a19dc40944. Principal проверил full terminal и соответствие
четырёх source/meta blobs проверенному input16e8089f935d977058c387688ec62bbaaa74cbd5.

Полные результаты — reports/2026-10-15-devtools-approved/, вне Git; ignored reports в R
не captured. Это EditMode-проверка, не runtime/Quest и не эквивалент исторической выборки141.
Reader сборщика и ещё активная batch-матрица публикуются отдельно после своей проверки/приёмки.
