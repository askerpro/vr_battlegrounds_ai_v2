# Unity MCP: версия, локальный патч и проверки

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

1. Получить Unity lock по правилам общего редактора.
2. Если embedded-папка уже существует, сохранить её локальные изменения отдельно;
   установщик намеренно отказывается её перезаписывать.
3. Выполнить PowerShell:

   ```powershell
   ./Tools/UnityMcp/Install-Upstream.ps1 -LockOwner <владелец>
   ```

   Для установки без сети передать `-ArchivePath <zip>` с архивом закреплённого upstream.
   Проверяются версия и SHA256 трёх исходников; git apply сначала проверяется на staging-копии.
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
