# T-26 · Харнесс сетевых тестов в одном процессе (ярусы A и B)

| | |
|---|---|
| Находка | — (инфраструктура) |
| Блокирована | — |
| Блокирует | T-02, T-04, T-11 (их проверку) |
| Уровень проверки | сам является инфраструктурой |
| Оценка | полдня |

Обоснование и рецепт — в [`../testing.md`](../testing.md#как-тестировать-сетевую-логику).

## Зачем

Почти вся серверная логика помечена `[Server]`, а Mirror вне активного сервера такие
методы молча заглушает. Обычный юнит-тест вызывает метод, тот ничего не делает, тест
зеленеет и **не проверяет ничего** — худший вид ложной уверенности.

Проверено экспериментально: `EliminationMode.Initialize` вне сервера оставляет
`TeamStates.Count == 0`.

## Что делать

Базовый класс для сетевых тестов в `Assets/Tests/EditMode/` (или отдельная папка
`Network/`), поднимающий Mirror без сокета.

Рабочий рецепт (проверен по исходникам Mirror, `NetworkServer.cs:74, 141, 173`):

```csharp
// Ярус A — сервер без сети
_transportObject = new GameObject("TestTransport");
Transport.active = _transportObject.AddComponent<KcpTransport>();
NetworkServer.listen = false;   // Listen() пропустит ServerStart(), сокет не откроется
NetworkServer.Listen(4);        // NetworkServer.active == true

// Ярус B — плюс локальный клиент
NetworkClient.ConnectHost();
```

Разбор:

- `NetworkServer.listen` — `public static bool`, при `false` в `Listen()` не вызывается
  `Transport.active.ServerStart()`;
- транспорт всё равно нужен как объект: `Initialize()` делает
  `Debug.Assert(Transport.active != null)` и затем подписывается на его события
  в `AddTransportHandlers()`;
- `NetworkClient.ConnectHost()` использует `LocalConnectionToClient` /
  `LocalConnectionToServer` — сообщения идут напрямую, без сокета.

Порядок в `TearDown` важен, иначе Mirror оставит статики грязными и следующий тест
поведёт себя иначе:

```csharp
NetworkClient.Shutdown();
NetworkServer.Shutdown();
Transport.active = null;
Object.DestroyImmediate(_transportObject);
```

Отдельно предусмотреть: сброс статических полей проекта между тестами
(`PlayerSession.LocalSession`, синглтоны менеджеров) — иначе тесты потекут друг в друга.

## Первые тесты на новом харнессе

| Тест | Ярус | Закрывает |
|---|---|---|
| `Initialize_заполняет_TeamStates` | A | доказывает, что харнесс работает — сейчас `Count == 0` |
| `Победа_в_сете_даёт_одно_очко` | A | T-02, недостижимо без сервера |
| `SyncVar_команды_долетает_до_клиента` | B | базовая проверка репликации |
| `Переподключение_восстанавливает_позицию` | B | T-04 |
| `ActiveAvatar_виден_клиенту` | B | T-11 |

Первый тест обязателен и должен идти первым: он проверяет сам харнесс. Если
`TeamStates` не заполнился — тесты на этом харнессе ничего не значат.

## Границы

Только харнесс и перечисленные тесты. Игровой код не менять: задача — научиться
проверять, а не чинить. Найденные по ходу дефекты записывать в аудит как новые находки,
а не править здесь.

Ярус C (два процесса) — отдельная задача [T-27](T-27-two-process-e2e.md).

## Как проверить

```
execute_code: VrBattlegrounds.EditorTools.AndroidCompileGate.Run()   → PASS
run_tests(mode="EditMode", assembly_names=[...]) → get_test_job(..., wait_timeout=60)
```

`Initialize_заполняет_TeamStates` зелёный = харнесс живой.

## Готово, когда

- Базовый класс поднимает и корректно гасит Mirror между тестами.
- `Initialize_заполняет_TeamStates` зелёный.
- Рецепт и ограничения дописаны в [`../testing.md`](../testing.md).
