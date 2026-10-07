# Реализация T-50 этапа 4 и SDK-патчей 54–55

> Реализация в текущем чате по прямому поручению пользователя. Контракт:
> [design](T-50-stage4-design.md). Проверенные этапы 0–3 уже в origin/dev `424bf5f4`.
> Используется superpowers:executing-plans; принятый коммит нового среза до проверки в Unity не создаётся.

## 1. Устройства трекинга

- [x] В `UxrControllerTracking.cs`: instance `HeightOffset`; чтение сенсоров из него, глобальную статику убрать.
- [x] `AvatarCalibrationApplier` ставит offset конкретным устройствам своей связанной модели, включая disabled.
- [x] Минимально адаптировать старый харнесс чтения рук к экземпляру устройства; ожидания пола не менять.
- [ ] Проверить компиляцию и изоляцию двух устройств/старого и нового аватара технической пробой.
- [x] Задокументировать SDK-патч 54 и резерв в coordination до коммита.

## 2. Один snapshot placement

- [x] `PlayerPlacement` value type (None/World/Anchors, Position/Rotation/CapturedOnMap), TryNormalize/Capture/Resolve.
- [x] `PlayerCalibration.Placement`, WithPlacement; общая нормализация целого снимка и equality/hash.
- [x] `PlayerSession.ServerCapturePlacement` и snapshot-only origin: менять только placement, один writer.
- [x] Фасад `SpawnPlaceRegistry` без Dictionary; старые API читают/записывают через владельца.
- [x] `GamePlayerConnectMessage` и `SessionSnapshot` несут placement внутри calibration, без mutable дубликатов.
- [x] `PlayersManager.InitialCalibration` объединяет сохранённое сервером место с явными данными клиента;
      клиентский world не принимается; server World той же карты сохраняется для живого либо
      откалиброванного погибшего игрока, anchor-pose — по флагу калибровки.
- [x] `AvatarManager` разрешает одну запись до Instantiate; отдельный override мирового snapshot убрать.

## 3. Процедура, применение и наблюдение

- [x] PSM передаёт целевую anchor-root-pose одним Submit; собственные saved-place поля и direct teleport убрать.
- [x] Применитель принимает явный apply-placement для нового замера/ответа; захват не переносит аватар.
- [x] SDK55 root-target вместо camera-floor replay; camera-local pose сохраняется, один StateEventAuthority author.
- [x] Предсказание/отказ не публикуют SDK; latest accepted ack испускает один перенос корня.
- [x] `PlayerPlacementTracker`: только bound avatar, server capture + local cache, подавление feedback при apply.
- [x] Жизненные циклы: смена карты/тела/отключение используют одну запись; принятая anchor-pose защищена от NT lag.
- [x] Для временно отсутствующих якорей — явный same-map world fallback, без применения старого мира на другой карте.

## 4. Верификация и передача

- [x] Адаптировать только API принятых тестов/сценариев; перечислить изменённые ожидания до новой игровой приёмки.
- [x] AndroidCompileGate + узкие принятые группы + технические пробы через checkpoint/request/guard/finish/receive.
- [x] Независимое read-only ревью полного diff (gpt-6.1-sol, high), устранить значимые замечания.
- [x] Полный EditMode; базовые отказы не подавлять, результат сохранять в `Docs/tasks/report/T-50-stage4/`.
- [x] Docs: README, session-architecture, game-manager, troubleshooting, CHANGELOG, sdk-patches, T-50.
- [x] Пользовательская проверка в Unity/шлеме; после неё закрепить принятые новые игровые ожидания тестами.

## Review focus

1. SDK55 синхронизирует root target: camera-local translation/yaw остаются прежними при задержке другой камеры.
2. SyncVar-hook/ack и host: snapshot-only захват не создаёт перенос или второе сетевое SDK-событие.
3. Старый LocalAvatar ещё жив, delayed netId и body replacement: работать по связи сессии.
4. Missing anchors, rotated map, process restart, dead/alive snapshot: соблюсти frame и здоровье.
5. Late device activation и два аватара: offset не переносится в чужое устройство и не остаётся глобальным.

## Журнал

- 2026-10-07: прямое поручение пользователя на этап 4 + instance tracking offset; контракт отражён в design.
- Self-review: one owner snapshot, projection-only legacy API, explicit measurement vs archive application;
  отдельного transport/владельца/геометрической миграции нет. Игровые тесты до приёмки не расширяются.
