# Известные generated-файлы выделенных сборочных checkout (build-<config>-<platform>).
#
# Unity сама переписывает эти файлы в сборочном checkout при каждом запуске/сборке, без правки исходников.
# Дирижёры сборок (Build-Game.ps1, Run-QuestE2E.ps1) подключают файл через dot-source и возвращают такие
# файлы к индексу, только если diff совпал с ожидаемой природой; любое другое изменение по-прежнему
# считается незакоммиченной правкой и отменяет -Ref / парную сборку. Разбор — tasks/build-generated-files/Details.md.
#  - packages-lock.json: в репозитории MCP записан embedded (патченая копия Packages/com.coplaydev.unity-mcp,
#    ignored, ставит Tools/UnityMcp/Install-Upstream.ps1); в сборочном checkout копии нет, и UPM пишет
#    git-форму из manifest.json с hash.
#  - URP-ассеты качества: ShaderBuildPreprocessor перед каждой сборкой пишет поля m_Prefilter* под
#    целевую платформу (Win64 и Android дают разные значения, в общем Editor ещё и зависят от порядка сборок).

$GeneratedBuildFiles = @{
    'Packages/packages-lock.json'            = 'upm-lock-mcp'
    'Assets/URPDefaultResources/PC.asset'    = 'urp-prefilter'
    'Assets/URPDefaultResources/Quest.asset' = 'urp-prefilter'
}
$McpPackageName = 'com.coplaydev.unity-mcp'

# Форма записи MCP-пакета в lock: embedded, git (URL из manifest.json и hash из него) или $null.
function Get-McpLockForm($Lock, [string] $ManifestUrl) {
    $entry = $Lock.dependencies.$McpPackageName
    if ($null -eq $entry) { return $null }
    $names = @($entry.PSObject.Properties | ForEach-Object { $_.Name })
    if ($entry.version -ceq "file:$McpPackageName" -and $entry.source -ceq 'embedded' -and $names -notcontains 'hash') {
        return 'embedded'
    }
    if ($entry.source -ceq 'git' -and $entry.version -ceq $ManifestUrl -and $ManifestUrl -cmatch '#([0-9a-f]{40})$' -and $entry.hash -ceq $Matches[1]) {
        return 'git'
    }
    return $null
}

