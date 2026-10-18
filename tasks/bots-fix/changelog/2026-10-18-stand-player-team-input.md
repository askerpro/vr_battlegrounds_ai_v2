# Адресный выбор команды игроком для bots-fix

2026-10-18. Этап clip-cutover, планr64, accepted=false. Необходимый шаг проверки реального Host и обычного внешнего клиента в RH, в рамках порученного завершения bots-fix.

Фактический799 H8 показал Live/round0/Setup:8ботов уже распределены по командам, но человек Host остаётсяteam0. Это штатный PlayerChoice/AllPlayersHaveModeTeam, не дефект AI. AutoGoLive не выбирает команду за человека. Старый прогон сохранён finaltrue/completedfalse/0rounds/0shots/0hits и не используется для Combat/perf.

Через существующий StandRequest добавлено typedдействие request-team-change, NewTeamId/NewAvatarId. Новые поля входят в idempotency fingerprint; адрес RunId/ParticipantId/ProcessSessionId, положительный epoch и ненулевой exactavatar обязательны. Исполнитель требует managedrunningPlay, подключённого готового локального ownedPlayer, совпадающих текущего connection/session/avatar; DedicatedServer и Spectator не выполняют ввод. Вызывается только существующий клиентский CmdRequestTeamChange, как у планшета. Ответ team-change-requested означает отправку запроса, а не принятие сервером. Политику команды/скина выполняют прежние серверные правила.

Изменены только2существующихEditorsource: StandProtocol.cs и PlayModeStandParticipant.cs. Прямых писателейTeamIndex/Ready/Transform, forcedready, fakeHMD, нового сетевого протокола игры, второго lifecycle или запуска E2E нет. Сетевой boundary и sourceдифф переданы read-onlyreview; компиляция и Unityещёпредстоят. Тесты/ожидания до пользовательской оценки качества не добавляются. Подготовлены rawonce-onlyHostвводы со штатнымCmd; RH будет адресоваться своемуclient-onlyпроцессу через новое действие.

Common36 должен включать обаEditorsource как общие H/C overrides: иначе RH и Host использовали бы разные варианты стенда. Frozen87 сохраняет прежние77/COMMON26/CUTOVER48/probe2/TestsB/tools12; две новые общие source не меняют тестовые или gameplayожидания. Артефактыcommon34 и всех неуспешных попыток сохраняются.
