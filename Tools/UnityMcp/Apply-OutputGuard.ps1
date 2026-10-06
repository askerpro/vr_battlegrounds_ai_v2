[CmdletBinding()]
param([string]$Ticket, [string]$Token, [string]$StateDir, [switch]$OfflineEditor,
      [string]$PythonExecutable = 'python')
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$target = Join-Path $projectRoot 'Packages/com.coplaydev.unity-mcp'
$executePath = Join-Path $target 'Editor/Tools/ExecuteCode.cs'
$helperPath = Join-Path $target 'Editor/Helpers/ExecuteCodeOutputGuard.cs'
$helperSource = Join-Path $PSScriptRoot 'OutputGuard/ExecuteCodeOutputGuard.cs'
$baselineHash = '53552BB02B3568F0B91E7289AD269469990E368F0238A115F7C968F7CF885726'
$patchedHash = 'F1457DC145BBA8A0FC0E36EBECC27A1E2F9F3A69397DEC79D92269BB7C5DA144'
function Get-SourceHash([string]$Path) {
    # Git может менять LF/CRLF у патча; сравниваем код, а не локальную форму перевода строк.
    $text = [IO.File]::ReadAllText($Path).Replace("`r`n", "`n")
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($text))).Replace('-', '') }
    finally { $sha.Dispose() }
}
function Assert-EditorAccess {
    $guardArgs = @((& $PythonExecutable -c "import sys; sys.path.insert(0, r'$projectRoot/Tools/agents'); import broker_runtime; print(broker_runtime.require() / 'editor_broker' / 'client_guard.py')").Trim(), '--project', $projectRoot)
    if ($Ticket) { $guardArgs += @('--ticket', $Ticket) }
    if ($Token) { $guardArgs += @('--token', $Token) }
    if ($StateDir) { $guardArgs += @('--state-dir', $StateDir) }
    if ($OfflineEditor) { $guardArgs += '--offline-editor' }
    $reply = & $PythonExecutable @guardArgs
    if ($LASTEXITCODE -ne 0) { throw "Editor broker отказал в записи: $reply" }
}
Assert-EditorAccess
if ((Get-Content -LiteralPath (Join-Path $target 'package.json') -Raw | ConvertFrom-Json).version -ne '10.2.0') { throw 'Ожидался MCP 10.2.0.' }
$installedHash = Get-SourceHash $executePath
if ($installedHash -ne $baselineHash -and $installedHash -ne $patchedHash) { throw 'ExecuteCode отличается от проверенной базы; чужие изменения не перезаписываю.' }
foreach ($pair in @(@($helperPath, $helperSource), @($helperPath + '.meta', $helperSource + '.meta.txt'))) {
    if ((Test-Path -LiteralPath $pair[0]) -and (Get-SourceHash $pair[0]) -ne (Get-SourceHash $pair[1])) {
        throw 'Существующий helper/meta отличается от нашего артефакта; чужие изменения не перезаписываю.'
    }
}
$stage = Join-Path $projectRoot ('tmp/McpOutputGuardInstall/' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path (Join-Path $stage 'Editor/Tools') -Force | Out-Null
if ($installedHash -eq $baselineHash) { Copy-Item -LiteralPath $executePath -Destination (Join-Path $stage 'Editor/Tools/ExecuteCode.cs') }
$relativeRoot = $stage.Substring($projectRoot.Length + 1).Replace([IO.Path]::DirectorySeparatorChar, [char]'/')
$patch = Join-Path $PSScriptRoot 'output-guard-10.2.0.patch'
Push-Location $projectRoot
try {
    if ($installedHash -eq $baselineHash) {
        & git --no-pager -c core.autocrlf=false apply --check "--directory=$relativeRoot" $patch
        if ($LASTEXITCODE -ne 0) { throw 'Патч не применим.' }
        & git --no-pager -c core.autocrlf=false apply "--directory=$relativeRoot" $patch
        if ($LASTEXITCODE -ne 0) { throw 'Патч не применён к staging-копии.' }
        if ((Get-SourceHash (Join-Path $stage 'Editor/Tools/ExecuteCode.cs')) -ne $patchedHash) { throw 'Результат патча не совпадает с проверенным hash.' }
    }
    Assert-EditorAccess
    if ((Get-SourceHash $executePath) -ne $installedHash) { throw 'ExecuteCode изменён во время подготовки; установка отменена.' }
    # Сначала зависимость, затем подготовленный вызывающий код. Перекомпиляция — отдельный шаг.
    if (!(Test-Path -LiteralPath $helperPath)) { Copy-Item -LiteralPath $helperSource -Destination $helperPath }
    if (!(Test-Path -LiteralPath ($helperPath + '.meta'))) { Copy-Item -LiteralPath ($helperSource + '.meta.txt') -Destination ($helperPath + '.meta') }
    if ($installedHash -eq $baselineHash) { Copy-Item -LiteralPath (Join-Path $stage 'Editor/Tools/ExecuteCode.cs') -Destination $executePath }
    Write-Output 'Установлен OutputGuard. Refresh/компиляцию и live probe выполняйте через editor-broker в своей аренде.'
}
finally { Pop-Location }