# $null — diff lock сводится к смене формы записи MCP под фактическое наличие embedded-копии; иначе причина.
function Test-GeneratedLockChange([string] $WorktreePath) {
    $rel = 'Packages/packages-lock.json'
    try {
        $indexText = @(& git --no-pager -C $WorktreePath show ":$rel" 2>$null) -join "`n"
        if ($LASTEXITCODE -ne 0) { return 'lock не прочитан из индекса' }
        $indexed = $indexText | ConvertFrom-Json
        $current = Get-Content -LiteralPath (Join-Path $WorktreePath $rel) -Raw -Encoding UTF8 | ConvertFrom-Json
        $manifest = Get-Content -LiteralPath (Join-Path $WorktreePath 'Packages/manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    } catch {
        return ('JSON не разобран: ' + $_.Exception.Message)
    }
    $url = [string]$manifest.dependencies.$McpPackageName
    $expected = if (Test-Path -LiteralPath (Join-Path $WorktreePath "Packages/$McpPackageName") -PathType Container) { 'embedded' } else { 'git' }
    $indexForm = Get-McpLockForm $indexed $url
    $currentForm = Get-McpLockForm $current $url
    if ($null -eq $indexForm) { return "запись $McpPackageName в индексе не embedded и не git из manifest.json" }
    if ($currentForm -cne $expected) { return "запись $McpPackageName в рабочей копии не $expected-форма для этого checkout" }

    foreach ($lock in @($indexed, $current)) {
        foreach ($field in @('version', 'source', 'hash')) { $lock.dependencies.$McpPackageName.PSObject.Properties.Remove($field) }
    }
    if (($indexed | ConvertTo-Json -Depth 32 -Compress) -cne ($current | ConvertTo-Json -Depth 32 -Compress)) {
        return "изменены записи кроме version/source/hash пакета $McpPackageName"
    }
    return $null
}

# $null — изменены только значения существующих полей m_Prefilter* URP-ассета; иначе причина.
function Test-GeneratedPrefilterChange([string] $WorktreePath, [string] $Rel) {
    $diff = @(& git --no-pager -C $WorktreePath -c core.quotepath=false diff --no-color --no-ext-diff --no-textconv -U0 -- $Rel 2>$null)
    if ($LASTEXITCODE -ne 0) { return 'git diff не выполнился' }
    $removed = New-Object System.Collections.Generic.List[string]
    $added = New-Object System.Collections.Generic.List[string]
    # Заголовок diff — только до первого hunk: внутри hunk '--- '/'+++ ' — удалённые/добавленные строки файла.
    $inHunk = $false
    foreach ($line in $diff) {
        if ($line -cmatch '^@@ ') { $inHunk = $true; continue }
        if (-not $inHunk -and $line -cmatch '^(diff --git |index |--- |\+\+\+ )') { continue }
        if ($line -cmatch '^([-+])  (m_Prefilter[A-Za-z0-9]*): -?\d+$') {
            if ($Matches[1] -ceq '-') { $removed.Add($Matches[2]) } else { $added.Add($Matches[2]) }
            continue
        }
        return "строка вне полей m_Prefilter*: $line"
    }
    if ($removed.Count -eq 0) { return 'нет изменённых полей m_Prefilter*' }
    if (($removed -join ',') -cne ($added -join ',')) { return 'набор полей m_Prefilter* изменился' }
    return $null
}

# Изменения checkout в Assets/Packages/ProjectSettings: Generated — известные generated-файлы с проверенным
# diff (только рабочая копия, индекс не тронут), Unexpected — всё остальное с причиной.
function Get-BuildCheckoutChanges([string] $WorktreePath) {
    $records = @(& git --no-pager -C $WorktreePath -c core.quotepath=false status --porcelain=v1 --untracked-files=all -- Assets Packages ProjectSettings 2>$null)
    if ($LASTEXITCODE -ne 0) { throw "git status не выполнился в $WorktreePath (код $LASTEXITCODE)." }
    $generated = New-Object System.Collections.Generic.List[string]
    $unexpected = New-Object System.Collections.Generic.List[string]
    foreach ($record in $records) {
        if ([string]::IsNullOrWhiteSpace($record)) { continue }
        $status = $record.Substring(0, 2)
        $path = $record.Substring(3)
        $kind = $GeneratedBuildFiles[$path]
        $reason = $null
        if ($null -eq $kind) { $reason = 'не generated-файл' }
        elseif ($status -cne ' M') { $reason = "статус '$status' вместо изменения рабочей копии" }
        elseif ($kind -ceq 'upm-lock-mcp') { $reason = Test-GeneratedLockChange $WorktreePath }
        else { $reason = Test-GeneratedPrefilterChange $WorktreePath $path }

        if ($null -eq $reason) { $generated.Add($path) } else { $unexpected.Add("$record — $reason") }
    }
    return [pscustomobject]@{ Generated = $generated.ToArray(); Unexpected = $unexpected.ToArray() }
}

# Сохраняет diff generated-файлов в $PatchPath и возвращает их к индексу. Чужие правки сюда не попадают:
# передавать только пути из Generated результата Get-BuildCheckoutChanges.
function Restore-GeneratedBuildChanges([string] $WorktreePath, [string[]] $Paths, [string] $PatchPath) {
    New-Item -ItemType Directory -Force (Split-Path $PatchPath -Parent) | Out-Null
    & git --no-pager -C $WorktreePath diff --no-color --no-ext-diff --binary "--output=$PatchPath" -- @Paths
    if ($LASTEXITCODE -ne 0) { throw "не удалось сохранить diff generated-файлов $WorktreePath в $PatchPath" }
    & git -C $WorktreePath checkout -- @Paths
    if ($LASTEXITCODE -ne 0) { throw "не удалось вернуть generated-файлы в $WorktreePath" }
    Write-Host ("Возвращены к индексу generated-файлы Unity в {0}: {1} (diff: {2})" -f $WorktreePath, ($Paths -join ', '), $PatchPath)
}

# Имя checkout для файлов логов: build-test-win64-Vr_Battlegrounds_ai.
function Get-BuildCheckoutLabel([string] $WorktreePath) {
    return ((Split-Path (Split-Path $WorktreePath -Parent) -Leaf) + '-' + (Split-Path $WorktreePath -Leaf)) -replace '[^A-Za-z0-9_.-]', '_'
}
