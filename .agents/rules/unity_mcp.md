# Unity MCP — Правила и Известные Проблемы

## execute_code — Ошибка MAX_PATH на Windows

### Симптом
Вызов `execute_code` падает с ошибкой:
```
Execution failed: Error running mono.exe: Имя файла или его расширение имеет слишком большую длину.
```

### Причина
`CSharpCodeProvider` (CodeDom) передаёт **все** referenced assemblies (~100+) как аргументы командной строки `mono.exe`.
Суммарная длина путей вроде `C:\Program Files\Unity\Hub\Editor\6000.x.xf1\Editor\Data\...` превышает лимит Windows (~32KB).

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

### Проверка статуса
Если `execute_code` уже работает — патч применён и повторно делать не нужно.
Быстрая проверка: `execute_code → action: execute, code: return 42;`
- Если `success: true` → всё ОК.
- Если ошибка `MAX_PATH` → применить патч выше.
