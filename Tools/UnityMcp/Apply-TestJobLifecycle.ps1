[CmdletBinding()]
param([string]$Ticket, [string]$Token, [string]$StateDir, [switch]$OfflineEditor,
      [string]$PythonExecutable = 'python')
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$target = Join-Path $projectRoot 'Packages/com.coplaydev.unity-mcp/Editor/Services/TestJobManager.cs'
$baselineHash = '80BE4984F63398BA8D3484A514E131F131B9A562757E713B07D0AC73FC164476'
$patchedHash = '395272A8507666A4D88D3B72E69023A03F25499712F1CB895877DFB2CCFF4A01'
function Get-SourceHash([string]$Path) {
    # Код сравнивается после нормализации LF/CRLF; backup сохраняет исходные байты.
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
$package = Join-Path $projectRoot 'Packages/com.coplaydev.unity-mcp'
if ((Get-Content -LiteralPath (Join-Path $package 'package.json') -Raw | ConvertFrom-Json).version -ne '10.2.0') { throw 'Ожидался MCP 10.2.0.' }
$installedHash = Get-SourceHash $target
if ($installedHash -ne $baselineHash -and $installedHash -ne $patchedHash) { throw 'TestJobManager отличается от проверенной базы; чужие изменения не перезаписываю.' }
$originalByteHash = (Get-FileHash -LiteralPath $target).Hash
$stage = $null
if ($installedHash -eq $baselineHash) {
    $stage = Join-Path $projectRoot ('tmp/McpTestJobLifecycleInstall/' + [guid]::NewGuid().ToString('N'))
    $stagedSource = Join-Path $stage 'Editor/Services/TestJobManager.cs'
    New-Item -ItemType Directory -Path (Split-Path -Parent $stagedSource) -Force | Out-Null
    Copy-Item -LiteralPath $target -Destination (Join-Path $stage 'TestJobManager.cs.backup')
    # Патч применяется к собственной LF-копии. Существующая .meta не меняется.
    [IO.File]::WriteAllText($stagedSource, [IO.File]::ReadAllText($target).Replace("`r`n", "`n"), [Text.UTF8Encoding]::new($false))
    $relativeRoot = $stage.Substring($projectRoot.Length + 1).Replace([IO.Path]::DirectorySeparatorChar, [char]'/')
    $patch = Join-Path $PSScriptRoot 'test-job-lifecycle-10.2.0.patch'
    Push-Location $projectRoot
    try {
        & git --no-pager -c core.autocrlf=false apply --ignore-space-change --check "--directory=$relativeRoot" $patch
        if ($LASTEXITCODE -ne 0) { throw 'Патч жизненного цикла тестов не применим.' }
        & git --no-pager -c core.autocrlf=false apply --ignore-space-change "--directory=$relativeRoot" $patch
        if ($LASTEXITCODE -ne 0) { throw 'Патч жизненного цикла тестов не применён к staging-копии.' }
    }
    finally { Pop-Location }
    if ((Get-SourceHash $stagedSource) -ne $patchedHash) { throw 'Результат патча не совпадает с проверенным hash.' }
}
Assert-EditorAccess
if ((Get-SourceHash $target) -ne $installedHash -or (Get-FileHash -LiteralPath $target).Hash -ne $originalByteHash) {
    throw 'TestJobManager изменён во время подготовки; установка отменена.'
}
if ($installedHash -eq $baselineHash) {
    if ((Get-FileHash -LiteralPath (Join-Path $stage 'TestJobManager.cs.backup')).Hash -ne $originalByteHash) { throw 'Backup не совпадает с исходными байтами.' }
    Copy-Item -LiteralPath $stagedSource -Destination $target
}
if ((Get-SourceHash $target) -ne $patchedHash) { throw 'Hash установленного TestJobManager не подтверждён.' }
Write-Output 'Установлен TestJobLifecycle. Refresh/компиляцию и live probe выполняйте через editor-broker в своей аренде.'
