# T-04 · `session.ActiveAvatar` вместо `conn.identity`

| | |
|---|---|
| Находка | NET-05 (высокий) |
| Блокирована | — |
| Блокирует | T-11 |
| Уровень проверки | 2 (PlayMode, хост в процессе) |
| Оценка | 15 мин |

> **Временная правка.** Окончательно связь сессия ↔ аватар переделывает
> [T-11](T-11-session-avatar-link.md). Делается сейчас, потому что оживляет уже написанный,
> но мёртвый код восстановления сессии.

Разбор — в [`../audit/network-audit-2026-08.md`](../audit/network-audit-2026-08.md#net-05--высокий--восстановление-сессии-не-сохраняет-здоровье-и-позицию).

## Что делать

`Assets/Scripts/Managers/PlayersManager.cs:140`, метод `UnregisterSession`:

```csharp
// сейчас — conn.identity это PlayerSession, а не аватар, поэтому всегда null
PlayerController avatar = (conn.identity != null) ? conn.identity.GetComponent<PlayerController>() : null;
```

Заменить на `session.ActiveAvatar` (поле уже заполняется на сервере в
`AvatarManager.SpawnAvatar` и `ChangeAvatar`).

## Почему это важно

Объектом игрока для Mirror назначен `PlayerSession`
(`NetworkServer.AddPlayerForConnection(conn, sessionGO)`), а `PlayerController` — отдельный
заспавненный объект. Из-за `null` в `SessionRecoveryManager.SaveDisconnectedSession`
не выполняется ветка сохранения физического состояния: `Health`, `Position`, `Rotation`
остаются нулями, `NeedsPhysicalRestore` — всегда `false`. Весь код восстановления позиции
в `AvatarManager.SpawnAvatar` (строки 72–81, 92–95) сейчас недостижим.

## Границы

Только эта строка в `PlayersManager`. Аналогичная ошибка в `TeamSpawnZone.cs:133` —
задача [T-11](T-11-session-avatar-link.md), туда не лезть: там нужен клиентский путь,
которого пока нет.

## Как проверить

PlayMode-тест или ручной прогон на хосте:

1. Подключиться, дождаться спавна аватара, отойти от точки спавна, получить урон.
2. Отключиться и переподключиться с тем же `deviceToken`.
3. Игрок должен появиться на том же месте с тем же здоровьем.

В логах категории `Player`: `[SessionRecoveryManager] Saved session for ...`, затем
`Restoring session for ...`.

## Готово, когда

- `SaveDisconnectedSession` получает непустой аватар.
- После переподключения восстанавливаются позиция и здоровье, а не только счёт и команда.