- Ownership RED на базе 424bf5f4, ticket94: все 7 технических признаков отказали; отчёт ownership-red.json.
- Независимое ревью 9ade60b выявило API compile mismatch, prediction-before-acceptance, camera-lag replay,
  приоритет anchored live snapshot и dead calibrated world fallback. Исправления включены в 0e8687c;
  SDK55 зарезервирован до изменения. Старую заявку103 отменили до begin; проверочная заявка107 в очереди.
- Review 0e8687ca: все прежние замечания закрыты, новых Critical/Important нет; source approved, live verification pending.
- Временные пробы подготовлены: ownership (7), value/frame + Mirror + SDK binary replay с задержкой камеры,
  preview/rejection/accepted ack/duplicate ack/archive/old-body. Постоянных новых игровых ожиданий нет.
- Ticket107: begin отказал на CS0103 (SpawnPlaceRegistry, отсутствующий using Core); finish потребовал recovery.
  Recovery вернул 424bf5f4, очередь разблокирована. compile-red.json сохранён; импорт исправлен в 5524d073.
  Результат recovery 19241f22 содержит только две ожидаемые .meta, unexpected=[]; штатный receive
  отклонён из-за аварийного статуса. Каждая .meta просмотрена через git show и перенесена нативно
  с исходным Unity GUID, без регенерации. Повторная заявка113, технические пробы ещё не выполнены.
- Ticket113 / 5524d073: Android PASS, ownership 7 GREEN / 0. Старые группы 46/47:
  SessionRecoveryTests сравнивал прежний PlayerCalibration без нового Placement с целым новым snapshot.
  API-адаптация сохраняет ожидания пола/роста/признака через WithPlacement(None); ожидания pose не добавлены.
  Дополнительные runtime-пробы не дошли до логики: SceneManager.CreateScene запрещён в EditMode.
  Стенд переведён на EditorSceneManager.NewScene + имя временной сцены, callbacks tracker включаются явно.
- Finish113 получил ошибку artifact path: отчёт находится в worktree агента, а CLI ищет его в worker.
  Recovery завершил возврат базы, result55982de accepted=true, paths=[], receive завершён.
  В следующих finish локальные отчёты не передаются как worker artifacts.
- Origin/dev сдвинулся на 8507d7d6 (принятый arsenal 2c), ветка обновлена.
  Единственный autostash-конфликт Docs/CHANGELOG.md склеен с обеими записями; код конфликтов не имел.
  Не использовать промежуточный 78b1f7f с маркерами документа: исправленный checkpoint113c7788, заявка117.
- Заявки117/последующая попытка отклонены при смене опубликованной базы; принятая база21f9e239
  опубликована сопровождающим, повтор publish вернул previous_base=тот же21f9e239 (ticket120).
  Ticket121 безопасно завершён без MCP по просьбе пользователя ждать агента брокера, paths=[].
- После возобновления: база worker06624cb9, ветка обновлена без конфликтов на принятую историю.
  Ticket127 / aad33e19: компиляция и текущая консоль чистые; узкий набор47/47 GREEN.
  Дополнительные пробы отказали до своей логики: additive NewScene несовместим с untitled парковкой.
  Temporary harness переведён в Single и возвращает чистую пустую сцену в finally.
- Полный native XML ticket127 завершён: 2100, passed1953, failed147, skipped0.
  MCP get_test_job не переподключился; native XML сохранён до возврата worker.
  Recovery127 завершён, paths=[], unexpected=[]; runtime-файлы после прогона не менялись.
- Сравнение full-comparison.json: тот же набор147 отказов, новых/исчезнувших против предыдущего полного
  прогона нет; у146 сообщения совпадают. LobbyLayout: Grenade y0,00 → y0,01 (физический шум того же отказа).
  Против контрольной базы146: единственный дополнительный DogTagSetup на стороннем LightingData, как раньше.
- Оставшийся короткий пакет: SDK/Mirror replay и preview/rollback/ack + финальный Android,
  input ea50635e, заявка135. Assets между aad33e19 и ea50635e идентичны.
- Сопровождающий опубликовал базу c0a53921 (ticket138). Заявка135 отменена владельцем;
  rebase с autostash сохранил код, единственный конфликт changelog объединён без удаления чужой записи.
  Новый input a3d5e936, заявка139; из-за изменений запуска карт повторяются связанные проверки интеграции.
