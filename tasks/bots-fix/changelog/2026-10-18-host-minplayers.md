# Host и явный порог автостарта — 2026-10-18

Unity793/794 на491bf8ec обнаружили базовые дефекты, необходимые для дальнейшего
проверяемого сравнения ботов. Источники/полные журналы — reports/native793/analysis.md
и reports/native794/analysis.md. Оба запуска штатно finish/receive, paths0.

- Mirror SDK6 распознаёт фактически завершённую загрузку networkScene локального Host
  в существующем решении authentication; Ready и connect-message остаются штатными.
- HeadlessPrecacheGuard учитывает pendingHost mode до ConnectHost и сохраняет исходный
  UsePrecaching. SDK81 ждёт настоящего LocalAvatar; принудительного включения прогрева нет.
- DebugOrchestrator.TryGoLive применяет положительный MinPlayersOverride независимо
  от режима меню. Fallback ModeData/default1 действует только без явного override.
  Role.Player count и единственный путь GoLive сохранены; MapReferee не изменён.
- Пакет H/C содержит common30/frozen81, cutover48 и pinned probe2fa. Compiler готов
  собирать свежий Mirror до прежних11 сборок; старые common29 результаты исторические.

На момент записи новые исходники ещё не прошли compilation/SHA-review/Unity runtime.
Новых тестов/ожиданий не добавлено, пользовательская оценка качества открыта.
Ручная проверка после сборки: Host с собственными session/avatar и прогревом;
обычный remote Player rejoin; MinPlayers9 при8Player+2Spectator удерживает автостарт,
а MinPlayers8 при том же составе позволяет существующий GoLive. Проверить последующую
смену карты, cleanup и профили; снимки/журналы не заменяют Quest/визуальную приёмку.
