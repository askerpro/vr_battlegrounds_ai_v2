# Матрица оставшейся проверки

2026-10-19. Текущая base `48d38b9b2885b771f9cc961b4b640eaa7f74ca9e`.
Runtime818/824/829 проверяли исходную base2e1b6a2fc с указанными technical inputs;
новый startup-порядок после этих прогонов ещё не реализован.
Источники плана: принятая задача `tasks/bots-fix`, её cc1-plan, cc2-director,
cc3-perf, pf-network-spec и next-iteration. Старый PASS относится только к своему
input/роли/методике. Новый результат вносится с SHA, ticket, raw evidence и областью.

## Очерёдность и критерии

| Блок | Что осталось проверить | Текущий результат / ограничения |
|---|---|---|
| Host | Ходьба, стрельба, хват | Принято пользователем на прошлой итерации. Новые активные тела/позы требуют fresh provenance и визуальной проверки. |
| External startup | Общий runtime contract подготовки сервера, bind/advertise и первого подключения клиента; затем Dedicated +2PC Spectator bot0/bot8 discovery/localhost и длительная init любой роли |824/829 выполнены: localhost connect за~80s доlisten/SocketException; discovery timeout внутри server late-gap12.33s. Нужен новый порядок server preparation/publication и client waiting; реализация ещё не начата. Rootcause main Editor70s reload/30s map не выделена; role-swap PASS не acceptance. |
| Spectator | Bodyless, team0, карта/позы/оружие ботов через обычные receive пути, disconnect/reconnect |824 discovery0 Ready:2PC Spectator bodyless/team0, reconnect epoch1→2. S8 с8ботами не достигнут. S8 — именно Spectator; Player/team0 не подходит. |
| Remote Player | S2C/H1C, latejoin, оружие в руках/кобуре, смерть/новое тело, reconnect; owner self-skip | Нужен новый input. Клиенты не должны создавать bot-компоненты/второй lifecycle. |
| cc1 E/PE/H/U/P/G | PPK/AK105: штатный Equip/Prepare/Holster/Unarmed/Purchase/Death/Ghost/Respawn; одна связка владельцев, стационарная цель | Прошлые E/PE и G не закрывают всю матрицу; P стенда не доказывает Director→Buy→Offer. |
| Blaze proxy A | Существующие T01/T06/T08: normalizedTime, Move/footpath, missing states | Generic 1 s вариант B нужен только при провале A; не новая обязательная feature. |
| Director M2 | 4 scored rounds, admin Stop, второй матч с тем же Session/Mind, закупка/экономика/context | firstNaturalProof=true означает четыре раунда до Stop, а не natural match end. |
| Lifecycle | Pause/resume, reload той же/другой карты, Respawn/new life, natural Offer refusal, spawn provenance | Normal UI/MapLoader; continuousDirectorEquip до AddBot. Записывать session/mind/body/runKey/modeEpoch/shopToken/economy/inventory. notExercised = UNKNOWN; sampled maxOwned1 не доказывает ровно один spawn. |
| Weapon × avatar | Каждый активный body и weapon: emission/outcome/readiness, ActorSource/session damage credit, смерть/restore/cleanup | Исторические802:240 + observer1 и G805 относятся к старому input. Пассивный observer не готовит и не стреляет. |
| Contact | Runtime right/left + whole body, точные GUID/pose/frame, визуальный контакт включая закрытую часть хвата | HandFit analysis_incomplete не PASS; ±2 mm не утверждённый общий порог. Нужна проверка Quest. |
| SDK registry/startup | Fresh startup, next map, abort/rejoin/terminal cleanup | Исторические795/798 и getterErrors0 не доказывают отсутствие утечки; +150 MB mono не принимается как leak-free. |
| Existing checks | Применимые неизменённые Editor/managed/Android checks на fresh input | Android817 PASS и Android829 Passed/Errors0; native Floor818 existing EditMode99/99/catalog7/0. Старые .NET fixtures не компилируются из-за отсутствующих типов/stubs, не переписаны. Исторические788:49,792/796:40,798:48+2ignored,802:240+1 не fresh PASS. |
| N8 H/C | Методически валидные одинаковые seed/common/probe/hardware/quality/warmup/windows/frames/roster | H800 был zero shots; C801 cap600/completedfalse; H′804 диагностический. H′ не объявлять baseline без решения по методике. |
| Performance | H8/C8/H10/C10/RH/S8 | Все шесть fresh cells. C8 Combat p95 <= свежий H8; GC/scaling без выдуманного допуска, bot bytes/s <= remote human; свой session/avatar исключить. |
| Quest | Quest-client bot0/1/9 + PC dedicated frame9; качество и metrics против сопоставимых remote dummies | Решение4895 заменяет старый Quest-host/1/8/16. Физический Quest ещё не проверен. |
| Release | Новые изменения → соответствующая userQA → регрессии → зональные review → обычный FF origin/dev → task-close | Floor080f137aa/helper48d38b9b2 опубликованы с новыми approve/ACK по явному исключению пользователя без QA. Их userQA открыта; остальные runtime/Quest проверки не приняты. Future pilot-split/script-brain сюда не входят. |

## Методика измерений

У performance фиксировать длительность и frames, allocations bytes/frame и bytes/s,
Gen0 rate, валидность recorder, roster и все console counters каждого процесса.
Отсутствующие/невалидные данные — UNKNOWN, не нули. H10/C10 — Host с 10 ботами
и человеком; RH — dedicated 8 ботов + 1 Player без своего session/avatar;
S8 — dedicated 8 ботов + 2 Spectator после функционального прохождения.
Сравнение H/C требует одинакового фактического seed до создания ботов.

Quest evidence: exact build SHA/input, устройство, роль, сцена, Hz/FFR/CPU/GPU,
quality, снимки/видео по необходимости и полные метрики. Editor/Simulator/Android
compile не заменяют физическое поведение. Worker освобождать до ожидания человека.

## Совместимость старых проверок

- `pf-round5/b0-preflight.cs` ожидает старые body GUID и удалённые
  BotBodyMode.PlayerFlow/AddBot(mode)/BotStandRunOptions.PlayerFlow. Не запускать
  с одной заменой GUID и не обозначать отказ obsolete fixture как runtime defect.
- `tools/net-round1` содержит удалённый PlayerFlow field: сначала проверить
  совместимость payload. Сценарии E/A/P/G/LJ/LH/RC остаются планом, не результатом.
- Старое ожидание ghost «ничего не пишет» неверно для принятой архитектуры:
  штатный BotAvatarDriver ведёт rest/travel; запрещены Hands/Aim/Combat/grab/shot.
- M1 не обязан стартовать: минимум два участника/две команды.
- Непинованный cc3 B0 probe даёт CS0123: FinishActive с optional вторым аргументом
  не соответствует Func<string,string>. Пинованные H/C с одним аргументом ранее
  компилировались. Не чинить expectations до приёмки проверяемого поведения.
- Старый analyze.py мог принимать пропущенные lifecycle/participants/counters
  за нули и выдавать PASS. Его вывод без raw evidence не критерий приёмки.
- `LedgerSpawnReadyTests.Отказ_replay_при_выключенном_учёте_виден_один_раз`
  ранее исключён как obsolete; это не пройденный и не обновлённый тест.

Тесты новой/изменённой функциональности добавлять или переписывать после
пользовательской проверки соответствующего поведения в этой же задаче.
