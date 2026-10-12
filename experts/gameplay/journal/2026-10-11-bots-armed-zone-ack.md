# bots-fix/armed-development: zone ACK только на технический прогон, на вливание — нет

Дата: 2026-10-11. Запрос: inbox #3390 (bots → gameplay), scope_digest `9dac00ef…8641`.
Решение пользователя: ответить в рекомендованном виде.

- Zone ACK выдан на DEVELOPMENT/технический Unity-прогон в изолированном worker (событие #3498).
- Ответ с условиями до вливания отправлен bots-fix (событие #3499). Native-адрес сессии bots
  не определён — доставка через человека: task-id `bots-fix`, event_id 3499.
- Ревью сделано по рабочей копии `F:/CodexWorktrees/bots/Vr_Battlegrounds_ai`, HEAD 57898794c,
  незакоммиченный diff +119/−10 в PlayersManager, AvatarManager, AvatarTeardown, PlayerSession.

Условия до вливания (повторное gameplay-ревью по итоговому diff):
1. Ранний return по `!NetworkServer.active` в `AvatarTeardown.ReleaseBeforeDestroy/ConfiscateBeforeDestroy`
   ломает EditMode `AvatarTeardownTests.cs:74,101`.
2. В `ReplaceBody` освобождение старого тела перенесено до Spawn нового — затрагивает людей
   (смена скина/команды, смерть): ограничить ботами или доказать эквивалентность в Unity.
3. Спавн (gameplay) не должен зависеть от каталога ботов (`BotCombatAssets`, `BotClipBody`,
   `BotEquipmentLifecycle`, `Resources.Load` на спавне): резолвер NPC-тела — интерфейс на стороне bots,
   сигнатура контрактом. Приоритет призрака выбывшего остаётся за gameplay.
4. SyncVar `_botControlled`/`BotAvatarSource` в PlayerSession — сетевая часть, нужна зона network
   (owner-задачи нет → zone-override по решению пользователя). Смысл флага gameplay принят.
5. Мелочи: лишний `if (newPc != null)`; `CreateBotSession` может вернуть null.

## Продолжение 2026-10-12: проект «бот = обычный аватар» (4194 → ответ 4204)

bots-fix по решению пользователя переходит на штатный поток игрока (`tasks/bots-fix/player-flow-plan.md`).
Позиция gameplay: поддержано. Бот-ветки в origin/dev нет — её не вливать (не «откат»). Тело бота —
TeamAvatarStrategy по TeamIndex/AvatarIndex, `PlayerSession.BotAvatarSource` убрать; `IsBotControlled`
как SyncVar — только если нужен клиентам (решает network). Хитбоксы/смерть/сброс — путь игрока;
калибровка — `ServerAcceptCalibration(..., Connect)` в `CreateBotSession` до Spawn; покупки — штатная экономика.
Этап 0 — переключатель режима тела только в конфиге стенда; zone-ack — по фактическому diff.

Ответ bots 4210: условия приняты. Новая модель строится от origin/dev; клиповый путь (BotAvatarSource,
_botControlled, ветки AvatarManager/PlayersManager/AvatarTeardown) в dev не вливается, остаётся архивом ветки.
Признак бота — серверный `BotDirector.IsBot`, тело — TeamAvatarStrategy/AvatarIndex. Жду zone-request этапа 0.

Также в разборе инбокса: #2633/#2767 (Binding PPK/AK105) закрыты без gameplay — авторинг взял
weapon-system (#2776, #2826, #3167). Удаление старых Docs/tasks арсенала (#1140) — в backlog п.11.
