# Unity MCP: execute_code fix на Windows

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

## Где уже зафиксировано внутри проекта

Техническая версия этой же информации уже есть в AI-инструкции:
- `.agents/rules/unity_mcp.md`

Этот файл в `Docs/` нужен как человеческая документация для разработчиков, не только для агента.

## Пошаговый фикс

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

## Важные примечания

- Это локальный фикс среды разработки.
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
