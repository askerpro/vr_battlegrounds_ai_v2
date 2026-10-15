# Принятая публикация текущего SceneDetail

Пользователь15Oct ответил на запрос Unity/Quest-проверки: «вливай как есть - согласовав с UI expert
расположение в меню». Команда разрешает accepted commit/push текущего состояния без ожидания
дополнительной ручной проверки. Границы доказательств сохраняются: compile/authoring/static review
и PC-host startup/cleanup есть; изменение controls, JSON roundtrip, Quest и GPU7ms не проверены.

С UI согласуется прежнее размещение «Перф-анализ → GPU», один Build/Snapshot вместо legacy-блока.
MenuDebug остаётся UI-owned. Canonical запрос7979 плюс штатное уведомление по прямому поручению пользователя.

Чистый candidate собирается поверх свежего origin/dev из154 проверенных product paths и материала
tasks/scene-detail. Старые committed изменения experts/level-design/backlog и tasks/lobby-decoration
не публикуются этой задачей. Основной HEAD/index и чужие вложенные checkout сохраняются.
Helper использует отдельный индекс и фиксирует состав/parent/tree/commit в reports/accepted-release/.
