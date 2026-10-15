# Перепроверка событий масштаба 2026-10-14

## Принятие и интеграция 2026-10-15

Пользователь: «считай, что я принял — вливай». Приёмка подтверждена, новое подтверждение не требуется.
Rebase на8eb877915 без конфликтов; production applier не менялся. Добавлены9 тестов принятого контракта
после приёмки: callbacks вокруг масштаба/BodyIK на своём и чужом аватаре; повтор/floor-only; epsilon;
сброс; фактический oldScale; исключения обработчиков; отсутствие Dummy.
Тесты дополняют26 существующих, не заменяют multiplayer/Quest. Свежий пакет — worker678,
checkpoint817677550c29593c96e66fbbaa15a53d8de9c33a.

Статическое whole-branch review: замечание к владению singleton в test cleanup устранено;
snapshot существующих UxrManager берётся через Resources.FindObjectsOfTypeAll. Других блокеров чтением нет.
Старые изменения experts/avatar-ik/backlog.md и expert.json из истории этой ветки исключены из итогового
diff относительно принятой dev. Вливание ограничено applier, тестами и task material.
Непринятый WIP прогрева сохранён отдельным stash; его Readme/plan восстановлены как untracked provider
документы ради live immutable grant и исключены из checkpoint/commit калибровки.

Решение по тестовому порядку: production реализован и принят до этой итерации согласно AGENTS.md;
эта итерация добавляет регрессию на принятую логику и не объявляет фиктивный RED→GREEN.

Worker678/epoch574 завершён и получен: resultc9d0013451b0610e8ac00ad5c26388325d9a9d04, paths=[],unexpected=[].
Applier/source и test/source hashes совпали, оба события есть в загруженной сборке, AndroidPASS0.
MCP jobc9a4bb0b2f2349b89857b056eff432db дал полный terminal Result26/26PASS, но новый класс9 не обнаружен.
Это известный terminal исход, не UNKNOWN и не PASS35. Test type отсутствует до/после, scriptCompilationFailed=false.
Сразу finish/receive, затем infra_issue7416+native сопровождающему; VERSIONeb4ab1d981654f3bcd64cd4eb546cbcc950370d6.
Офлайн SDK555/game461/tests244 — errors0. Спецификация35 проверок сохранена в
`reports/accept-20261015/test-spec.json`; полного подтверждения нового класса ещё нет. Не запускать
альтернативный клиент/самостоятельный refresh ради ремонта инфраструктуры, дождаться безопасного решения.
До ответа сопровождающего допуск освобождён штатным stop; user acceptance/merge authorization сохраняются.
Ответ7457 от2026-10-15: worker/MCP восстановлен, runtime f6387f9, обычная FIFO доступна.
Следующий собственный пакет: явный import/compile, discovery нового и контрольного старого типов,
Android gate, ровно4 класса/35 тестов; полный terminal Result сохранить до finish. При отсутствии
нового типа тесты не запускать, finish/receive и адресный infra_issue. База после rebase bb2804607.
Worker685/epoch581: A78bd82b2d0957f49352d24d2258d8cc359bc3afb,
R3986e3264a98791ccee60e77f88b3e3dffd77a70; paths=[],unexpected=[],received=true.
`refresh_unity(force,scripts,compile=request)` дал refresh_triggered=false; новый тестовый исходник
на диске точный, Type.GetType нового класса=false, контрольного CalibrationHeightReplicationTests=true.
TestRunnerApi discovery: PlayerCalibrationOwner16, ScaleReplication5, HeightReplication5; нового класса нет.
Прогон не начинался. Следующий пакет исправляет пропущенный asset import: scope=all/явный ImportAsset
собственных новых файлов, затем compile, discovery35, Android и стандартный run_tests.
Scope=scripts не считать доказательством выполненного импорта. Evidence сопровождающему7529,
полные native receipts в reports/worker-685/. Изменения конфигурации/worker/MCP не выполнялись.
Worker691/epoch586: Acdfa813ab70de0abf35d5313ec67a9cafd54c0a2,
Ra874bfa80a6eb412f180e67df7545d2ade38bac2; paths=[],unexpected=[],receive выполнен.
Явный ImportAsset нового файла imported=false; scope=all refresh_triggered=true, compile завершён,
но новый тип отсутствует при наличии контрольного. Тесты не начинались.
Найдена собственная ошибка добавленного test.cs.meta: GUID из33hex. Исправлен на32hex
`a2efaf4461a646c2a957561070802f29`; ссылок на прежний некорректный GUID нет.
Исходники applier/tests не менялись. Следующий FIFO-пакет проверит точный GUID и MonoScript,
compile/discovery35, Android и полный standard Result. Инфраструктура не ремонтировалась.
Worker696/epoch591, база1812dc5bf22860e8302e858b1637e92b1df636e9:
A9898b546c4c031e7cd22ebb352523a1bf4b3c8b9, R576f0c633ed706a8faf774181d46d812956069d5,
paths=[],unexpected=[],received=true. Импорт MonoScript=true, GUID=a2efaf4461a646c2a957561070802f29.
Applier SHA256LF2f5a2288c5e41262449ac008bbd45f62ee3c0c7303f74e4757ccd38fcc5ffc67;
test SHA256LF5f8c255775e90685d9699b172625e920f411e1c77fd137ed9399a1ac9df1a347.
Оба типа загружены; Console errors0; discovery4fixtures=16+5+5+9, все Runnable.
AndroidCompileGate PASS/errors0. Job882e2dac869f4b1ca2f7f71ec988f5e2 terminal succeeded:
Result.summary total35/passed35/failed0/skipped0; полные35details, новых9 и все9Passed.
Полные native ответы reports/worker-696/. Это scoped EditMode/compile, без физического Quest evidence.
При следующем rebase/stash сохранять live provider Readme/plan прогрева на месте: убирать в stash только
точные пути калибровки, не tasks/uxr-precache-once целиком. Повторная выдача grant ради переключения не нужна.

