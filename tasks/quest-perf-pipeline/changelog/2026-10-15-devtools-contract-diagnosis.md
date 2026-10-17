# Два отказа DevTools связаны с устаревшими тестовыми контрактами

Источник: обращение performance7591, worker688, input11d9d62cc, basebb28046079,
job4d0be5f6a53a47e4965acbbb6c30b96a. Полный terminal Result:141/139PASS/2FAIL/0skip;
17 новых view-tour проверок PASS. Исторический общий verdict остаётся RED.

Проверены причины на HEAD5435b3488 и совпадение релевантных blobs с входом прогона:

- DebugBootstrapEditorOnlyTests.cs:68 читает machine-wide EditorPrefs, но setter BotCount
  после принятой миграции51c8c0478 пишет checkout-local JSON. Возврат dual-write нарушил бы
  изоляцию checkout. Будущая проверка должна использовать изолированный JSON-профиль и
  полностью восстанавливать профиль/SessionState, не общие пользовательские ключи.
- Проверка сцен берёт весь enabled EditorBuildSettings. Штатный GameBuilder использует
  BuildSceneResolver.Resolve(false), исключающий DebugMaps, в том числе BotCombatStand.
  Общая поблажка для DebugMaps не заменяет проверку фактических входов сборщика.
- При явном E2E RunForScenes("BotCombatStand") resolver всё же допускает сцену с компонентом
  из UNITY_EDITOR assembly. Это отдельная статически выявленная несовместимость контракта
  built fixture. Ответ bots7663 подтвердил границу Editor/MPPM-only: built route должен
  явно отказать с причиной player-incompatible fixture. Новый runtime fixture сейчас не требуется;
  launch-infra владеет admission/resolver, bots — составом стенда и assembly boundary.

Новые Unity-проверки, сборки, изменения тестов/кода/сцен не выполнялись. Подробные отчёты,
SHA/blobs и ограничения: reports/2026-10-15-devtools-688/prefs.md и editor-only.md, вне Git.
Исходный test-job-1.json performance имеет SHA256
15c3e82b96bd874b425a721956c94aab9b2e315fe1f8143eb48418bb0ed46e07.

Следующая ограниченная правка launch-infra: проверка JSON roundtrip в изолированном профиле,
проверка фактического release input и общий отказ для несовместимых explicitly requested
automation fixtures. Решение7663 — граница владельца bots, не contract-ack отсутствующей
ревизии и не подтверждение нового PASS. Изменение ещё не реализовано.

Последующее прямое решение пользователя15.10 сняло ожидание batch admission/reservation7514:
проверка процессов/логов/lock перед build, при занятости повтор по таймеру20–30мин, без брокера.
Первая Test cold запущена на accepted74bd42f57; результат ещё не получен.
DevTools-правки согласованы; технический RED snapshot1dcf2bdeeaf, заявка721 QUEUED.
Четыре существующих checkout повторно не создаются.

Продолжение14:05MSK: 721 завершена сопровождающим после compile refusal; 727 дала точный
RED5/4PASS/1FAIL, затем штатный finish/Rabd17d7f2a63. После release реализован generic
Editor/Player assembly guard, static review clean. Полный GREEN731/input16e8089f935d
исполняет только devtools_scene_guard; source HEAD47c0102778d, результат ещё ожидается.

Test cold завершён: оба GameBuilder PASS, UXR748f8ac1c2e18298 одинаковый, pipeline exit1
из-за неверного Read-UxrIdHash. Минимальный consumer fix: canonical raw firstline16hex,
invalid/missing fail closed. Реальный producer artifact проверен; RED15/18 → GREEN18/18,
principal18/18. Writer/manifest не менялись. Полные native логи/exit/артефактhash/diff сохранены.
После terminal/free/no-lock восстановлены только собственные generated sideeffects к74bd,
Library сохранены. Test repeat14:01MSK/exec12203 на том же SHA; caller hash отдельно в startJSON.
Новые кодовые изменения ещё не приняты пользователем; pipeline PASS и весь DevTools GREEN
до actual terminal результата не заявляются. Дальше Prod cold → Prod repeat.

14:12MSK: полный DevTools731 terminal подтверждён principal:129/129PASS,0failed/skipped,
129leaves/outside namespace0; job18b567b0dff84e33b368a83985eec52a. Cleanup/finish успешны,
R945281d39131b6e6455d2c2b5c11a4a19dc40944. Current129 не выдаются за ту же выборку,
что исторические141. Все четыре owned source/meta blobs совпадают с проверенным input16e8089.
User acceptance ещё pending; complete raw result сохранён в own reports вне Git.
Gameplay8213 подтвердил scoped generated cleanup после terminal/free/no-lock; source callback
и каталог остаются их зоной и отдельным кандидатом catalog-build-derived-state.
