# Прогрев UltimateXR не запускается на сервере без своего игрока

Обновлено: 2026-10-15, продолжение в Codex. Владелец: эксперт `network`. Worktree:
`F:/CodexWorktrees/network/Vr_Battlegrounds_ai`, ветка `claude/network`. Дефект bots 6231 (копии 6233/6235), данные
performance 6245, позиция avatar-ik 6296.

## Статус

**2026-10-15 пользователь поручил коммит и вливание общего сетевого пакета. Код принят к публикации.**
Результат интеграции точного SHA фиксируется в хабе; это не подтверждение физической Quest-проверки.
Код проверен в Unity, Android gate и существующем наборе Network/Weapons (429/429 на прежнем input).
Этап `guard` — planned после штатного stop при передаче; это не отсутствие готового кода. Тикет 643 DONE,
input `01e8b7748dfb`: исходник совпал по SHA256, новая перегрузка загружена, compile failed=false, Android PASS,
EditMode 429 passed / 0 failed / 0 skipped. Finish/receive paths=[], unexpected=[]. Windows build 651 PASS;
ServerOnly+2 clients functional 24/24, общий E2E RED по поздним событиям GhostAvatar (не precache-оракул).
658 завершён и получен: input `5c5329ec17ba`, Direct3D11, batch=false, ServerOnly, TestMap1, Ready,
8 Bots / 8 ActiveAvatar. Три снимка за 92,36 с: UsePrecaching=false, IsPrecaching=false, childCount=0,
живых precache-копий=0. За 54,35 с private memory 10472,22→10472,09 MiB, working set 4213,50→4233,00 MiB.
Это краткое PC-наблюдение, не долговременный perf-тест. Guard-log подтверждён. Cancel→Idle/CleanupPassed=true;
профиль/prefs/settings восстановлены, но edit scene Untitled→Offline (infra 7048). Аренда освобождена,
R `fa8135f83e01a89f553cb6298ad3285229572c01`, paths=[]unexpected=[].
Сводка: `../network-sync-hardening/reports/worker-658-summary.json`.
Физическая проверка host/client на Quest pending. Старый тикет 610 отменён по СТОП 6539, который снят 6805.

## Сделано

- `ecfbfdee7` (раньше `59262d43b`, изначально `f645d72f9`, база проверки — `97a320c88`): `Assets/Scripts/Network/HeadlessPrecacheGuard.cs`.
  Прогрев выключается не только в batchmode/без графики, но и у процесса «только сервер»
  (`NetworkServer.active && !NetworkClient.active`); решение — в `OnSceneLoaded` каждой сцены (роль известна после
  старта сети; сервер всегда грузит сцену карты до первого спавна тела); `RegisterAfterComposition` — то же условие.
  Регистрация выключенных UniqueId выполняется сторожем в тех же условиях. Хост и клиенты не затронуты.
  Старая перегрузка `ShouldPrecache(bool, GraphicsDeviceType)` сохранена для `HeadlessPrecacheGuardTests`.
- Историческая офлайн-компиляция сборки VrBattlegrounds (Unity csc без weaver): 459 файлов, 0 ошибок.
  После rebase не повторялась; это не текущая Unity-проверка.

## Дальше (конкретный следующий шаг)

1. Проверки 643 завершены; полные отчёты — `tasks/network-sync-hardening/reports/worker-643-*.json`.
   Windows E2E выполнен (первый RED по ghost events, повтор GREEN); server-only+8 ботов проверен в 658.
   Дальше пользовательская/Quest-приёмка и долговременная регрессия владельцев после вливания.
   Подготовленный общий пакет и пределы доказательств — [Details.md](../network-sync-hardening/Details.md).
2. Приёмка пользователя (см. ниже). Затем вливание fast-forward в origin/dev штатным путём.
3. После вливания сообщить bots (`bots-fix`, они ждут: 6330) и performance (`perf-stress-scenario`, 6332) — они
   делают регрессию: матч 8 ботов на сервере с графикой (cc-0/M8) и perf-stress с сервером с графикой.
4. Этап `regression-tests` — только после приёмки (правило: тесты, когда нет других задач).

## Оставшиеся проверки

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
  повторное полное ревью не требуется. Условие cleanup снято source-ревью 6854 на checkpoint `b00b03f33`;
  runtime-прерывание не проверено; оговорка по исчезновению аватара исправлена владельцем в `6a0fde1e0`
  (6906), точечный source ACK 6938. Сторож и патч 81 не
  конфликтуют (при UsePrecaching=false TryPrecaching выходит).

## Риски и ловушки

- Сторож решает по роли при загрузке сцены: если когда-нибудь тело заспавнят до первой загрузки сцены после старта
  сервера, прогрев успеет стартовать. Сейчас сервер всегда грузит карту до спавна.
- Хук `.claude/hooks/sdk-patch-guard.ps1` к этому коду не относится (не SDK).

## Координация

- Маршрут Claude очищен; canonical inbox — по owner network и адресу текущей сессии Codex. Ownership/worktree
  сохранены. Ожидание worker разрешено объявлением 6805.
- Вопросы пользователю — штатным инструментом вопросов; приёмка после Unity-проверки.
