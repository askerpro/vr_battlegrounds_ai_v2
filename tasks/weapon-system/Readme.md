# Единая WeaponSystem: машина состояний оружия

## Цель и мотивация

Всё поведение огнестрела — затвор, патронник, спуск, поза, звук, вибрация, отклик — решает одна машина
состояний `WeaponStateMachine`. Учёт патронов один, в SDK; у каждого канала вывода один исполнитель.
Раньше за одно и то же отвечали несколько компонентов и правили состояние друг за другом: рассинхрон позы,
двойные звуки, сетевая ошибка учёта.

## Статус

Обновлено 2026-10-11. Владелец — эксперт `weapon-system`; задача перенесена в его постоянный worktree
`F:/CodexWorktrees/weapon-system/Vr_Battlegrounds_ai`, ветка `claude/weapon-system` (хаб 3358/3361, план r39).

Выполняется `use-context`: единый контекст руки и экипировки для реальной стрельбы NPC.
VR/Equipment, SDK70/71/72 и canonical authoring PPK/AK105 технически verified.
Незакоммиченный код в worktree побайтно равен проверенному пакету own21 `d942dce3`.
Ждём совместный NPC-прогон bots-fix и VR-приёмку пользователем; accepted commit и merge ещё не созданы.
[План и бриф](use-context.md), история проверок — [Details.md](Details.md) «Сделано».

`codex-expert`: [адаптер Codex](../../.codex/agents/weapon-system-expert.toml) приведён к общему шаблону
экспертов (3275); правила роли — `experts/weapon-system/AGENT.md`. Отдельный запуск Codex не проверялся.

Влиты: B, C, C2, `drive` (5285ceb7), `waves-f` (5af1c7ef), `ejection` (722d14df), `expert-kb` (cd497223).
На машине F1–F4; F5 ещё legacy. Есть вылет, четыре категории отклика, повтор инициализации и запрет
перехвата второй рукой. Подробности — база знаний и журналы этапов.

## Архитектура

Чистая машина (`Assets/Scripts/Weapons/Core`) → датчики → порт учёта SDK → исполнители. Полное описание, инварианты,
решения пользователя — база знаний [`Docs/weapons/`](../../Docs/weapons/README.md).

## План

Очередь этапов и риски — [`Docs/weapons/roadmap.md`](../../Docs/weapons/roadmap.md) (единственный источник).
2026-10-10: план r37 подготовлен; база обновлена fetch/rebase до accepted `1001415b`, WIP сохранён.
Контекст, SDK70 и SDK71 технически verified. Full-context exact13 candidate
`d8dec7e3` включает VR/Equipment grant и SDK prerequisites. SDK72 атрибуция verified `21fcd298`,
`weapon-equipment-shooter@1` agreed обеими сторонами. Actor adapter принадлежит bots-fix.
Canonical authoring выделен отдельным этапом на writer и два prefab; `ejection-manual` следует после него.
Binding exact11 8435c229 с actor adapter сверён; разрешён DEVELOPMENT overlay в reports,
production Assets остаются у bots-fix. SDK lifetime-порт реализован по решению пользователя.
Проект `ejection-manual` — [ejection-manual.md](ejection-manual.md), SDK-патч 69.
Машинный план — [plan.json](plan.json); подробности — [Details.md](Details.md); спецификация машины —
[refactor-plan.md](refactor-plan.md); учёт C2 — [ledger-single-source.md](ledger-single-source.md); журналы этапов —
`changelog/`; скрипты (офлайн-компиляция, claim, продление допуска) — [tools/](tools/Readme.md).

## Синхронизация

- Контракты: `weapon-use-context@2` (владелец мы, agreed), `weapon-equipment-binding@1` (bots-fix; agreed, ещё не accepted),
  `haptics-api@1` (haptics-system).
- Сопровождающий: строки `Docs/weapons` в `AGENTS.md` и `Docs/README.md` — отдельный выпуск после миграции (1494).

## Проверка

C2, `drive`, `waves-f`, `ejection` — шлем пользователя, Android PASS, EditMode (последний: аренда 337, 401 тест,
1 красный вне этапа — бюджет треугольников R08). Подробности — журналы `changelog/`.

## Следующий шаг

Блокеры на 2026-10-11:

1. Runtime bots-fix (этап B на own21 `d942dce3`). Их authoring NPC прошёл: 399, затем 402 — 3 тела,
   readback 0 расхождений (3260). Перед Play был найден пробел фикстуры стенда (3205); bots-fix его
   исправляет в своей зоне. Фактические выстрелы, SDK-учёт, ActorSource и damage credit пока не получены.
2. VR-приёмка пользователем в шлеме: стволы игрока на новом коде (контекст руки, SDK-порт отдачи,
   lifetime захвата). От NPC не зависит, может идти параллельно.
3. Вопрос infra 3202 про `.meta` файлов `WeaponAutomationCapability`/`WeaponGripBinding` закрыт ответом 3381:
   это writes bots-fix/`equipment-foundation`, уведомление bots-fix — 3383.

После 1 и 2: accepted commit, обновление тестов, merge в origin/dev; затем `use-context-prefab-authoring`
(в хабе planned, фактически выполнен в аренде 398) и `ejection-manual`.

Наши authoring/readback/idempotence и Android уже PASS, asset review завершён: 0 замечаний.
Свежая managed compile: Core6/SDK555/Game434/DebugBootstrap6/Editor263/Tests231 exit0;
тесты не запускались. Accepted commit и merge-request не созданы.
[Готовность к merge](reports/merge-readiness.md), [текущие результаты](Details.md).
