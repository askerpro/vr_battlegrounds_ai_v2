# Unity MCP — Правила и Известные Проблемы

> Редактор общий для нескольких агентов и человека: эксклюзивные операции — под замком
> `Tools/agents/unity-lock.sh`, правила — [`unity_sharing.md`](unity_sharing.md).

Версии на 2026-08-16: пакет `com.coplaydev.unity-mcp` **10.1.2** (источник — Git, ветка `main`,
живёт в `Library/PackageCache/`), python-сервер `mcpforunityserver` **10.1.2**.

## Транспорт: клиент и Unity должны сходиться в одной точке

### Симптом
Инструменты `mcp__unityMCP__*` падают с `No Unity Editor instances found`, при этом Unity
запущен, а в его консоли бридж рапортует `Connection verification successful`.
Ресурс `mcpforunity://instances` возвращает `instance_count: 0`.

### Причина
«Транспорт» — это два независимых звена, и настраиваются они в разных местах:

```
Claude Code ──[stdio | http]── сервер mcp-for-unity ──[TCP 6400+ | WebSocket 8080]── Unity
             ↑ .mcp.json                              ↑ окно MCP for Unity в редакторе
```

Если в редакторе выбран WebSocket-режим, Unity подключается к HTTP-хабу, который поднимает
сам редактор (`--transport http --http-url http://127.0.0.1:8080`, pid лежит в
`Library/MCPForUnity/RunState/mcp_http_<порт>.pid`). А `.mcp.json` со `stdio` порождает
**второй, отдельный** серверный процесс, который ищет Unity сканом старых TCP-портов, не
находит и отдаёт `0 instances`. Два сервера, между ними ничего.

### Диагностика
```powershell
# 1. Хаб жив и на каком порту
Get-ChildItem "$PWD\Library\MCPForUnity\RunState"        # имя pid-файла содержит порт
Invoke-WebRequest http://127.0.0.1:8080/health -UseBasicParsing

# 2. Кто слушает порт и с какими аргументами запущен
Get-NetTCPConnection -State Listen -LocalPort 8080 | Select-Object OwningProcess
Get-CimInstance Win32_Process -Filter "ProcessId=<pid>" | Select-Object CommandLine

# 3. Что делает наш серверный процесс
Get-Content "$env:LOCALAPPDATA\UnityMCP\Logs\unity_mcp_server.log" -Tail 30

# 4. Каким транспортом видит себя бридж внутри Unity
Select-String "$env:LOCALAPPDATA\Unity\Editor\Editor.log" -Pattern 'MCP-FOR-UNITY'
```

### Рабочая конфигурация проекта
`.mcp.json` цепляется к уже поднятому хабу, своего сервера не плодит:

```json
{
  "mcpServers": {
    "unityMCP": { "type": "http", "url": "http://127.0.0.1:8080/mcp" }
  }
}
```

Почему http, а не stdio: разницы в скорости нет (всё через loopback, любой вызов упирается
в главный поток Unity), зато один общий сервер на всех клиентов, штатное переживание
доменного релоада (`[HTTP Reload] Resume succeeded`) и включённые project-scoped tools
(`execute_custom_tool`), которых в stdio-режиме не было.

Плата — порт прибит в конфиге. Если Unity поднимет хаб на другом порту, поправить `url`
по имени pid-файла в `RunState/`. После правки `.mcp.json` — реконнект (`/mcp`).

## Скилл `unity-mcp-skill`

Лежит в `C:\Users\asker\.claude\skills\unity-mcp-skill\`, ставится **синхронизацией самого
плагина** (маркер `.unity-mcp-skill-sync`). Править его файлы бессмысленно — затрёт при
следующем обновлении. Проектные правила писать только сюда, в репозиторий.

- Вызывается по имени папки — `unity-mcp-skill`. Во frontmatter стоит другое имя
  (`unity-mcp-orchestrator`), оно не используется.
- Когда звать: автоматизация редактора — GameObject, компоненты, сцены, префабы, скриншоты,
  тесты. Один раз в начале такой задачи. Для чтения консоли и ошибок компиляции достаточно
  `/unity-check`.
- Что берёт на себя: схемы инструментов, `batch_execute` (лимит 25 команд, значение видно
  в `mcpforunity://editor/state` → `batch_execute_max_commands`), скриншоты с
  `include_image=true` и `capture_source="scene_view"`, поллинг `run_tests` → `get_test_job`,
  восстановление после `stale_file` через `get_sha`.
- Чего он не знает: правил проекта. `GameLog`, `MapLoader.LoadMap`, запрет `Editor/` внутри
  `Assets/Scripts/` — всё это только в `CLAUDE.md`, скилл её не заменяет.
- ⚠️ Не выполнять его совет про `manage_editor(action="deploy_package"/"restore_package")` —
  это перезапишет установленный пакет MCP вместе с любыми локальными патчами.
