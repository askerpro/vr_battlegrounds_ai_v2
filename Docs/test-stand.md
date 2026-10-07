# Адресный стенд Play Mode

Первый этап автоматизации: агент запускает выделенный сервер и два клиента через существующий
Play Mode Scenarios, получает адреса реальных процессов и исполняет ограниченные команды
в выбранном экземпляре. Сеть запускает существующий `GameNetworkDiscovery` по тегам сценария.

## Запуск из Unity MCP

В linked worktree использовать собственную аренду редакторского worker через брокер.
Вызовы ниже выполняются через штатный `execute_code`; результат — JSON-строка.

```csharp
return VrBattlegrounds.EditorTools.TestStand.PlayModeTestStand.StartProbe();
```

Запуск копирует `Server+client` во временный сценарий с двумя клиентами и начальной сценой Offline.
Исходный сценарий и ассеты не сохраняются с изменениями. `Status()` возвращает участников с
`RunId`, `ParticipantId`, `ProcessSessionId`, PID, тегами, фактическими флагами Mirror и сценой.
Готовность сети означает один ServerOnly и два клиента с `Connected=true`.

```csharp
return VrBattlegrounds.EditorTools.TestStand.PlayModeTestStand.Status();
```

Для команды взять адрес нужного участника из текущего статуса:

```csharp
var request = new VrBattlegrounds.EditorTools.TestStand.StandRequest {
    RunId = "<RunId>", ParticipantId = "Player 2", ProcessSessionId = "<ProcessSessionId>",
    RequestId = "marker-1", Action = "create-marker", MarkerName = "probe", TimeBudgetMs = 5000
};
return VrBattlegrounds.EditorTools.TestStand.PlayModeTestStand.Send(request);
```

Доступны `create-marker`, `read-marker`, `remove-marker`, `state` (для `state` MarkerName пустой).
Маркер — локальный несетевой GameObject без физики. Проверка изоляции читает тот же MarkerName
у каждого участника и требует, чтобы он существовал только у цели.

`Send` возвращает операцию. Пока `Completed=false`, опрашивать `Operation(RequestId)`.
Новый RequestId обязателен для нового действия; повтор с тем же содержимым возвращает прежний
результат. Изменённое содержимое под прежним RequestId отклоняется. Ответ включает адрес
и PID фактического исполнителя. Команды идут через Unity MPE IPC; отдельный MCP-сервер клиента
не требуется. Устаревшая сессия или запуск не получают команды.

`EffectUnknown=true` означает, что эффект мог произойти, но его подтверждение не получено.
Не повторять такое действие с новым RequestId без проверки состояния. Бюджет ожидания
не прерывает уже выполняющийся код Unity. Кэши ограничены и не вытесняют выполненные запросы.
Перезагрузка домена меняет ProcessSessionId; адрес нужно получить заново.

```csharp
return VrBattlegrounds.EditorTools.TestStand.PlayModeTestStand.Operation("marker-1");
// По завершении своего прогона:
return VrBattlegrounds.EditorTools.TestStand.PlayModeTestStand.Stop();
```

После Stop дождаться `Phase=idle`, `Playing=false` и проверить `CleanupPassed`.
Не освобождать аренду во время продолжающейся операции Play Mode, компиляции или импорта.

## Временные настройки

Участники получают VR / Player / Admin=false через единственную IDisposable-область
`LocalClientProfile.BeginTemporaryOverride`. Она блокирует отладочный override и восстанавливает
предыдущие nullable-поля. `ClientDeviceIdentity` обеспечивает отдельный токен каждого участника
без записи тестового значения в PlayerPrefs. Обычное подключение сохраняет прежний fallback
и Editor-суффикс. CLI E2E использует ту же временную идентичность.

На время прогона сохраняются и восстанавливаются Debug Bootstrap Enabled / HostIsAdmin,
настройка паузы XR без фокуса и предыдущий Play Mode сценарий. Порты транспорта и Discovery
меняются только у runtime-компонентов. Descriptor находится в `Temp/VRBattlegroundsTestStand`.
Остановка отзывает команды, удаляет свои маркеры и освобождает области; общий IPC-сервис не закрывается.
При аварийном завершении координатора личные EditorPrefs могут требовать восстановления;
это ограничение пилота нужно учитывать при дальнейшей реализации recovery.

## Проверки

```powershell
dotnet run --project Tools/TestStand/ProtocolHarness.csproj
dotnet build Tools/TestStand/EditorBinding.csproj
# Только в собственной RUNNING-аренде после guard, из своего worktree:
uv run --with 'mcp>=1.20,<2' Tools/TestStand/worker_probe.py --instance '<worker>' --editor-root '<worker-root>' --output tmp/test-stand/live.json
```

ProtocolHarness выполняет настоящий код адресного gate, областей профиля/токена и E2E-вердикта (22 проверки); Unity-порты
там заменены минимальными заглушками. EditorBinding компилирует Editor-код с установленными
managed DLL Unity; путь SDK задаётся `-p:UnityManaged=...`. Эти проверки не доказывают IPC и сеть.
`worker_probe.py` запускает штатный прокси из своего worktree, проверяет фактическую цель,
AndroidCompileGate и два запуска при выключенном/включённом Debug Bootstrap. Полный отчёт сохраняется
в JSON. Живой результат и недоказанные критерии записываются в плане реализации.

2026-10-07: worker ticket 147, Android PASS, 53/53 живых утверждения, 22/22 локальных проверки.
Подтверждены Server+2clients, адресные маркеры, повторный запуск, отказы адресов, очистка,
восстановление личных настроек и обычный Play до/после. Input checkpoint: `8ed6484a`.
Проверены настройки domain reload None / DisableDomainReload; фактический reload main
не доказан (ProcessSessionId не изменился). Forced reload, смерть процесса и потеря IPC
остаются отдельными проверками. Тесты обрыва/таймаута здесь относятся к протоколу и вердикту.

## Следующие этапы

1. Добавить адресные disconnect/reconnect через единственного владельца сетевого lifecycle,
   ожидание восстановления PlayerSession, сохранённого места, authority и количества игроков.
2. Добавить XR-input adapter с одним владельцем позы/кнопок; совместимость Meta Simulator
   исследовать отдельно с текущим Oculus backend проекта. Не подключать второй писатель поверх UltimateXR.
3. Проверять UI реальным указателем/нажатием, хват — через штатный ввод и UltimateXR,
   а не прямым вызовом результата игрового действия.
4. Сетевые fault-сценарии различать: штатное отключение, пропажа пакетов, задержка, смерть сервера,
   перезапуск с новым адресом. Сохранение сессии и восстановление транспорта — разные критерии.

Маркерный пилот не устанавливает качество хвата, ходьбы, stereo XR, Quest или устойчивость
переподключения. Серверный main позволяет отключать выбранного клиента без остановки сервера.
