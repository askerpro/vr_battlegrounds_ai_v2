# Собраны невлитые зависимости player-flow

2026-10-18. Прямое поручение пользователя: задачу завершает одна ведущая сессия
с субагентами во всех нужных зонах; эксперты подключаются для ревью.

## Переносы

- SDK81: `UxrManager.cs` из technical checkpoint `6a0fde1e0ad9771075cd47e047fe319f30bc84a7`.
  Сохраняется исходный номер81; приёмка Host/abort/latejoin не переносится из исторического прогона.
- SDK82/MagazineOnly: десять C# из WIP `88c664f0925e5d5d0d38000b026d9bbc32561d32`.
  Номер82 уже зарезервирован исходной задачей weapon-system; новый номер для переноса не выдаётся.
  Runtime.source: readiness mode provider, cold replay/correction, MagazineOnly, atomic snapshot guards;
  Editor: существующая таблица WeaponSystemAuthoring назначает профиль R08 и Revolver.
- Четыре Unity-authored blob из того же WIP импортированы Git после завершения foreign781:
  profile `a90ed8ed73e4617a4516383af7e7dbe3b286b971`, meta `8288c6d0bf903fe8d465c7d11db9747b4c5d3cc8`,
  R08 `a8291ad6ceb814345f0ab8515bf2bf40d1b22b5d`, Revolver `970315c5c9fc78f8f0cb080026e8ed2f4490509e`.
  Physical hash-object --no-filters совпал со всеми четырьмя source blobs; YAML вручную не менялся.
- Gameplay Spectator: пять функциональных файлов из WIP `2453262ed460687380afe83239e9d0ebe1ff39e0`.
  Network: `GameNetworkManager.cs` из `7b67ce0512f0fb8c1cb76b8782178bf4484a80d4`.
  Дополнение: DebugOrchestrator.TryGoLive считает Role.Player. Другого lifecycle владельца нет.
- SDK84: отдельный диагностированный selection для external bot hands, см. запись патча84.
  Это не часть SDK81 и не перенос editor/prefab-миграции Cyborg.
- Seed backend уже dev192eb34f8; cc3 теперь задаёт20261018 и сохраняет actual receipt до cleanup.
- Visual provenance слушает NetworkUxrIdentity.InstanceCreated и записывает exact instance→template,
  registry witness, SDK prefab id и результат selector. Не наблюдавшееся создание остаётся UNKNOWN.

Shot-cost tools, GPU/scene-detail диагностика, незавершённые Cyborg assets и чужие task docs
не включались. Все существенные границы и история контрактов — completion-plan.md.
Уведомления о едином исполнении доставлены в canonical inbox: weapon9114, network9116,
gameplay9118, SDK81/IK9120, grip9128, launch9130, performance9132. Ответ grip9134 прочитан:
его prefab-hierarchy остаётся отдельной задачей, новых принятых prefab ещё нет.

## Фактическая проверка

- Compile cc3: 11 сборок и5 C#6 payloads, exit0/errors0. Editor-зависимости собраны из текущих
  исходников: первый проход со старыми DLL дал39 Editor и8 payload ошибок; после обновления порядка
  сборки собственной compile-check.ps1 ошибки исчезли, продуктовый код ради них не менялся.
- Visual: 11 сборок и5 C#6 payloads, exit0/errors0; повтор с фактическим текущим
  BotStandMatchProbe `f10735c1c`, SHA256 `868a48b2a2970c353df01afb226090f99d838f1de66d65bf94c71d8e54014acd`,
  также exit0/errors0. cc3 сохраняет отдельную pinned common probe для будущего H/C.
- Полные журналы: `tasks/bots-fix/reports/consolidation-20261018/` (вне Git);
  свежий exact-probe проход первоначально в `%TEMP%/bots-consolidation-20261018-current-probe`.
- Physical LF проверяется только на43 изменённых/новых собственных путях. Native apply_patch
  сохранял прежний CRLF одиннадцати файлов, поэтому после двух native попыток выполнена только
  нормализация CRLF→LF этих owned paths через UTF-8 WriteAllText; логика не менялась.
- `diff --check` managed/docs чист. Два whitespace сообщения в импортированных Unity prefab
  относятся к точным source blob строкам `m_Name: `; YAML ради линтера вручную не менялся.

Unity этого объединённого source, сетевой клиент, визуальная/Quest проверка и пользовательская
приёмка ещё впереди. Новые/переписанные tests/smoke/managed probes не создавались.

## Обновление после Unity783 и первого ревью

783 завершён штатно, receive ok, source paths0: input43b58afd4, result354cee7e1.
Контроллер и B0 ready; PlayLaunch RunId3163c32791a84550abb5bdf53ce360e9.
G old2379/new3819 сняты, анализ4 рук incomplete; визуальная приёмка остаётся открыта.
StopOwnRun: Idle, CleanupPassed=true, profileRestored=true, captureCameras0, pending0,
detached=true. Finish выполнен после завершения операции; worker передан следующей заявке.
192 артефакта в reports/received783, SHA256 центральных и локальных копий совпали192/192.
Транспортные disconnect/timeout не повторялись; итог установлен по durable evidence.