- В поставке битые ссылки на `references/resources-reference.md` и
  `references/probuilder-guide.md` — синк их не кладёт. Реально есть только
  `tools-reference.md` и `workflows.md`.

## execute_code — MAX_PATH (историческое, только 9.x)

**В 10.1.2 не воспроизводится.** Проверка:

```
execute_code → action: execute, code: return 42;
```
Сейчас отвечает `{"result": 42, "compiler": "codedom"}` — на CodeDom, без всякого патча.
Пакет при этом обычный, из `Library/PackageCache/`; embedded-копии в `Packages/` нет и не нужно.

Ниже — рецепт для 9.x. Применять, **только** если проект откатили на старую версию и
`execute_code` снова падает с `Имя файла или его расширение имеет слишком большую длину`.
Цена вопроса — embed пакета, то есть заморозка его версии.

### Причина (9.x)
`CSharpCodeProvider` (CodeDom) передавал **все** referenced assemblies (~100+) аргументами
командной строки `mono.exe`. Суммарная длина путей вида
`C:\Program Files\Unity\Hub\Editor\6000.x.xf1\Editor\Data\...` превышала лимит Windows (~32 КБ).

### Исправление (Embed + Patch)

**Шаг 1 — Embed пакет MCP:**
```
manage_packages → action: embed_package, package: com.coplaydev.unity-mcp
```
Пакет скопируется в `Packages/com.coplaydev.unity-mcp/` и станет редактируемым.

> ⚠️ Папка `Packages/com.coplaydev.unity-mcp/` добавлена в `.gitignore` — это чисто локальный фикс.

**Шаг 2 — Пропатчить `ExecuteCode.cs`:**

Файл: `Packages/com.coplaydev.unity-mcp/Editor/Tools/ExecuteCode.cs`

В методе `CodeDomCompile()` заменить блок, где assembly references добавляются через `ReferencedAssemblies.Add()`,
на **response-file подход** — записать пути в `.rsp` файл и передать через `CompilerOptions`:

```csharp
private static Assembly CodeDomCompile(string source, string[] assemblyPaths, out List<string> errors)
{
    errors = new List<string>();
    var filtered = FilterAssemblyPathsForCodeDom(assemblyPaths);

    // FIX: Write assembly references to a temporary response file (.rsp)
    // to avoid Windows MAX_PATH / command-line length limits.
    string rspPath = null;

    using (var provider = new CSharpCodeProvider())
    {
        var parameters = new CompilerParameters
        {
            GenerateInMemory = true,
            GenerateExecutable = false,
            TreatWarningsAsErrors = false,
        };

        try
        {
            // Build the response file with all assembly references
            var rspContent = new StringBuilder();
            foreach (var path in filtered)
                rspContent.AppendLine($"/reference:\"{path}\"");

            rspPath = Path.Combine(Path.GetTempPath(), $"mcp_codedom_{Guid.NewGuid():N}.rsp");
            File.WriteAllText(rspPath, rspContent.ToString(), Encoding.UTF8);

            // Pass references via response file instead of ReferencedAssemblies
            parameters.CompilerOptions = $"/noconfig \"@{rspPath}\"";

            var results = provider.CompileAssemblyFromSource(parameters, source);

            if (results.Errors.HasErrors)
            {
                foreach (CompilerError error in results.Errors)
                {
                    if (!error.IsWarning)
                    {
                        int userLine = Math.Max(1, error.Line - WrapperLineOffset);
                        errors.Add($"Line {userLine}: {error.ErrorText}");
                    }
                }
                return null;
            }
            return results.CompiledAssembly;
        }
        finally
        {
            if (rspPath != null && File.Exists(rspPath))
            {
                try { File.Delete(rspPath); } catch { }
            }
        }
    }
}
```

**Шаг 3 — Дождаться рекомпиляции:**
```
refresh_unity → compile: request, mode: force, scope: all
```

**Шаг 4 — Проверить:**
```
execute_code → action: execute, code: return "It works!";
```

## Тесты краснеют после `execute_code` — перезагрузить скрипты

После серии `execute_code` (загрузка префабов, `Apply …`, сборщики) в той же сессии редактора краснеют
`StateEventAuthorityTests.Автор_предмета_в_руке_машина_держащего`, `AvatarTeardownTests.Смена_аватара_изымает_оружие_из_руки`,
`EquipmentStripTests.Оружие_в_руке_уничтожается_и_рука_свободна`: синглтон `UxrGrabManager` остаётся в полусостоянии
(`HasInstance` ложно при живом экземпляре). Это шум харнесса, не отказ логики (2026-09-29). Перед итоговым прогоном:
`execute_code` → `EditorUtility.RequestScriptReload()`, затем `refresh_unity` и тесты — зелёные.
