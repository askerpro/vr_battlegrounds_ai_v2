Please read and strictly follow the instructions in the `.agentrules` file located in the root of this project.

Instruction for AI Assistant:
Proceed to read `.agentrules` via your file reading tools and use its content as your project rules.

---

## Ограничения чтения файлов (Legacy Copilot/Cursor)
Если инструмент `get_file` работает только для файлов из `.csproj` проектов или открытых вкладок редактора, а файлы `.md` в него не входят:
Для чтения `.md` документации используйте терминал PowerShell:
`[System.IO.File]::ReadAllText("F:\UnityProjects\Vr_Battlegrounds_ai\Assets\Docs\README.md", [System.Text.Encoding]::UTF8)`
Или активно просите пользователя открыть файл через `#file:` в контексте чата.
