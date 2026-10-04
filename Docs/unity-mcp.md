# Unity MCP: версия, локальный патч и проверки

## Ограничение диагностического вывода

Цель — сохранять полную диагностику, передавая агенту только ограниченную сводку.
В MCP 10.2.0 установлен патч `execute_code`: возвращаемый результат, чей JSON превышает
6000 символов, автоматически сохраняется целиком в `tmp/mcp-reports/execute-code/<id>.json`.
В `data.result` клиент получает `__mcp_output` (truncated, reportId, reportPath,
originalCharacters) и `summary`, суммарно не более 2400 символов JSON. Маленькие ответы
сохраняют прежний тип и структуру. Это работает и при replay исходного кода.

При массиве `failures` сохраняются общий `failureCount`, число возвращённых записей
`failureEntries`, до 5 примеров и группировка. Если явный общий счётчик больше массива,
массив считается выборкой и `failureGroupsScope=sample`; иначе — complete. Непустые
ошибки или положительный общий счётчик задают `summary.passed=false`. Отсутствие ошибок
в выборке не превращает положительный общий счётчик в ноль. Внешнее `success` описывает
выполнение инструмента. При отказе сохранения возвращается ошибка с
`data.executionCompleted=true`: код уже выполнился, повторять его нельзя.

Детали читать коротким новым вызовом, без повторения исходной операции:

```csharp
return MCPForUnity.Editor.Helpers.ExecuteCodeOutputGuard.ReadReport(
    "<reportId>", "failures", 10, 5);
```

Принимается только 32-символьный ID отчёта. `selector` — JSONPath к одному значению,
пустой — корень; `offset` — неотрицательная позиция. Массивы: до 10 элементов, строки:
до 1200 единиц UTF-16; JSON ответа — до 2400 символов. Страница может быть уменьшена
ради бюджета. `nextOffset` — продолжение, `null` — конец. `itemsTruncated` сообщает
о сокращении вложенных элементов: запрашивать их более узким selector. Объекты читаются
как сводки. Полный отчёт не выводить обратно в контекст целиком.

Граница патча — возвращаемые значения execute_code. Ошибки компилятора, Console,
изображения и остальные MCP-инструменты им не ограничиваются. Сериализация полного
результата происходит в памяти, отчёты занимают место на диске; автоудаления нет.
Схема Python-инструмента и каталог tools/resources не меняются.

