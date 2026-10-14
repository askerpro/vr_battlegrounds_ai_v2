[CmdletBinding()]
param([string]$Ticket, [string]$Token, [string]$StateDir, [switch]$OfflineEditor,
      [string]$PythonExecutable = 'python')
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$target = Join-Path $projectRoot 'Packages/com.coplaydev.unity-mcp/Editor/Services/TestJobManager.cs'
$baselineHash = '80BE4984F63398BA8D3484A514E131F131B9A562757E713B07D0AC73FC164476'
$previousHash = '395272A8507666A4D88D3B72E69023A03F25499712F1CB895877DFB2CCFF4A01'
$patchedHash = 'CBC4952435AB4EF0A710726038EB4D4EDD254E30592916D9B4C3D92E1BB0EACA'
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
$serviceBaselineHash = 'B5F25BFFBCEE394D746F4A67B7CFABA1BF3E24279F581D79FEC7C20098D6EE23'
$servicePatchedHash = '291801256C293EF07F51BF53668FEC4A5A64E5CEDE7C89F755614D75F5B545F9'
$items = @(
    [pscustomobject]@{ Name='TestJobManager.cs'; Target=$target; Expected=$patchedHash },
    [pscustomobject]@{ Name='TestRunnerService.cs'; Target=(Join-Path $package 'Editor/Services/TestRunnerService.cs'); Expected=$servicePatchedHash }
)
foreach ($item in $items) {
    if (-not (Test-Path -LiteralPath $item.Target -PathType Leaf) -or -not (Test-Path -LiteralPath ($item.Target + '.meta') -PathType Leaf)) { throw 'Оба source и существующие .meta обязательны; установка отменена.' }
    $item | Add-Member OriginalHash (Get-SourceHash $item.Target)
    $item | Add-Member OriginalBytesHash ((Get-FileHash -LiteralPath $item.Target).Hash)
    $item | Add-Member MetaHash ((Get-FileHash -LiteralPath ($item.Target + '.meta')).Hash)
}
$managerHash = $items[0].OriginalHash
$serviceHash = $items[1].OriginalHash
$knownOldPair = ($managerHash -in @($baselineHash, $previousHash, $patchedHash)) -and $serviceHash -eq $serviceBaselineHash
$knownCurrentPair = $managerHash -eq $patchedHash -and $serviceHash -eq $servicePatchedHash
if (-not $knownOldPair -and -not $knownCurrentPair) { throw 'Source pair отличается от проверенных версий; чужие/частичные изменения не перезаписываю.' }
function Assert-OriginalPair {
    foreach ($entry in $items) {
        if ((Get-SourceHash $entry.Target) -ne $entry.OriginalHash -or (Get-FileHash -LiteralPath $entry.Target).Hash -ne $entry.OriginalBytesHash -or (Get-FileHash -LiteralPath ($entry.Target + '.meta')).Hash -ne $entry.MetaHash) { throw 'Source/.meta pair изменены во время подготовки; установка отменена.' }
    }
}
if ($knownCurrentPair) {
    Assert-EditorAccess
    Assert-OriginalPair
    Write-Output 'TestJobLifecycle/terminal tree уже установлены; source и .meta сохранены.'
    return
}
$stage = Join-Path $projectRoot ('tmp/McpTestJobLifecycleInstall/' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path (Join-Path $stage 'Editor/Services') -Force | Out-Null
foreach ($item in $items) {
    $stagedSource = Join-Path $stage ('Editor/Services/' + $item.Name)
    $item | Add-Member Staged $stagedSource
    $item | Add-Member Backup (Join-Path $stage ($item.Name + '.backup'))
    Copy-Item -LiteralPath $item.Target -Destination $item.Backup
    Copy-Item -LiteralPath ($item.Target + '.meta') -Destination (Join-Path $stage ($item.Name + '.meta.backup'))
    [IO.File]::WriteAllText($stagedSource, [IO.File]::ReadAllText($item.Target).Replace("`r`n", "`n"), [Text.UTF8Encoding]::new($false))
}
$patchName = if ($managerHash -eq $baselineHash) { 'test-job-lifecycle-10.2.0.patch' } elseif ($managerHash -eq $previousHash) { 'test-job-lifecycle-mcp2-to-current.patch' } else { 'test-job-terminal-results-10.2.0.patch' }
$patch = Join-Path $PSScriptRoot $patchName
$relativeRoot = $stage.Substring($projectRoot.Length + 1).Replace([IO.Path]::DirectorySeparatorChar, [char]'/')
Push-Location $projectRoot
try {
    & git --no-pager -c core.autocrlf=false apply --ignore-space-change --check "--directory=$relativeRoot" $patch
    if ($LASTEXITCODE -ne 0) { throw 'Source pair patch не применим.' }
    & git --no-pager -c core.autocrlf=false apply --ignore-space-change "--directory=$relativeRoot" $patch
    if ($LASTEXITCODE -ne 0) { throw 'Source pair patch не применён к staging-копии.' }
}
finally { Pop-Location }
foreach ($item in $items) {
    if ((Get-SourceHash $item.Staged) -ne $item.Expected) { throw 'Staged source pair hash не подтверждён.' }
    $item | Add-Member CandidateBytesHash ((Get-FileHash -LiteralPath $item.Staged).Hash)
    if ((Get-FileHash -LiteralPath $item.Backup).Hash -ne $item.OriginalBytesHash -or (Get-FileHash -LiteralPath (Join-Path $stage ($item.Name + '.meta.backup'))).Hash -ne $item.MetaHash) { throw 'Backup source/.meta не совпадает с исходными байтами.' }
}
# Подготовка обоих файлов завершена; повторный guard и CAS до первой записи.
Assert-EditorAccess
Assert-OriginalPair
$changed = [Collections.Generic.List[string]]::new()
function Save-Transaction([string]$Phase, [string[]]$Errors = @()) {
    $receipt = [ordered]@{schema_version=1; phase=$Phase; changed_sources=@($changed.ToArray()); rollback_errors=@($Errors)}
    [IO.File]::WriteAllText((Join-Path $stage 'transaction.json'), ($receipt | ConvertTo-Json -Depth 4), [Text.UTF8Encoding]::new($false))
}
Save-Transaction 'staged'
try {
    foreach ($item in $items) {
        if ($item.OriginalHash -eq $item.Expected) { continue }
        if ((Get-FileHash -LiteralPath $item.Target).Hash -ne $item.OriginalBytesHash -or (Get-FileHash -LiteralPath ($item.Target + '.meta')).Hash -ne $item.MetaHash) { throw 'Source/.meta изменены перед replace; установка отменена.' }
        $writeTemp = Join-Path $stage ($item.Name + '.write-' + [guid]::NewGuid().ToString('N'))
        Copy-Item -LiteralPath $item.Staged -Destination $writeTemp
        # Атомарна замена одного файла. Между двумя replace нет общей filesystem transaction.
        [IO.File]::Replace($writeTemp, $item.Target, (Join-Path $stage ($item.Name + ".swap-before-" + [guid]::NewGuid().ToString("N"))))
        $changed.Add($item.Name)
        Save-Transaction 'installing'
    }
    foreach ($item in $items) {
        if ((Get-SourceHash $item.Target) -ne $item.Expected -or (Get-FileHash -LiteralPath ($item.Target + '.meta')).Hash -ne $item.MetaHash) { throw 'Установленный source/.meta pair не подтверждён.' }
    }
    Save-Transaction 'committed'
}
catch {
    $installError = $_.Exception.Message
    $rollbackErrors = [Collections.Generic.List[string]]::new()
    $rollbackItems = @($items)
    [array]::Reverse($rollbackItems)
    foreach ($item in $rollbackItems) {
        try {
            $currentBytesHash = (Get-FileHash -LiteralPath $item.Target).Hash
            if ($currentBytesHash -eq $item.OriginalBytesHash) { continue }
            if ($currentBytesHash -ne $item.CandidateBytesHash -or (Get-FileHash -LiteralPath ($item.Target + '.meta')).Hash -ne $item.MetaHash) { throw 'Rollback не перезаписывает неожиданные source/.meta.' }
            if (-not $changed.Contains($item.Name)) { $changed.Add($item.Name) }
            if ((Get-FileHash -LiteralPath $item.Backup).Hash -ne $item.OriginalBytesHash) { throw 'Rollback backup hash изменён.' }
            $rollbackTemp = Join-Path $stage ($item.Name + '.rollback-' + [guid]::NewGuid().ToString('N'))
            Copy-Item -LiteralPath $item.Backup -Destination $rollbackTemp
            [IO.File]::Replace($rollbackTemp, $item.Target, (Join-Path $stage ($item.Name + ".rollback-before-" + [guid]::NewGuid().ToString("N"))))
        }
        catch { $rollbackErrors.Add($item.Name + ': ' + $_.Exception.Message) }
    }
    if ($rollbackErrors.Count -eq 0) { Save-Transaction 'rolled_back' } else { Save-Transaction 'rollback_blocked' $rollbackErrors.ToArray() }
    throw "Установка source pair не завершена: $installError. Rollback errors: $($rollbackErrors.Count). Evidence: $stage"
}
Write-Output 'Установлены TestJobLifecycle и terminal tree. Refresh/компиляцию и live probe выполняйте через editor-broker в своей аренде.'
