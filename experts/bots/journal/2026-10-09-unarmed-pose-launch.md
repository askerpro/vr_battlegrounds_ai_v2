# Безоружная поза MEF/Cyborg принята пользователем; запуск стенда перешёл на PlayLaunch

Пользователь 2026-10-09 принял безоружную позу MEF/Cyborg; хват и стрельба не приняты. Просмотр318
отклонён (скольжение, хваты Cyborg, support/наклон Rifle MEF; Pistol MEF нормален). Позже
пользователь отложил визуальную доводку хватов: приоритет — движение и реальная стрельба (дата
решения — unknown, не позже 2026-10-10). Это решения по невлитому коду bots-fix, не merge.

В тот же день `51c8c0478515f95cd1996b43755fcd7ba9f2ddef` (vr-test-stand, play-launch-integration,
принят пользователем) переписал `BotCombatStandEditor.cs` на PlayLaunch; launch adapter исключён из
writes bots-fix, clip-cutover ждёт вливания этой интеграции (уже merged). Bot T01 в той приёмке —
NeedsReview, выстрелы не заявлены.

Источники: `tasks/bots-fix/Readme.md` «Блокеры и следующие действия» (копия владельца);
`tasks/vr-test-stand/Details.md` (поиск «Bots-fix сообщил650», «Bot T01»); `git show 51c8c0478 --stat`.