Ветка `avatar-ik/calibration-scale-events` перебазирована на принятую базу `d3c62ca5e` без конфликтов;
HEAD `f4ac8c20f`. `AvatarCalibrationApplier.cs` не изменился относительно предшествующей ветки `d90553c62`.
Старое свидетельство worker590 (26/26 на8ec241cd9) сохраняется как историческое.

Единственный писатель масштаба и производных полей тела — AvatarCalibrationApplier. События экземплярные,
синхронные, на каждой машине вокруг записи масштаба; контракт r1 agreed, новая ревизия поведения не вводится.
Потребитель manipulation не меняет калибровку из обработчика. Ошибка подписчика не прерывает Apply.

Пакет worker: checkpoint точного исходника и документов, без выходных ассетов; импорт/компиляция Unity,
AndroidCompileGate, существующие CalibrationHeightReplicationTests, CalibrationScaleReplicationTests,
PlayerCalibrationOwnerTests. После окончания или отказа проверок — finish/receive до диагностики и ожидания.
Play в этот пакет не входит. Тесты новых событий до пользовательской проверки не пишутся.

Приёмка пользователя: калибровка роста в лобби, прежняя посадка тела и масштаб, нет ошибок applier;
пока нет подписчиков, поведение остаётся прежним. Принятие/merge — после подтверждения, через брокер.
Вливание событий отдельно от потребителя кобур; manipulation получает уведомление после принятого merge.

Worker638/epoch537: Unity6000.4.1f1, исходник SHA256
`2f5a2288c5e41262449ac008bbd45f62ee3c0c7303f74e4757ccd38fcc5ffc67`, обе декларации событий найдены
в загруженной сборке, scriptCompilationFailed=false. AndroidCompileGate — PASS, ошибок0.
EditMode job d6a15c7668fc4aaf8ec66e8752f45470: 26 passed, 0 failed, 0 skipped.
Finish: result b4b58a3e148e55a642b52556f2011421e98b0281, paths=[], unexpected=[].
Артефакты — `reports/worker-638/`, включая полные ответы MCP. Это техническая проверка существующего
поведения, не приёмка новых событий пользователем; тесты новых событий ещё не написаны.
