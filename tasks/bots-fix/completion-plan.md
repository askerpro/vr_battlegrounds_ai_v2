# Завершение bots-fix после перехода на «задача = агент»

Дата: 2026-10-18. Ведущая сессия — `01a14cd3-0777-7292-8802-d68a8df74022`.
Цель и архитектура утверждены пользователем в `player-flow-plan.md`; текущим поручением
вся необходимая межзонная реализация передана этой сессии и её субагентам.
Машинные этапы и области записи — только в `plan.json`. Эксперты дают знания и ревью.

## Исходное состояние

- После fetch `origin/dev=50c27ae71da47d43245cbd4eb1ba6747a2a7e44c`.
- Собственный HEAD `f10735c1c42eb68ca9095587c5a0819701600fde`: 0 behind / 24 ahead.
- Собственный WIP `bca9e3632d3d8d3c4659d2d68a16d4407f69ff67` — только сохранённый отчёт
  `reports/cc2-lifecycle-review747.md`. Код cutover уже в предыдущих checkpoints;
  последний содержательный — `89ce6646b7b0db09570f2a297da017903d53c25a`.
- Хаб r54: clip-cutover PLANNED epoch4, остановлен при переносе worktree.
  pf-prototype/equipment/network MERGED; старый Readme архивирован отдельно.
- Все проверенные соседние checkout чистые, кроме активного `cyborg-hands`:
  6 tracked + 6 untracked по prefab hierarchy/Editor. Их не переносить автоматически.

## Карта невлитых зависимостей

| Задача / область | Точный источник | Решение в этой задаче |
|---|---|---|
| weapon-system / R08 F5 SDK82 | WIP `88c664f0925e5d5d0d38000b026d9bbc32561d32`, HEAD `7e3605570` | Интегрировать минимальный связный source + profile/R08, без shot-cost tools. Unity768/776 — историческое authoring/Android, не runtime-приёмка |
| arsenal-generator / Spectator | WIP `2453262ed460687380afe83239e9d0ebe1ff39e0`, HEAD `a8a73d14d` | Перенести пять функциональных source-файлов совместно с network, проверить Player-only startup |
| network-sync-hardening / spectator-connect | `7b67ce0512f0fb8c1cb76b8782178bf4484a80d4`, HEAD `cc050e3ac` | Перенести GameNetworkManager вместе с gameplay API, проверить Ready и отсутствие тела |
| uxr-precache-once / SDK81 | свежий technical checkpoint `6a0fde1e0ad9771075cd47e047fe319f30bc84a7`; старая ветка `avatar-ik/uxr-precache-once` | Проанализировать и довести Host/latejoin/abort в своей задаче; не брать устаревший tip `e21b43c2a` вместо свежего source |
| hand-rig-quality / external fingers | WIP `3887876ae` / backup `29ba41f1b` эквивалентны по patch-id; runtime fix отсутствует | Диагностировать штатный выбор позы UpdateExternally, обеспечить одного SDK-писателя; активную prefab-миграцию не захватывать |
| vr-test-stand / seed Variant B | принято `192eb34f8d1b2139341db3593b98f4e75e573f9f` | Уже origin/dev; подключить cc3 к API и actual receipt, повторно backend не реализовывать |
| perf-stress-scenario | WIP `1d85f2305`, HEAD `ab370f985` | Метод fresh H/C обязателен; GPU/scene-detail инструменты автоматически не переносить |
| avatar-ik/calibration-scale-events | WIP `4c5fc8d9a`, HEAD `3a550228c` | Только task docs сверх dev; не подменяет SDK81 |
| manipulation/haptics | HEAD `b37dd4865` | Только перенос doc paths сверх dev; новых bot blockers нет |

Принятые foundation, precache-server-guard, seed, death-body-defer,
grab-authority/holster, pocket-removal и Mirror keyframe повторно не переносить.
Стабильные эквиваленты: binding `93f67ded` → `6849ba0e6`, guard `1e63f01f7` →
`377fc9551`, seed `4ee701356` → `192eb34f8`.

Формальные контракты: binding r2 ACTIVE (`6849ba0e6`), r3 AGREED (bots54/weapon67),
use-context r2, shooter r1, grab-lifetime r2 ACTIVE (`74de91912`). Binding r3 активируется
после принятого вливания. `weapon-system/acceptance-g` ждёт merged cutover, поэтому
ожидание acceptance-g со стороны bots-fix создало бы цикл. `held-root-motion@1` из старых
Details не зарегистрирован в hub и не вводится как новый блокер.

## План исполнения

Термины сетевых ролей (сверка по вопросу пользователя 2026-10-18): погибший остаётся
`GameRole.Player` в команде с `IsEliminated` и телом Ghost. Подключённый игрок до выбора
команды — тоже `GameRole.Player`, `TeamIndex=0`, со штатным fallback avatar. Отдельный
`GameRole.Spectator` выбирается профилем подключения: не участвует в бою, без команды
и игрового тела. В S8 два таких клиента наблюдают восемь ботов. Ни смерть, ни отсутствие
команды сами по себе не меняют Role на Spectator.