- Ticket139 / a3d5e936 на базе c0a53921: текущая консоль без ошибок, AndroidCompileGate PASS,
  связанные тесты47/47 GREEN, техническая проба24/24 GREEN. Проверены два устройства (включая выключенное),
  Mirror value roundtrip, повёрнутые/сдвинутые якоря, same-map world fallback, приоритет серверного снимка
  и binary SDK55 replay при запаздывающей camera-local pose. Старый MoveAvatarTo дал drift0,2828429м;
  новый root-target совпал точно. Finish/receive завершены, paths=[], unexpected=[].
- Последняя ack-проба первоначально отказала до логики: тестовая фикстура вызвана вне Test Runner без LogScope.
  Исправлен только временный стенд (создание/Dispose LogScope через reflection), исходники не менялись.
  Ticket140 / тот же a3d5e936: ack13/13 GREEN, SDK-событие ровно одно; preview/rejection/duplicate ack молчат,
  архивирование не переносит тело, старый аватар после смены связи игнорируется.
- Итоговый полный EditMode на a3d5e936: job9d03eb1d7b904057ad8544516f4fdbf8 завершён,
  XML2145: passed1999, failed146, skipped0. Новых отказов против прежнего полного147 нет;
  GameModeWiringTests про старый оркестратор Lobby больше не падает после изменения базы.
  Единственное отличающееся сообщение LobbyLayout — прежний физический шум положения гранаты.
  Отчёты full-base-final-native-results.xml и full-base-final-comparison.json.
- Общий TestResults.xml сначала относился к параллельному чужому прогону2116 и не доказывал завершение нашего.
  После сверки job2145/2145 и времени сохранён правильный XML2145; чужой — отдельно unrelated-concurrent-native-results.xml.
  Finish140 дождался готовности worker и завершён успешно, receive выполнен, paths=[], unexpected=[].

## Приёмка и итоговые проверки

2026-10-07 пользователь подтвердил «все работает — тесты и коммит» после ручного Play на worker,
checkpoint10a65e1b, ticket148: Host/Lobby, локальная сессия/аватар, консоль без ошибок.
Наблюдатель после Stop восстановил настройки и завершил finish/receive, paths=[], unexpected=[].
Отдельные результаты каждого пункта и удалённого двухклиентского прогона пользователь не перечислял.

После приёмки добавлены22 проверки: поза между картами/в снимке, lifecycle, устройства,
SDK binary replay, preview/rejection/ack и сериализация. Тестовый OwnerSession получил подключённого
локального клиента без сокетов; игровая команда не подавляется и реально доставляется серверу.

- RED ticket158 /5770fe3d: новый тест ловит именно `CmdRequestCalibration ... without an active client`.
- RED ticket160 /9c09e99f: временная camera-dependent SDK-мутация даёт ошибку root0,837436м.
  Мутация не входит в рабочий код/коммит.
- Ticket162 /76eb59ca:66/69. Три raw-value ожидания требовали побитового равенства нормализованного quaternion.
  Runtime probe42° подтвердил exactEqual=false, angle=0°; тесты проверяют геометрию и конечность всех компонентов,
  норму quaternion и эквивалентность q/−q. Остальные поля и Mirror roundtrip сравниваются точно.
- Итог ticket167 /f4e1a31b: Android PASS,69/69 GREEN; полный EditMode2167:2021 passed/146 failed/0 skipped.
  Все146 отказов и сообщения совпали с предыдущим полным2145; новых/исчезнувших нет.
  Отчёты final-android.json, final-narrow-job.json, final-narrow-native-results.xml,
  final-full-native-results.xml, final-full-comparison.json. При проверке XML сверяются число тестов и путь assembly worker.
- Независимое read-only ревью gpt-6.1-sol/high: Critical/Important нет. SDK-стенд владеет своим менеджером,
  восстанавливает прежний singleton и очищает регистрации через finally даже при исключениях.
- Все проверочные аренды завершены и получены;167 paths=[], unexpected=[]. Полный набор проекта остаётся красным.

Чеклист пользовательской проверки:

1. Две метки: физическое место и поворот совпадают; пол, глаза и руки сохраняют заданную высоту.
2. Смена Cyborg/MEF/Heavy при ещё живом старом теле: повторная калибровка не нужна, смещение рук не переносится в чужое устройство.
3. Смена карты с повёрнутыми/сдвинутыми якорями: физическое место и поворот сохраняются в системе якорей.
4. Переподключение живого/мёртвого игрока: положение и признак калибровки сохраняются, восстановление здоровья отдельно.
5. Отказ нового замера в бою: локальная позиция возвращается, повторный ответ не вызывает повторного переноса.
6. Хост и удалённый клиент: визуально подтвердить перенос и положение рук; EditMode/локальный binary replay не заменяют сетевую XR-проверку.