Дополнительная защита: `ReportSummary.cs.txt` сокращает результат в самой проверке,
`compact-result.js` убирает дублирование MCP-envelope и ограничивает сводку 6000 символами.
В `.codex/config.toml` задан резервный `tool_output_token_limit = 4000`.
Шаблоны вызовов — [правила Unity MCP](../.agents/rules/unity_mcp.md#защита-контекста-от-массовых-результатов).

Проверки вне Unity:

```powershell
node Tools/UnityMcp/Tests/compact-result.test.cjs
node Tools/UnityMcp/Tests/report-summary.test.cjs
python Tools/UnityMcp/Tests/config-output-limit.test.py
node Tools/UnityMcp/Tests/output-guard.test.cjs Packages/com.coplaydev.unity-mcp/Editor/Tools/ExecuteCode.cs
python Tools/UnityMcp/Tests/output-installer.test.py
python Tools/UnityMcp/Tests/output-install-fresh.test.py <upstream-10.2.0.zip>
```

Первый стенд работает на Node без зависимостей; для второго нужны .NET 9 и восстановленный
пакет Newtonsoft.Json в Library/PackageCache. NuGet-источники отключены; Unity не запускается.
Третий стенд проверяет TOML и корневой параметр лимита (Python 3.11+), без запуска агента.
Для проверки существующего отчёта передать его путь первым стендом как аргумент.

Проверено 2026-10-04: внешний стенд ограничителя и настоящего InvokeCompiled — 28 assertions;
установщик — отказ частичной записи, проверяемое продолжение, повтор и отказ перезаписи
чужих изменений. Свежая offline-установка обоих патчей из закреплённого ZIP прошла в
Windows PowerShell 5.1; распаковывается только MCPForUnity, выход пути ZIP за пределы пакета отклоняется;
discovery-стенд — 8/8. Корневой TOML и клиентская сводка тоже проверены. Полный отчёт с 8880
ошибками занимает 310857 символов, сводка нативного ограничителя — 769, клиентской
обёртки — 689. В живом вызове без клиентского фильтра весь MCP-ответ — 1888 символов;
все 8880 записей сохранены в файле, страница 10–14 и nextOffset=15 корректны.
Пустая выборка с явным failureCount=8880 сохраняет общий счётчик и passed=false.
Живая проверка return 42 прошла;
AndroidCompileGate — PASS, ошибок в консоли — 0.

Патч установлен в текущий редактор: уже работающие агенты получают ограниченные
результаты execute_code без перечитывания конфигурации Codex. Резервный клиентский
лимит требует загрузки конфигурации новой сессией; его действие на все desktop-обёртки
не подтверждено. Явный бюджет functions.exec=2000 остаётся обязательным.
Старые большие ответы из истории автоматически не исчезают.

Параметр бюджета истории описан в [официальном справочнике Codex](https://learn.chatgpt.com/docs/config-file/config-reference).

## Текущее состояние

С 2026-10-03 используется embedded MCP **10.2.0** из закреплённого upstream
`30d22075093d1d35dfb0091c1c7550e9ad948577`. В stable уже переведён на TypeCache
CommandRegistry; в ToolDiscoveryService перенесено удаление полного AppDomain-обхода
из beta `3bb0acea8a23d6592f60272cc1fd179c095032d1`. Остальные beta-изменения не переносились.

Локальный патч дополнительно согласует порядок дубликатов по FullName/Assembly.FullName
у реестра и метаданных и изолирует исключение при создании пользовательского атрибута.
В editor asmdef удалены ссылки на отсутствующие Roslyn DLL; upstream CodeDom с response-file
сохранён и проверен через execute_code. Пакеты игровых SDK не менялись.

Каталог до/после: **35 tools, 19 resources**, все метаданные и параметры совпадают.
Проверены настоящий пользовательский sync/async handler, ресурс и вложенный
integer-параметр в Assembly-CSharp-Editor. Внешний стенд: **8/8**, AndroidCompileGate: **PASS**.
Полные границы и численные результаты — [аудит перезагрузки](audit/editor-reload-audit-2026-10-03.md).

## Установка на другой машине

Embedded-папка `Packages/com.coplaydev.unity-mcp/` игнорируется Git. Исходники патча,
установщик и проверки сохраняются в `Tools/UnityMcp/`. Перед открытием Unity на новой
машине восстановить embedded-папку: packages-lock содержит source=embedded.

Для существующего MCP 10.2.0 автоматический ограничитель устанавливается под Unity lock:
`./Tools/UnityMcp/Apply-OutputGuard.ps1 -LockOwner <владелец>`, затем Refresh и live probe.
Установщик допускает проверяемое продолжение частичной установки и повтор готовой;
неизвестные изменения ExecuteCode/helper/meta не перезаписывает. Сравнение исходников
нормализует LF/CRLF, чтобы форма патча после Git checkout не ломала проверку.

1. Получить Unity lock по правилам общего редактора.
2. Если embedded-папка уже существует, сохранить её локальные изменения отдельно;
   установщик намеренно отказывается её перезаписывать.
3. Выполнить PowerShell:

   ```powershell
   ./Tools/UnityMcp/Install-Upstream.ps1 -LockOwner <владелец>
   ```

   Для установки без сети передать `-ArchivePath <zip>` с архивом закреплённого upstream.
   Распаковывается только MCPForUnity: длинные пути upstream TestProjects не мешают PowerShell 5.1.
   Проверяются версия и SHA256 четырёх исходников; discovery и output-guard патчи сначала
   проверяются на staging-копии. OutputGuard и его стабильный meta копируются из Tools/UnityMcp/OutputGuard.
4. Открыть Unity либо выполнить Unity Refresh под тем же lock. Установщик сам не вызывает
   Refresh и не переписывает manifest/lock. Проверить resolvedPath, версию 10.2.0 и отсутствие
   ошибок C#; затем execute_code `return 42;` и AndroidCompileGate.
5. Внешний стенд (нужен .NET 9):

   ```powershell
   dotnet restore Tools/UnityMcp/Tests/DiscoveryHarness.csproj --configfile Tools/UnityMcp/Tests/NuGet.Config
   dotnet run --no-restore --project Tools/UnityMcp/Tests/DiscoveryHarness.csproj
   ```

   Стенд компилирует реальные исходники embedded-пакета со stub окружением. Он проверяет
   sync/async/resource handlers, параметры, дубликаты (включая одинаковый FullName в разных
   assemblies), Rescan, import worker и изоляцию повреждённого атрибута. Проверку Unity
   TypeCache и восстановления транспорта этот стенд не заменяет.

В `Tests/UnityProbe.cs.txt` находится временная проверка настоящей Unity assembly:
импортировать под lock в `Assets/Editor/VR_Battlegrounds/Debug/`, вызвать её handlers,
после проверки убрать только собственный probe и его EditorPrefs-ключи. Постоянный
probe в проекте не нужен. `MeasureUnity.cs.txt` измеряет discovery без бинарного Profiler.
`ReadReloadTrackers.cs.txt` читает встроенные таймеры реальных callbacks без их замены.

## Отдельная задержка встроенного UVCS

В контрольных reload выявлен 10-секундный таймаут PlasticApp.BeforeAssemblyReload:
ThreadWaiters=1, InUseConnections=0, UVCSOperations=False. MCP teardown занимал около 4 мс.
Встроенная интеграция UVCS выключена штатным переключателем для этого проекта через
EditorPrefs этой машины. Пакет collab-proxy не удалён. Git и внешний cm не зависят от
этого переключателя. Включение обратно — штатная настройка Unity Version Control;
после включения повторить обычный замер. Подробнее — [Git/Plastic](version-control.md).
`Tests/DisableUvcs.cs.txt` содержит воспроизводимый вызов этого переключателя с проверкой
отсутствия активной UVCS-операции. На других машинах применять при подтверждённом таймауте.

## Старые версии: execute_code fix на Windows

## Что это и когда нужно

Этот документ описывает локальный фикс для инструмента Unity MCP `execute_code`,
который может падать на Windows из-за слишком длинной командной строки при компиляции через CodeDom.

Используйте этот документ, если вызов `execute_code` возвращает ошибку вида:

```text
Execution failed: Error running mono.exe: Имя файла или его расширение имеет слишком большую длину.
```

## Причина

`CSharpCodeProvider` (CodeDom) добавляет все referenced assemblies в аргументы запуска `mono.exe`.
В Unity-проектах это может быть 100+ путей, и итоговая длина аргументов превышает лимит Windows.

### Где уже зафиксировано внутри проекта

Техническая версия этой же информации уже есть в AI-инструкции:
- `.agents/rules/unity_mcp.md`

Этот файл в `Docs/` нужен как человеческая документация для разработчиков, не только для агента.

### Пошаговый фикс для старого пакета

1. Embed пакет MCP в проект

```text
manage_packages -> action: embed_package, package: com.coplaydev.unity-mcp
```

После embed пакет становится редактируемым в:
- `Packages/com.coplaydev.unity-mcp/`

2. Пропатчить файл

Файл:
- `Packages/com.coplaydev.unity-mcp/Editor/Tools/ExecuteCode.cs`

В методе `CodeDomCompile()` перевести передачу ссылок на сборки с
`ReferencedAssemblies.Add(...)` на response-file (`.rsp`) через `CompilerOptions`.

Суть:
- записать все `/reference:"..."` в временный `.rsp` файл;
- передать компилятору `@<rsp-file>`;
- после компиляции удалить `.rsp`.

3. Перекомпилировать Unity

```text
refresh_unity -> compile: request, mode: force, scope: all
```

4. Проверить результат

```text
execute_code -> action: execute, code: return 42;
```

Ожидается `success: true`.

## Автосохранение перед тестами

В установленном MCP пакете TestRunnerService.RunTestsAsync вызывает
SaveDirtyScenesIfNeeded для обоих режимов, включая EditMode. Каждая открытая
изменённая сцена с путём сохраняется через EditorSceneManager.SaveScene.
Самостоятельного вызова SaveScene в коде проверки для этого не требуется.

Перед run_tests проверять scene.isDirty у всех открытых сцен. Если сохранение
пользовательской сцены не входит в задачу, этот инструмент на изменённой сцене
не запускать; использовать изолированную проверку. Не сбрасывать dirty-флаг
и не закрывать сцену ради обхода сохранения: так можно потерять изменения.
Не выдавать прогон run_tests за проверку без записи сцены.

- Response-file уже есть в upstream 10.2.0; повторный ручной патч ExecuteCode.cs не нужен.
- Это локальный фикс среды разработки для старых версий.
- Папка `Packages/com.coplaydev.unity-mcp/` обычно игнорируется в Git (`.gitignore`),
  поэтому патч может не распространяться автоматически на других разработчиков.
- После обновления/переустановки пакета патч может слететь.

## Как новому разработчику быстро проверить, нужен ли фикс

1. Вызвать `execute_code` с простым выражением (`return 42;`).
2. Если есть ошибка про длинное имя файла/расширение, применить шаги фикса выше.
3. Если `success: true`, ничего делать не нужно.

## Диагностика, если не помогло

- Убедиться, что редактировался именно embed-пакет в `Packages/com.coplaydev.unity-mcp/`.
- Проверить, что Unity завершила рекомпиляцию без ошибок.
- Перезапустить Unity MCP сервер и повторить тест `execute_code`.
