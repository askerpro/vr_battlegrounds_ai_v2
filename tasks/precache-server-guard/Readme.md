# Прогрев UltimateXR не запускается на сервере без своего игрока

Обновлено: 2026-10-14 (передача Claude → Codex). Владелец: эксперт `network`. Worktree:
`F:/CodexWorktrees/network/Vr_Battlegrounds_ai`, ветка `claude/network`. Дефект bots 6231 (копии 6233/6235), данные
performance 6245, позиция avatar-ik 6296.

## Статус

**Код готов, НЕ проверен в Unity, НЕ принят, НЕ влит.** Этап `guard` — running в хабе (begin выдан 2026-10-14,
token `bfebb18531e6455cb624b671489238fc`). Unity-проверка не выполнялась: тикет брокера 610 отменён по общей остановке
6539 (аренды не было, очередь стояла из-за сбоя publication-runner, infra_issue 6459).

## Сделано

- `f645d72f9` (ветка `claude/network`, поверх origin/dev `a84b9f2ce`): `Assets/Scripts/Network/HeadlessPrecacheGuard.cs`.
  Прогрев выключается не только в batchmode/без графики, но и у процесса «только сервер»
  (`NetworkServer.active && !NetworkClient.active`); решение — в `OnSceneLoaded` каждой сцены (роль известна после
  старта сети; сервер всегда грузит сцену карты до первого спавна тела); `RegisterAfterComposition` — то же условие.
  Регистрация выключенных UniqueId выполняется сторожем в тех же условиях. Хост и клиенты не затронуты.
  Старая перегрузка `ShouldPrecache(bool, GraphicsDeviceType)` сохранена для `HeadlessPrecacheGuardTests`.
- Офлайн-компиляция сборки VrBattlegrounds (Unity csc без weaver): 459 файлов, 0 ошибок.

## Дальше (конкретный следующий шаг)

1. Unity-проверка в аренде worker — ОДНОЙ заявкой вместе с `network-sync-hardening`: вершина `ccfaf5991` (или новее)
   содержит этот коммит. Новый `--key` (старый `precache-server-guard-guard-1` отменён). Пакет: компиляция с weaver,
   AndroidCompileGate (`execute_code` → `VrBattlegrounds.EditorTools.AndroidCompileGate.Run()`, маркер
   `[AndroidCompileGate]` в консоли), EditMode `VrBattlegrounds.Tests.Network` (37 файлов, в т.ч.
   `HeadlessPrecacheGuardTests`). Полный отчёт — в `tasks/precache-server-guard/reports/` (вне Git). Перед заявкой
   fetch/rebase origin/dev; worker и брокер на 2026-10-14 выключены — ждать объявления сопровождающего.
2. Приёмка пользователя (см. ниже). Затем вливание fast-forward в origin/dev штатным путём.
3. После вливания сообщить bots (`bots-fix`, они ждут: 6330) и performance (`perf-stress-scenario`, 6332) — они
   делают регрессию: матч 8 ботов на сервере с графикой (cc-0/M8) и perf-stress с сервером с графикой.
4. Этап `regression-tests` — только после приёмки (правило: тесты, когда нет других задач).

## Ждёт пользователя

- Приёмка сторожа: сервер с графикой (выделенный сервер в редакторе/оконный) + 8 ботов — память ровная, childCount
  `UxrManager` и число копий прогрева не растут, в журнале строка «[HeadlessPrecacheGuard] сервер без своего игрока —
  прогрев UltimateXR выключен». Хост и клиент — прогрев как раньше (без регрессии шейдеров/эффектов на шлеме).

## Решения пользователя

- 2026-10-14, AskUserQuestion по дефекту 6231: «Да: сторож + корень у avatar-ik (Recommended)» — сторож делает network,
  корень (SDK `UxrManager`) — avatar-ik.

## Связанные работы других

- avatar-ik, задача `uxr-precache-once`, UltimateXR patch 81 (коммит 91470fff6, ветка `avatar-ik/uxr-precache-once`):
  прогрев без перезапуска, раз на набор сцен, копии в контейнере `UxrPrecachedInstances`. Ревью network 6527 —
  сетевое ACK с условием: обнулять `_precacheCoroutine` при обрыве корутины (OnDisable `UxrManager` и/или
  try/finally), иначе прогрев застрянет навсегда и `IsPrecaching` вечно true. Ждём от них строку diff в inbox;
  повторное полное ревью не требуется. Сторож и патч 81 не конфликтуют (при UsePrecaching=false TryPrecaching выходит).

## Риски и ловушки

- Сторож решает по роли при загрузке сцены: если когда-нибудь тело заспавнят до первой загрузки сцены после старта
  сервера, прогрев успеет стартовать. Сейчас сервер всегда грузит карту до спавна.
- Хук `.claude/hooks/sdk-patch-guard.ps1` к этому коду не относится (не SDK).

## Координация

- Inbox: `python Tools/agents/coordination.py inbox --owner network --unread --as "<свой адрес>"`, повторять пока
  `has_more`; ожидание — `wait-inbox --owner network --as "<свой адрес>"` фоновой командой. Route опубликует новая сессия.
- Вопросы пользователю: в Codex — явным сообщением с пометкой «ЖДЁТ ОТВЕТА» и дублировать в «Ждёт пользователя».