Ревью avatar-ik5773c53189974be9 и grip6fcb9a2b0a58439e — changes: EventArgs и yield
в неизменном покадровом выборе позы. SDK84 расширен на UxrAvatar/HandState/GrabPointInfo:
проверка изменения до EventArgs и прямой обход актуальной GUID/default-цепочки без кэша.
Accumulated11 Docs/expert paths объявлены явно в clip-cutover полного кандидата;
этап product-docs сохранён для окончательного описания принятой реализации.
Generated reports/cc2-lifecycle-review747.md исключён из кандидата после точного копирования
в ignored tasks/bots-fix/reports (SHA256 EFFD2BC6BD5803AB75962F826577A1826A56E44F95693048ABB63010FA799595).
Исходный WIPbca9e3632 и его история сохранены; это исправление размещения отчёта.

Результат783 уточнён: selected authored AK105 Grip/Support и exact instance source identity
до/после respawn подтверждены; fingerprint не менялся при capture. 22 Fired/13 DamageReceived,
ghost shots0, source mismatch/foreign/null0. analysis_incomplete вызван отсутствием analysis volume,
неполным геометрическим binding и неизвестной owner semantic mask; это не acceptance контакта.

Раунд2 avatar-ik выявил ParentAvatarPrefab.GetComponent в hot path: добавлен положительный
cache по живому ParentPrefab и Unity-null компонента, отрицательный результат не кэшируется.
Network review P1: удалён доверенный клиентский msg.isAdmin из HandleSpectatorConnect;
как у Player, новая сессия IsAdmin=false, полномочия выдаются существующим серверным путём.
Повторная офлайн-компиляция source r3:11 сборок+5 payloads, errors0.

785 запущен на source r2 snapshotdf0b97be2: B0 transport timeout, затем get_history подтвердил
success/ready=true/hashes747/elapsed64004.6ms; durable preflight появился. Повтор не выполнялся.
R08: существующий B3 с новым root cc1-r08-20261018-r1, options T07/seed101/35s/Capture=true,
WeaponName=R08_Weapon. Менялись только параметры существующей диагностики, expectations не добавлены.
Canonical infra_issue9169 доставлен; route сопровождающего recipient_cleared, native недоступен.

785 DONE/receive, result6c35e0224af95c44703da22e2b724ce11521973b, paths0/unexpected0.
70 артефактов reports/received785: SHA256 центральных и локальных копий совпали70/70.
R08 T07/35.055s: Fired92, Recorder90/90 requests/projectiles, hits65. Recorder.Begin откладывает
подписку на1с: два первых выстрела frames42/46 до первого Sample48, последующих ровно90.
Revolver T07/35.105s: requests56, Fired34, Rejected22, hits28, firstShotAfterEquip11.674s.
Причина Rejected этим журналом не определена. Оба normal recoil/spread, MEF Black,
fixtureReadytrue, errors0, foreignDamage0. damageTrace выключен (KillAtSeconds<0), поэтому
полная Actor/Source и projectile terminal атрибуция не подтверждены. Все67 visual samples
каждого прогона имеют readinessProfileValidtrue. HandFitfalse, контакт не принят.
Оба exact launch остановлены: Idle/CleanupPassedtrue, profileRestoredtrue, ownedfalse,
captureCameras0; исходы B5 сохранены в истории основной сессии. Полные logs экспортированы.

Weapon source review48b27cf2 выявил повторный GetComponents у legacy без provider.
Зарезервирован и реализован SDK86: negative discovery cache, явная revalidation topology,
Prepare до TryValidate, прежние forced ingress/install/snapshot и sticky invalid сохранены.
Compile SDK86 с pinned probe2fa11b27 и всеми5 актуальными cc3 payloads:11 сборок/errors0.
Полный лог reports/consolidation-20261018/sdk86-cc3-compile. Runtime785 был доSDK86.
P3 gameplay Docs про BotBody исправлен: писатель BotAvatarDriver, BotBody — источник данных.

## Подготовленный первый Unity-пакет

Проверить compile/idle/worker identity, существующий контроллер Blaze и visual B0.
Через PlayLaunch выполнить существующие B3→B5(warmup)→B4(G old/new)→B5→B6.
Новый root `tasks/bots-fix/reports/cc1-visual-consolidated-20261018-r1` и отдельный marker.
Чужие Play/грязные сцены не трогать. При росте CameraFade/неизвестном исходе — durable inspect,
без replay; после известного завершения собственный cleanup, немедленный finish с полными
артефактами root и receive. Получение отчёта не означает принятие контакта или gameplay.
