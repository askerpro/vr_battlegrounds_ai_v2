# lobby-station-validator — валидатор иерархии: в Lobby одна станция

Обновлено 2026-10-24. Владелец задачи — owner `level-design` (Claude), основной checkout по указанию
пользователя, ветка `task/lobby-station-validator` от `origin/dev` 2648853.

## Цель

`Tools/VR Battlegrounds/Gameplay/Validate Map Hierarchy` (`MapGameplayHierarchy.ValidateAll`) падал на Lobby:
«неверное количество зон/станций: 1/1» — ожидал 2 станции. По решению пользователя 2026-10-11 в Lobby
одна станция у западной стены навеса (так уже проверяют `LobbyLayoutTests` и `ArsenalMapGeometryTests`;
пользователь подтвердил 2026-10-24: в лобби планируется только одна станция).

## Сделано

- `MapGameplayHierarchy`: ожидание для Lobby — 1 зона / 1 станция; сообщение об ошибке показывает ожидаемые числа.
- `Docs/level-design/level-design.md`: «одна нейтральная зона и одна станция»; `Docs/troubleshooting.md`:
  шаг обновления — станция лобби (вместо четырёх).

## Статус

- [x] Компиляция; `ValidateAll` в Editor: PASS, Environment и 6 сцен.
- [ ] Проверка пользователем (меню Validate Map Hierarchy → PASS в консоли), коммит, вливание.
