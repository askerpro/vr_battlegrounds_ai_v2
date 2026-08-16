# T-22 · `ReplenishWeaponsNetwork` не мутирует префаб

| | |
|---|---|
| Находка | VR-04 (средний) |
| Блокирована | — |
| Блокирует | — |
| Уровень проверки | 3 (Multiplayer Play Mode) |
| Оценка | 1 час |

Разбор — в [`../audit/network-audit-2026-08.md`](../audit/network-audit-2026-08.md#vr-04--средний--replenishweaponsnetwork-мутирует-префаб-ассет).

## Что делать

`Assets/Scripts/Arsenal/ArsenalWallController.cs:98-110`:

```csharp
var prefab = slot.WeaponData.WeaponPrefab;
bool wasActive = prefab.activeSelf;
prefab.SetActive(false);          // ← мутация ассета, не инстанса

GameObject spawned = Instantiate(prefab);
prefab.SetActive(wasActive);      // ← и обратно
```

Цель понятна и правильная: не дать `UxrGrabbableObject.Awake()` отработать до того, как
через рефлексию отключён `_autoCreateStartAnchor` — иначе UXR создаёт лишний
«Auto Anchor», вытаскивающий оружие из слота.

Проблема в способе: `SetActive` вызывается на самом префабе. В редакторе это помечает
ассет грязным, и состояние может утечь в репозиторий. Плюс при исключении между двумя
вызовами префаб останется выключенным.

Заменить на инстанцирование под выключенным родителем:

```csharp
// один выключенный контейнер на весь метод
GameObject spawned = Instantiate(prefab, _inactiveRoot.transform);
DisableAutoAnchor(spawned);
spawned.transform.SetParent(null);   // или сразу в слот
spawned.SetActive(true);
```

Ключевое: дочерний объект выключенного родителя не получает `Awake` до активации.
Ассет при этом не трогается.

## Границы

Только способ отложить `Awake`. Логику пополнения слотов, `AssignNetworkItem`,
`NetworkServer.Spawn` и `RpcAssignItemToSlot` не менять.

Рефлексию в `DisableAutoAnchor` не переписывать — она фиксируется
в [T-21](T-21-document-sdk-reflection.md).

Состояние стены — задача [T-15](T-15-arsenal-state-replication.md).

## Как проверить

1. Прогнать раунд, дождаться пополнения арсенала. Оружие в слотах, «Auto Anchor»
   в иерархии нет.
2. В окне Project префаб оружия **не помечен как изменённый** (нет звёздочки,
   `git status` чист).
3. Multiplayer Play Mode: у второго игрока оружие в слотах на своих местах.

Пункт 2 — главный, ради него всё и делается.

## Готово, когда

- Префаб не мутируется, `git status` после прогона чист.
- «Auto Anchor» не появляется, оружие в слотах на местах у всех игроков.