- [x] Восстановить WIP, ветки без checkout, dirty, контракты и canonical события.
- [x] Собрать Spectator gameplay+network, сохранив existing guards и общий Player lifecycle (source, без runtime-приёмки).
- [x] Перенести девять runtime source SDK82 и WeaponSystemAuthoring побайтно из WIP `88c664f09` (компиляция впереди).
- [x] Импортировать MagazineOnly profile/meta, R08 и Revolver prefab из WIP после завершения чужого serialized этапа781.
- [x] Разобрать и перенести SDK81 побайтно из `6a0fde1e0`; Host/latejoin/abort ещё требуют runtime проверки.
- [x] Подготовить штатный SDK selector патч84 и exact spawn provenance; runtime/визуальное качество ещё не принято.
- [x] Подключить seed20261018 и actual receipt к существующим cc3 payloads, без изменения test expectations.
- [x] Пересобраны H36d3f350/Caa0956c5 после registry/SDK87: общие29 assets, diff48, exact manifests и semantic compile каждого11+5 errors0. Это техническая упаковка без runtime/приёмки.
- [x] После Mirror6/Host guard/explicit MinPlayers: common30 H=d5933476e/C=e1d95e8c2, frozen81/diff48/probe2. Exact offline каждого12сборок+5payloads errors0, свежий Mirror первым, foreign0; прежние common29 результаты исторические.
- [x] Unity795 inputf8: actual Host LocalAvatar/session/ClientReady и завершённый precache; обычный remote rejoin epoch1→2, Host сохранён. Два post-rejoin снимка через63.956с без роста transforms/copies/fades. Next-map/abort/Quest/полный registry не проверены; mono вырос150626304Б. DONE/receive180463621, SHA25621/21.
- [x] Офлайн-компиляция11 сборок и обоих пакетов по5 C#6 payloads; visual также с текущей пробой матча, errors0.
- [x] Unity788: неизменённые применимые EditMode49/49, полный job result сохранён вне Git; это не runtime/Quest-приёмка.
- [x] Unity792: неизменённые EditMode40/40 для registry/initial snapshot/late join/pocket; полный job08a29c3b сохранён. Повтор Spectator не достиг полной готовности двух клиентов; runtime приёмка открыта.
- [x] Unity796 наf8: те же40/40 существующих EditMode, полный joba53e145d. Min9 удержал Warmup131.360с при8Player+1Spectator; другой клиентinitialtimeout. DONE/receivef0761dd0b, центральные20SHA20. Это не2client/reconnect/S8PASS.
- [ ] Unity: R08 emission/ActorSource/damage/cleanup; visual old/new обе руки с source identity;
  четыре scored раунда, pause/resume, reload, Respawn и естественный отказ покупки.
- [ ] Fresh H8/C8, H10/C10, RH/S8; одинаковый seed/base/SDK/quality/прогрев, валидные recorder;
  C8 p95 не хуже H8, GC/scaling, bytes/s с исключением собственного клиента.
- [ ] Пользовательская приёмка качества; после неё новые/обновлённые тесты и удаление архивов.
- [ ] Ревью всех затронутых зон через review-request/review-prompt, журналы зон, fast-forward release,
  binding r3 activation и task-close. Pilot split/script brain/Quest — оставшиеся этапы исходной цели.

## Границы проверки

Уточнение состава R08: в оружейных Readme/Details сохранено решение пользователя от
2026-10-17 перевести оба револьвера. Поэтому `Revolver.prefab` входит в тот же перенос;
таблица WeaponSystemAuthoring уже назначает ему MagazineOnly. Его Git blob — `970315c5c`,
общий GUID профиля — `385b9018624991f44a28d52cc49e5a67`.

Старые 738/747/751 и Weapons141/141 не являются проверкой нового объединённого input.
Четыре fit analysis incomplete остаются открытыми; в783 applied pose/source подтверждены
для MEF+AK105 до/после respawn. Это не приёмка геометрического контакта и не покрытие других тел/стволов.
Старые PPK67/68 и foreignDamage1 сохраняются как исторические FAIL/UNKNOWN.
Новые/переписанные tests, smoke и managed probes с expectations до пользовательской
проверки не создаются. Допустимы компиляция, неизменённые existing checks и диагностика.
User acceptance предшествует accepted commit; технический checkpoint её не заменяет.

Все edits только в своём checkout. Сериализованные assets берутся побайтно из исходного
Git/Unity результата, не переписываются вручную. Не выполнять одновременно с чужим
активным serialized этапом. Unity — штатный broker и немедленный finish после пакета.
Состояние broker прочитано: blocked=false, recovery_required=false, чужая заявка781 RUNNING;
ошибка открытия БД внутри sandbox исчезла при штатном escalated read-only status.

## Источники аудита

Canonical события: 7933, 8064, 8081, 8272, 8388, 8491, 8565, 8576, 8578, 8583,
8591, 8758, 8843, 8885 (seed merged), 8895 (gameplay Spectator), 8897 (R08 authoring),
8901 (network candidate), 9011. Текущее состояние проверено через status и unread inbox.
Соседние Readme и reports содержат исторические доказательства. Свежие Unity783/785/788
имеют отдельные input SHA и области доказательств в компактном Readme; результаты
не переносятся между snapshots автоматически. Спецификации player-flow/clip-cutover/cc1
и cc3 acceptance сохраняются.
