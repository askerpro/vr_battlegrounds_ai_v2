# Дизайн-система и каркас планшета (T-32) с экраном «Обзор» (T-33) влиты в dev

Коммит `cbf8453b80ca2c49b4d907ffb8de5cbd709876f3` (2026-09-29). Решения пользователя 2026-09-29:
разделы в колонке (хаб-экран убран), скролл стиком с выключением телепорта руки, акцент `#FFAF00`.
Итог: каркас один на планшет (`MenuFrame` в `Tablet_Base`), экраны — варианты `Screen_Base` на `MenuKit`
и токенах `MenuTheme`; стек `MenuNavigation`; разделы объявляет режим (`GameModeData.menuTabs`);
админ-UI — одно правило `MenuPermissions`; 9 экранов переписаны, превью на образцовых данных и снимки
PNG; генерация — `MenuKitBuilder`; SlimUI-префабы меню удалены; скил `/add-menu-screen`.

Проверено (по источникам): харнесс тестов красный до правки (зафиксирован в T-32); тесты
`MenuDesignRulesTests`, `MenuContainmentTests`, `MenuKitLogicTests`, `MenuWiringTests`, `MenuTabsTests`,
`MenuPermissionsTests`, `OverviewBuilderTests`, `OverviewStateReaderTests` добавлены; числа итогового
прогона и окружение — unknown. В шлеме проверен скролл пальцем; стик после правки и видимость «Админ» —
нет. Формальная приёмка пользователем всей итерации — unknown.

Осталось: только шлем (скролл лучом/стиком, читаемость, удобство колонки), снимки «Обзора» по
контекстам — см. backlog `menu-headset-acceptance`.

Источники: `Docs/tasks/T-32-menu-design-system.md` «Решения пользователя», «Сделано»;
`Docs/tasks/T-33-tablet-overview-screen.md` «Что сделано», «Что осталось»; `git show cbf8453b8`.
