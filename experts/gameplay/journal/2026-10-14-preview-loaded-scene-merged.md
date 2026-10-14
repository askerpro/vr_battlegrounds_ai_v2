# Preflight открытой карты принят и влит; тесты принятых механик продолжаются

Пользователь принял `catalog-fingerprint/preview-loaded-scene` 2026-10-14: «Принимаю, влить».
Fast-forward `origin/dev`: 97a320c88469cb2a74d89adb0a6b6edd23536753, хаб stage_merged6800;
worker-база опубликована639 DONE / 6808. Unity625: preflight3/3 PASS, Windows E2E build с открытой Lobby PASS,
setup/dirty/sceneId сохранены. Сырой composite false из-за transient InstanceID сохранён и объяснён в Details.
Сцена/каталог не менялись; побочный PC.asset не получался. Зависимые владельцы уведомлены через inbox.

Тесты после пользовательской приёмки: teardown8 и preflight7 подготовлены; compile-only проверки не считаются
NUnit. run_tests в аренде640 потерял MCP-ответ; job_id не получен, исход восстанавливается через SessionState.
Finish640 DONE, получен только Unity-generated .meta. Infra_issue6863 передан сопровождающему.

launch-infra сообщил6892: map-run-startup-route на87643327108f с принятой разборкой —14/14 strict GREEN,
полные логи, unexpected/defects/native/dropped=0. Снятие known-noise ещё не принято/не влито у владельца.
Декор арсенала остаётся отложенным; series-smoke ждёт вливания e2e-smoke-kit.

Карта эксперта сверена от716743336 до принятого97a320c88 по шести изменившимся owned исходникам:
preflight, composer, bootstrap, PlayersManager, AvatarManager, AvatarTeardown. Владельцы и перечисленные
ответственности не изменились; AGENT.md соответствует текущим источникам. Обновлён reviewed_sha/date.
