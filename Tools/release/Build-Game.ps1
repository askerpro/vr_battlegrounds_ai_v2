# Сборка сервера, Quest и планшета без открытого редактора (batch-режим Unity).
#
# Вся логика — в Assets/Editor/VR_Battlegrounds/Release/GameBuilder.cs; скрипт только
# находит Unity нужной версии, запускает -executeMethod и показывает хвост лога.
# Профили сверяются по отпечатку UXR id (uxr-ids.txt рядом со сборками) — см. Docs/release.md.
#
# Unity запускается отдельно на каждую группу платформ: Android-профили (quest, tablet) — один
# запуск с -buildTarget Android, server — отдельный с -buildTarget Win64. Editor-скрипты
# компилируются под платформу запуска, а переключение платформы внутри сборки их не
# перекомпилирует: OculusManifest (VR-категория в манифесте Quest) и другие Android-обработчики
# есть только в редакторе, открытом под Android. Один запуск «server,quest» дал бы плоский Quest.
#
# Редактор на этом проекте должен быть закрыт: Unity не открывает проект дважды.
# При открытом редакторе — меню Tools/VR Battlegrounds/Release.
#
# Коды возврата: 0 — все сборки собраны, 1 — сборка упала, 2 — не запустилась.
#
# Запуск:
#   powershell -ExecutionPolicy Bypass -File Tools\release\Build-Game.ps1
#   powershell -ExecutionPolicy Bypass -File Tools\release\Build-Game.ps1 -Targets quest,tablet -Config Prod
#   powershell -ExecutionPolicy Bypass -File Tools\release\Build-Game.ps1 -Targets all -Config Test -DryRun
#   powershell -ExecutionPolicy Bypass -File Tools\release\Build-Game.ps1 -ReaggregateInput input.json -ReaggregateOutputDirectory reports\new-pair

[CmdletBinding()]
param(
    # all или список через запятую: server, quest, tablet.
    [string] $Targets = 'all',

    # Test — проверка на шлеме (Development, полные стектрейсы, быстрая сборка IL2CPP);
    # Prod — релиз (IL2CPP Master, без Development). Таблица — Docs/release.md.
    [ValidateSet('Test', 'Prod')]
    [string] $Config = 'Test',

    # Устарело: то же, что -Config Test.
    [switch] $Development,

    # Путь к Unity.exe. Пусто — версия из ProjectSettings/ProjectVersion.txt в Unity Hub.
    [string] $Unity = '',

    # Явный путь к Win64 сборочному worktree (Server). Пусто — авто-выбор build-<config>-win64.
    [string] $Win64Worktree = '',

    # Явный путь к Android сборочному worktree (Quest, Tablet). Пусто — авто-выбор build-<config>-android.
    [string] $AndroidWorktree = '',

    # Принудительно собирать в текущем worktree ($RepoRoot) вместо выделенных build-* worktree.
    [switch] $UseCurrentWorktree,

    # Переключить задействованные выделенные сборочные worktree на указанный git ref/SHA перед сборкой.
    [string] $Ref = '',

    # Проверить сборочные worktree, ревизии, LFS и вывести команды запуска Unity без старта сборки.
    [switch] $DryRun,

    # Повторная проверка сохранённых artifacts; Unity и сборочные checkout не затрагиваются.
    [string] $ReaggregateInput = '',
    [string] $ReaggregateOutputDirectory = ''
)

$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch {}

# Запуски Unity по группам платформ: сначала Android (quest, tablet), затем server.
# Бросает на неизвестном профиле.
function Get-BuildRuns([string] $Spec) {
    $known = @('server', 'quest', 'tablet')
    $names = New-Object System.Collections.Generic.List[string]

    foreach ($item in ($Spec -split '[,;]')) {
        $name = $item.Trim().ToLower()
        if ($name -eq '') { continue }

        if ($name -eq 'all') { $expanded = $known }
        elseif ($known -contains $name) { $expanded = @($name) }
        else { throw "неизвестный профиль '$name'. Есть: all, server, quest, tablet." }

        foreach ($n in $expanded) { if (-not $names.Contains($n)) { $names.Add($n) } }
    }

    if ($names.Count -eq 0) { throw 'не задан ни один профиль. Есть: all, server, quest, tablet.' }

    $runs = New-Object System.Collections.Generic.List[object]
    $android = @(@('quest', 'tablet') | Where-Object { $names.Contains($_) })
    if ($android.Count -gt 0) { $runs.Add([pscustomobject]@{ Name = 'android'; BuildTarget = 'Android'; Profiles = ($android -join ',') }) }
    if ($names.Contains('server')) { $runs.Add([pscustomobject]@{ Name = 'server'; BuildTarget = 'Win64'; Profiles = 'server' }) }
    return $runs.ToArray()
}

# Итог сборщика — блок от маркера [GameBuilder] до конца сообщения; нет маркера — хвост лога.
function Show-LogResult([string] $Log) {
    if (-not (Test-Path $Log)) {
        Write-Host "Лога нет: $Log" -ForegroundColor Yellow
        return
    }

    $lines = Get-Content $Log -Encoding UTF8
    $start = ($lines | Select-String -SimpleMatch '[GameBuilder]' | Select-Object -Last 1)
    if ($start) {
        $lines[($start.LineNumber - 1)..([Math]::Min($lines.Count - 1, $start.LineNumber + 18))] | ForEach-Object { Write-Host $_ }
    } else {
        Write-Host 'Итога [GameBuilder] в логе нет — сборка не дошла до конца. Хвост лога:' -ForegroundColor Yellow
        $lines | Select-Object -Last 40 | ForEach-Object { Write-Host $_ }
    }
}

function Test-EditorLocked([string] $WorktreePath) {
    $lock = Join-Path $WorktreePath 'Temp\UnityLockfile'
    if (-not (Test-Path -LiteralPath $lock)) { return $false }
    try {
        $stream = [System.IO.File]::Open($lock, 'Open', 'ReadWrite', 'None')
        $stream.Close()
        return $false
    } catch {
        return $true
    }
}

function Get-LfsPointerFiles([string] $WorktreePath) {
    $lfsFiles = @(& git -C $WorktreePath -c core.quotepath=false lfs ls-files -n 2>$null)
    if ($LASTEXITCODE -ne 0) {
        throw "Не удалось проверить Git LFS в $WorktreePath (git lfs ls-files, код $LASTEXITCODE)."
    }
    if ($lfsFiles.Count -eq 0) {
        return @()
    }
    $pointers = New-Object System.Collections.Generic.List[string]
    foreach ($rel in $lfsFiles) {
        if ([string]::IsNullOrWhiteSpace($rel)) { continue }
        $full = Join-Path $WorktreePath ($rel -replace '/', '\')
        if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { continue }
        try {
            $item = Get-Item -LiteralPath $full -ErrorAction Stop
            if ($item.Length -gt 256) { continue }
            $fs = [System.IO.File]::Open($full, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
            try {
                $buf = New-Object byte[] ([Math]::Min(64, [int]$item.Length))
                $read = $fs.Read($buf, 0, $buf.Length)
                $head = [System.Text.Encoding]::ASCII.GetString($buf, 0, $read)
                if ($head.StartsWith('version https://git-lfs.github.com/spec/v1')) {
                    $pointers.Add($rel)
                }
            } finally {
                $fs.Close()
            }
        } catch {
            continue
        }
    }
    return $pointers.ToArray()
}

# Известные generated-файлы сборочных checkout и их возврат к индексу.
. (Join-Path $PSScriptRoot 'BuildCheckoutGenerated.ps1')

# Изменения, которые мешают -Ref и парной сборке. В выделенном сборочном checkout известные
# generated-файлы не мешают (Build-Game возвращает их сам); в текущем проекте мешает любое изменение.
function Get-BlockingChanges([string] $WorktreePath) {
    if ($dedicatedWorktrees -contains $WorktreePath) { return @((Get-BuildCheckoutChanges $WorktreePath).Unexpected) }
    return @(& git -C $WorktreePath status --porcelain -- Assets Packages ProjectSettings 2>$null)
}

function Read-UxrIdHash([string] $ProfileBuildDir) {
    $uxrFile = Join-Path $ProfileBuildDir 'uxr-ids.txt'
    if (-not (Test-Path -LiteralPath $uxrFile -PathType Leaf)) { return $null }
    # UxrIdFingerprint.WriteTo записывает hash первой строкой без заголовка.
    $line = Get-Content -LiteralPath $uxrFile -Encoding UTF8 -TotalCount 1
    if ($null -eq $line) { return $null }
    $hash = $line.Trim()
    if ($hash -match '^[0-9a-fA-F]{16}$') { return $hash }
    return $null
}

# Собирает raw provenance только из текущих terminal попыток, не из оставшихся outputs прежней сборки.
function ConvertTo-BuildTimestamp($Value) {
    if ($Value -is [DateTimeOffset]) { return $Value }
    if ($Value -is [DateTime]) {
        if ($Value.Kind -eq [DateTimeKind]::Unspecified) { throw 'Дата без часового пояса не принимается' }
        return [DateTimeOffset]$Value
    }
    if ($Value -isnot [string] -or $Value -notmatch '^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d{1,7})?(Z|[+-]\d{2}:\d{2})$') {
        throw 'Ожидалась ISO 8601 дата с часовым поясом'
    }
    return [DateTimeOffset]::Parse($Value, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::None)
}

function Assert-FileHash([string] $Path, [string] $Expected) {
    if ($Expected -cnotmatch '^[0-9a-f]{64}$' -or -not (Test-Path -LiteralPath $Path -PathType Leaf) -or
        (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() -cne $Expected) {
        throw "Не совпал SHA256 файла: $Path"
    }
}

function Get-ContainedArtifactPath([string] $Root, [string] $Relative) {
    if ([string]::IsNullOrWhiteSpace($Relative) -or [IO.Path]::IsPathRooted($Relative) -or $Relative.Contains(':') -or
        @($Relative -split '[\\/]' | Where-Object { $_ -eq '..' -or $_ -eq '.' -or $_ -eq '' }).Count -gt 0) {
        throw "Недопустимый относительный путь artifact: $Relative"
    }
    $base = [IO.Path]::GetFullPath($Root).TrimEnd([char[]]'\/')
    $full = [IO.Path]::GetFullPath((Join-Path $base $Relative))
    if (-not $full.StartsWith($base + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Artifact вне outputRoot' }
    $cursor = Get-Item -LiteralPath $full -Force
    while ($null -ne $cursor -and $cursor.FullName.Length -ge $base.Length) {
        if (($cursor.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "ReparsePoint в artifact path: $full" }
        if ($cursor -is [IO.DirectoryInfo]) { $cursor = $cursor.Parent }
        else { $cursor = $cursor.Directory }
        if ($null -eq $cursor) { break }
    }
    return $full
}

# Как OutputFiles сборщика: полный набор, без перехода через reparse point.
# Pending status paths не сериализуются schema1; terminal inventory после CompleteStatusWrite обновлён.
function Get-BuildArtifactFiles([string] $Root) {
    $base = [IO.Path]::GetFullPath($Root).TrimEnd([char[]]'\/')
    $manifestPath = Join-Path $base 'build.json'
    $directories = New-Object 'System.Collections.Generic.Stack[string]'
    $directories.Push($base)
    while ($directories.Count -gt 0) {
        $directory = $directories.Pop()
        $attributes = [IO.File]::GetAttributes($directory)
        if (($attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "ReparsePoint в artifact tree: $directory" }
        foreach ($entry in [IO.Directory]::GetFileSystemEntries($directory)) {
            $attributes = [IO.File]::GetAttributes($entry)
            if (($attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "ReparsePoint в artifact tree: $entry" }
            if (($attributes -band [IO.FileAttributes]::Directory) -ne 0) { $directories.Push($entry) }
            elseif (-not [string]::Equals($entry, $manifestPath, [StringComparison]::OrdinalIgnoreCase)) { $entry }
        }
    }
}

# Единый validator для текущего запуска и повторной агрегации архивов.
function Read-ValidatedBuildProfile([string] $ManifestPath, [string] $ArtifactRoot, [string] $RecordedRoot,
    [string] $Profile, [string] $SourceSha, [string] $Configuration, $Attempt, [bool] $OutcomeConfirmed) {
    $manifest = Get-Content -LiteralPath $ManifestPath -Encoding UTF8 -Raw | ConvertFrom-Json
    if ($manifest.schemaVersion -ne 1 -or $manifest.sourceSha -cne $SourceSha) { throw 'Версия или полный sourceSha builder manifest не совпали' }
    $records = @($manifest.profiles | Where-Object { $_.profile -ceq $Profile })
    if ($records.Count -ne 1) { throw 'Builder manifest не содержит единственный ожидаемый профиль' }
    $record = $records[0]
    if ([IO.Path]::GetFullPath([string]$record.outputRoot) -ine [IO.Path]::GetFullPath($RecordedRoot)) { throw 'Builder outputRoot не совпал с исходным путём профиля' }
    if ($record.actual.configuration -cne $Configuration) { throw 'Builder configuration не совпала' }
    $start = ConvertTo-BuildTimestamp $record.terminal.startedUtc
    $end = ConvertTo-BuildTimestamp $record.terminal.endedUtc
    $attemptStart = ConvertTo-BuildTimestamp $Attempt.startedUtc
    $attemptEnd = ConvertTo-BuildTimestamp $Attempt.endedUtc
    if ($start -lt $attemptStart -or $end -lt $start -or $end -gt $attemptEnd -or $attemptEnd -gt [DateTimeOffset]::UtcNow) {
        throw 'Builder manifest не принадлежит временному интервалу текущего запуска'
    }
    if (-not $OutcomeConfirmed -or $record.success -isnot [bool] -or -not $record.success -or
        $record.terminal.result -cne 'Succeeded' -or @($record.files).Count -eq 0) { throw 'Успешный terminal builder и его процесс не подтверждены' }
    $seen = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    foreach ($file in @($record.files)) {
        $full = Get-ContainedArtifactPath $ArtifactRoot $file.path
        if (-not $seen.Add($full)) { throw 'Повторный нормализованный путь в inventory' }
        if ($null -eq $file.bytes -or $file.bytes -lt 0 -or (Get-Item -LiteralPath $full).Length -ne $file.bytes) { throw "Не совпал размер artifact: $full" }
        Assert-FileHash $full $file.sha256
    }
    $actualFiles = @(Get-BuildArtifactFiles $ArtifactRoot)
    if ($actualFiles.Count -ne $seen.Count) { throw 'Фактический набор artifact файлов отличается от inventory' }
    foreach ($actualFile in $actualFiles) {
        if (-not $seen.Contains($actualFile)) { throw "В artifact tree найден файл вне inventory: $actualFile" }
    }
    if (-not $seen.Contains((Get-ContainedArtifactPath $ArtifactRoot 'uxr-ids.txt')) -or [string]::IsNullOrWhiteSpace((Read-UxrIdHash $ArtifactRoot))) { throw 'Нет проверенного UXR fingerprint в inventory' }
    return [pscustomobject]@{ Manifest = $manifest; Record = $record }
}

# $Generated: checkout -> @{ путь -> sha256 } generated-файлов, классифицированных BuildCheckoutGenerated.ps1 после
# сборки. Запись dirty builder manifest с тем же путём и sha256 помечается generated и не делает пару dirty.
function Write-BuildPairManifest($Runs, $Heads, $TerminalRuns, [bool] $BuildPassed, $Recovery = $null, $Generated = @{}) {
    $profiles = New-Object System.Collections.Generic.List[object]
    $origins = New-Object System.Collections.Generic.List[object]
    $dirtyEntries = New-Object System.Collections.Generic.List[object]
    $dirtyKnown = $true
    $dirtyValue = $false
    $passed = $BuildPassed
    $folders = @{ server = 'Server'; quest = 'Quest'; tablet = 'Tablet' }
    foreach ($run in $Runs) {
        $terminal = $TerminalRuns[$run.Name]
        foreach ($profile in ($run.Profiles -split ',')) {
            $profile = $profile.Trim().ToLowerInvariant()
            $root = [IO.Path]::GetFullPath((Join-Path $run.ProjectPath ('Build/' + $folders[$profile])))
            $artifactRoot = $root
            if ($null -ne $Recovery) { $artifactRoot = $Recovery.Roots[$profile] }
            $manifestPath = Join-Path $artifactRoot 'build.json'
            $record = $null
            $reason = 'Сборка этого профиля не запускалась в текущей попытке'
            if ($null -ne $terminal) {
                try {
                    $confirmed = $null -ne $terminal.exitCode -and $terminal.exitCode -eq 0
                    if ($null -ne $Recovery) { $confirmed = $Recovery.OutcomeConfirmed }
                    $validated = Read-ValidatedBuildProfile $manifestPath $artifactRoot $root $profile $Heads[$run.ProjectPath] $Config $terminal $confirmed
                    $manifest = $validated.Manifest
                    $record = $validated.Record
                    if ($null -ne $Recovery) { $record.outputRoot = $artifactRoot }
                    $origins.Add([ordered]@{ path = $manifestPath; sha256 = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant() })
                    $generatedHashes = $Generated[$run.ProjectPath]
                    $entries = @($manifest.dirty.entries)
                    $sourceDirty = $entries.Count -eq 0
                    foreach ($entry in $entries) {
                        $isGenerated = $null -ne $generatedHashes -and $entry.status -ceq ' M' -and
                            $null -ne $entry.sha256 -and $generatedHashes[[string]$entry.path] -ceq $entry.sha256
                        if (-not $isGenerated) { $sourceDirty = $true }
                        $dirtyEntries.Add([ordered]@{ checkout = $run.ProjectPath; evidence = $entry; generated = $isGenerated })
                    }
                    if ($manifest.dirty.value -isnot [bool]) { $dirtyKnown = $false }
                    elseif ($manifest.dirty.value -and $sourceDirty) { $dirtyValue = $true }
                } catch {
                    $record = $null
                    $reason = $_.Exception.Message
                }
            }
            if ($null -eq $record) {
                $passed = $false
                $dirtyKnown = $false
                $unknownActual = [ordered]@{}
                $unknownReasons = [ordered]@{ manifest = $reason }
                foreach ($field in @('developmentBuild', 'scenePaths', 'buildGuid', 'appVersion', 'unityVersion', 'companyName', 'productName', 'buildTarget', 'subtarget', 'scriptingBackend', 'graphicsApis', 'buildOptions')) {
                    $unknownActual[$field] = $null
                    $unknownReasons[$field] = $reason
                }
                $unknownActual.unavailableReasons = $unknownReasons
                $record = [ordered]@{
                    profile = $profile; outputRoot = $artifactRoot; files = @(); success = $false
                    actual = $unknownActual
                    terminal = @{ startedUtc = $(if ($terminal) { (ConvertTo-BuildTimestamp $terminal.startedUtc).ToString('o') } else { $null }); endedUtc = $(if ($terminal) { $terminal.endedUtc } else { $null }); result = $null; reason = $reason }
                }
            }
            $profiles.Add($record)
        }
    }
    $dirty = $null
    if ($dirtyKnown -or $dirtyValue) { $dirty = $dirtyValue }
    $pair = [ordered]@{
        schemaVersion = 1; sourceSha = $Heads[$Runs[0].ProjectPath]; sourceShaReason = $null
        sourceKind = 'unknown'; sourceKindReason = 'Подтверждённый accepted/checkpoint proof не передан; Git HEAD не доказывает приёмку'
        dirty = @{
            value = $dirty; entries = $dirtyEntries.ToArray(); reason = $(if (-not $dirtyKnown) { 'Часть builder manifests не предоставила dirty evidence' } else { $null })
            generatedRule = 'generated=true: известный generated-файл сборочного checkout (Tools/release/BuildCheckoutGenerated.ps1) с тем же sha256 после сборки; value его не учитывает'
        }
        profiles = $profiles.ToArray(); sourceManifests = $origins.ToArray(); success = $passed
        generatedUtc = [DateTimeOffset]::UtcNow.ToString('o')
    }
    if ($null -ne $Recovery) { $pair.reaggregation = $Recovery.Evidence }
    $id = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffffffZ') + '-' + [Guid]::NewGuid().ToString('N')
    $directory = Join-Path $RepoRoot ('Build/manifests/build-' + $id)
    if ($null -ne $Recovery) { $directory = $Recovery.OutputDirectory }
    $null = New-Item -ItemType Directory -Path $directory -Force
    $path = Join-Path $directory 'build.json'
    $json = ($pair | ConvertTo-Json -Depth 40) -replace "`r`n", "`n"
    [IO.File]::WriteAllText($path, $json + "`n", (New-Object Text.UTF8Encoding($false)))
    return [pscustomobject]@{ Path = $path; Passed = $passed }
}

function Invoke-BuildReaggregation([string] $InputPath, [string] $OutputDirectory) {
    if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { throw 'Укажи новый -ReaggregateOutputDirectory' }
    $output = [IO.Path]::GetFullPath($OutputDirectory)
    if (Test-Path -LiteralPath $output) { throw 'Каталог повторной агрегации уже существует; перезапись запрещена' }
    $null = New-Item -ItemType Directory -Path $output
    try {
        $inputFull = (Resolve-Path -LiteralPath $InputPath).Path
        $inputHash = (Get-FileHash -LiteralPath $inputFull -Algorithm SHA256).Hash.ToLowerInvariant()
        $spec = Get-Content -LiteralPath $inputFull -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($spec.schemaVersion -ne 1 -or $spec.sourceSha -cnotmatch '^[0-9a-f]{40}$' -or @('Test', 'Prod') -cnotcontains $spec.configuration) { throw 'Некорректный recovery input schema/source/configuration' }
        Assert-FileHash $spec.originalPair.path $spec.originalPair.sha256
        Assert-FileHash $spec.originalPair.recordedPath $spec.originalPair.sha256
        Assert-FileHash $spec.console.path $spec.console.sha256
        $original = Get-Content -LiteralPath $spec.originalPair.path -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($original.schemaVersion -ne 1 -or $original.sourceSha -cne $spec.sourceSha -or $original.success -isnot [bool] -or $original.success) { throw 'Ожидался исходный failed pair той же ревизии' }

        # Единственный поддержанный legacy control flow: проверен при диагностике этого helper.
        # Другой helper требует отдельного доказательства; raw success не заменяет process outcome.
        $supportedHash = 'b7986f5fc30f599b60532bfec04abd95e17264753dab897dd75caadc3c94e2be'
        if ($spec.helper.sha256 -cne $supportedHash -or $spec.helper.commit -cne $spec.sourceSha -or $spec.helper.gitBlob -cnotmatch '^[0-9a-f]{40}$') { throw 'Нет поддержанного control-flow evidence исходного helper' }
        $blob = & git --no-pager -C $RepoRoot rev-parse ($spec.helper.commit + ':Tools/release/Build-Game.ps1')
        if ($LASTEXITCODE -ne 0 -or "$blob".Trim() -cne $spec.helper.gitBlob) { throw 'Helper blob не принадлежит исходному commit' }
        $startInfo = New-Object Diagnostics.ProcessStartInfo
        $startInfo.FileName = 'git'
        $startInfo.Arguments = 'cat-file blob ' + $spec.helper.gitBlob
        $startInfo.WorkingDirectory = $RepoRoot
        $startInfo.UseShellExecute = $false
        $startInfo.CreateNoWindow = $true
        $startInfo.RedirectStandardOutput = $true
        $process = [Diagnostics.Process]::Start($startInfo)
        $buffer = New-Object IO.MemoryStream
        try {
            $process.StandardOutput.BaseStream.CopyTo($buffer)
            $process.WaitForExit()
            $sha = [Security.Cryptography.SHA256]::Create()
            try { $helperHash = ([BitConverter]::ToString($sha.ComputeHash($buffer.ToArray()))).Replace('-', '').ToLowerInvariant() } finally { $sha.Dispose() }
            if ($process.ExitCode -ne 0 -or $helperHash -cne $supportedHash) { throw 'Не совпали исходные байты helper' }
        } finally { $buffer.Dispose(); $process.Dispose() }

        $console = @(Get-Content -LiteralPath $spec.console.path -Encoding UTF8)
        $manifestMarker = 'Build manifest: ' + $spec.originalPair.recordedPath
        $failureMarker = 'Provenance не подтверждает текущие успешные builder artifacts; смотри pair manifest.'
        if ($console.Count -lt 2 -or $console[-2] -cne $manifestMarker -or $console[-1] -cne $failureMarker) { throw 'Console не подтверждает terminal pairRunSuccess исходного failed pair' }
        $profiles = @($original.profiles)
        if ($profiles.Count -eq 0 -or @($spec.profiles).Count -ne $profiles.Count) { throw 'Неполное соответствие профилей recovery input и исходного pair' }
        $originalGenerated = ConvertTo-BuildTimestamp $original.generatedUtc
        $expectedRuns = @(Get-BuildRuns (($profiles | ForEach-Object { $_.profile }) -join ','))
        $header = 'Профили: ' + (($profiles | ForEach-Object { $_.profile }) -join ',') + ', конфигурация: ' + $spec.configuration + ', ревизия: ' + $spec.sourceSha.Substring(0, 12) + ', запусков Unity: ' + $expectedRuns.Count
        if (@($console | Where-Object { $_ -ceq $header }).Count -ne 1) { throw 'Console configuration/source/profiles не совпали' }
        $consoleRunIndex = 0
        $previousLine = [Array]::IndexOf($console, $header)
        foreach ($expectedRun in $expectedRuns) {
            $consoleRunIndex++
            $group = @($profiles | Where-Object { ($expectedRun.Profiles -split ',') -ccontains $_.profile })
            $checkouts = @($group | ForEach-Object { [IO.Path]::GetDirectoryName([IO.Path]::GetDirectoryName([string]$_.outputRoot)) } | Select-Object -Unique)
            if ($checkouts.Count -ne 1) { throw 'Несогласованное соответствие профилей и исходного checkout' }
            $line = 'Запуск ' + $consoleRunIndex + '/' + $expectedRuns.Count + ': профили ' + $expectedRun.Profiles + ', -buildTarget ' + $expectedRun.BuildTarget + ', worktree ' + $checkouts[0]
            $lineIndex = [Array]::IndexOf($console, $line)
            if ($lineIndex -le $previousLine -or @($console | Where-Object { $_ -ceq $line }).Count -ne 1) { throw 'Console не подтверждает порядок и mapping исходных запусков' }
            $previousLine = $lineIndex
        }
        $runs = New-Object System.Collections.Generic.List[object]
        $heads = @{}
        $terminals = @{}
        $roots = @{}
        $attempts = New-Object System.Collections.Generic.List[object]
        $uxr = @()
        $index = 0
        foreach ($profile in $profiles) {
            $name = [string]$profile.profile
            if (@('quest', 'tablet', 'server') -cnotcontains $name -or $roots.ContainsKey($name)) { throw 'Неизвестный или повторный профиль исходного pair' }
            $mapping = @($spec.profiles | Where-Object { $_.profile -ceq $name })
            if ($mapping.Count -ne 1) { throw 'Неоднозначное соответствие профиля архиву' }
            $map = $mapping[0]
            $archive = (Resolve-Path -LiteralPath $map.archiveRoot).Path
            $rawPath = Get-ContainedArtifactPath $archive 'build.json'
            Assert-FileHash $rawPath $map.manifestSha256
            $checkout = [IO.Path]::GetDirectoryName([IO.Path]::GetDirectoryName([string]$profile.outputRoot))
            $folder = @{ quest = 'Quest'; tablet = 'Tablet'; server = 'Server' }[$name]
            if ([IO.Path]::GetFullPath($profile.outputRoot) -ine [IO.Path]::GetFullPath((Join-Path $checkout ('Build/' + $folder)))) { throw 'Неподдержанный исходный outputRoot' }
            $target = 'Android'
            if ($name -eq 'server') { $target = 'Win64' }
            $index++
            $runName = 'reaggregate-' + $index
            $runs.Add([pscustomobject]@{ Name = $runName; BuildTarget = $target; Profiles = $name; ProjectPath = $checkout })
            $heads[$checkout] = $spec.sourceSha
            $terminal = @{ startedUtc = (ConvertTo-BuildTimestamp $profile.terminal.startedUtc); endedUtc = (ConvertTo-BuildTimestamp $profile.terminal.endedUtc); exitCode = $null }
            if ($terminal.endedUtc -gt $originalGenerated) { throw 'Исходный pair предшествует окончанию попытки' }
            $terminals[$runName] = $terminal
            $validated = Read-ValidatedBuildProfile $rawPath $archive $profile.outputRoot $name $spec.sourceSha $spec.configuration $terminal $true
            $toolBinding = @($validated.Record.actual.tools.files | Where-Object { $_.path -ceq 'Tools/release/Build-Game.ps1' -and $_.sha256 -ceq $helperHash })
            if ($toolBinding.Count -ne 1) { throw 'Raw manifest не связан с исходным helper' }
            $roots[$name] = $archive
            $uxr += Read-UxrIdHash $archive
            $attempts.Add([ordered]@{ profile = $name; startedUtc = $terminal.startedUtc.ToString('o'); endedUtc = $terminal.endedUtc.ToString('o'); exitCode = $null; outcome = 'success'; outcomeSource = 'control-flow-derived'; originalOutputRoot = $profile.outputRoot; archiveRoot = $archive; manifestSha256 = $map.manifestSha256; verifiedFiles = @($validated.Record.files).Count })
        }
        if (@($uxr | Select-Object -Unique).Count -ne 1) { throw 'Кросс-сверка UXR архивов не сошлась' }
        $evidence = [ordered]@{
            kind = 'saved-artifacts-reaggregation'; generatedUtc = [DateTimeOffset]::UtcNow.ToString('o')
            input = @{ path = $inputFull; sha256 = $inputHash }
            originalPair = $spec.originalPair; console = $spec.console; helper = $spec.helper
            outcomeEvidence = 'control-flow-derived; original per-process Process.ExitCode was not preserved; terminal console marker in the byte-identified helper requires pairRunSuccess after process exit and UXR checks'
            attempts = $attempts.ToArray(); uxrHash = $uxr[0]
        }
        $script:Config = $spec.configuration
        $result = Write-BuildPairManifest $runs.ToArray() $heads $terminals $true @{ Roots = $roots; OutcomeConfirmed = $true; Evidence = $evidence; OutputDirectory = $output }
        Write-Host ('Reaggregated build manifest: ' + $result.Path)
        return $result
    } catch {
        $failure = @{ schemaVersion = 1; success = $false; kind = 'reaggregation-rejected'; reason = $_.Exception.Message; input = $InputPath; generatedUtc = [DateTimeOffset]::UtcNow.ToString('o') }
        $json = ($failure | ConvertTo-Json -Depth 10) -replace "`r`n", "`n"
        [IO.File]::WriteAllText((Join-Path $output 'reaggregation-failure.json'), $json + "`n", (New-Object Text.UTF8Encoding($false)))
        throw
    }
}

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if (-not [string]::IsNullOrWhiteSpace($ReaggregateInput)) {
    try {
        $result = Invoke-BuildReaggregation $ReaggregateInput $ReaggregateOutputDirectory
        if ($result.Passed) { exit 0 }
        exit 1
    } catch {
        Write-Host ('Повторная агрегация отклонена: ' + $_.Exception.Message) -ForegroundColor Red
        exit 1
    }
}
if (-not [string]::IsNullOrWhiteSpace($ReaggregateOutputDirectory)) { throw '-ReaggregateOutputDirectory требует -ReaggregateInput' }
if ($Development) { $Config = 'Test' }

try {
    $runs = @(Get-BuildRuns $Targets)
} catch {
    Write-Host ('Профили: ' + $_.Exception.Message) -ForegroundColor Red
    exit 2
}

if ([string]::IsNullOrEmpty($Unity)) {
    $versionLine = Select-String -Path (Join-Path $RepoRoot 'ProjectSettings\ProjectVersion.txt') -Pattern '^m_EditorVersion: (.+)$'
    $version = $versionLine.Matches[0].Groups[1].Value.Trim()
    $Unity = "C:\Program Files\Unity\Hub\Editor\$version\Editor\Unity.exe"
}

if (-not (Test-Path $Unity)) {
    Write-Host "Не найден Unity: $Unity. Укажи путь параметром -Unity." -ForegroundColor Red
    exit 2
}

$cfgLower = $Config.ToLowerInvariant()
$defaultWin64Worktree = "F:\CodexWorktrees\build-$cfgLower-win64\Vr_Battlegrounds_ai"
$defaultAndroidWorktree = "F:\CodexWorktrees\build-$cfgLower-android\Vr_Battlegrounds_ai"

if ($UseCurrentWorktree) {
    $resolvedWin64Worktree = $RepoRoot
    $resolvedAndroidWorktree = $RepoRoot
} else {
    if (-not [string]::IsNullOrWhiteSpace($Win64Worktree)) {
        if (-not (Test-Path -LiteralPath $Win64Worktree)) {
            Write-Host "Не найден Win64 сборочный worktree: $Win64Worktree" -ForegroundColor Red
            exit 2
        }
        $resolvedWin64Worktree = (Resolve-Path -LiteralPath $Win64Worktree).Path
    } elseif (Test-Path -LiteralPath $defaultWin64Worktree) {
        $resolvedWin64Worktree = (Resolve-Path -LiteralPath $defaultWin64Worktree).Path
    } else {
        $resolvedWin64Worktree = $RepoRoot
    }

    if (-not [string]::IsNullOrWhiteSpace($AndroidWorktree)) {
        if (-not (Test-Path -LiteralPath $AndroidWorktree)) {
            Write-Host "Не найден Android сборочный worktree: $AndroidWorktree" -ForegroundColor Red
            exit 2
        }
        $resolvedAndroidWorktree = (Resolve-Path -LiteralPath $AndroidWorktree).Path
    } elseif (Test-Path -LiteralPath $defaultAndroidWorktree) {
        $resolvedAndroidWorktree = (Resolve-Path -LiteralPath $defaultAndroidWorktree).Path
    } else {
        $resolvedAndroidWorktree = $RepoRoot
    }
}

foreach ($run in $runs) {
    $wt = if ($run.BuildTarget -eq 'Android') { $resolvedAndroidWorktree } else { $resolvedWin64Worktree }
    $run | Add-Member -NotePropertyName ProjectPath -NotePropertyValue $wt -Force
}

$activeWorktrees = @($runs | ForEach-Object { $_.ProjectPath } | Select-Object -Unique)

# Проверяем всю группу до переключения любого checkout и гидратации LFS.
foreach ($wt in $activeWorktrees) {
    if (Test-EditorLocked $wt) {
        Write-Host "Проект открыт в редакторе ($wt). Закрой его или собирай из меню Tools/VR Battlegrounds/Release." -ForegroundColor Red
        exit 2
    }
}

# Выделенные сборочные checkout — все, кроме текущего проекта.
$dedicatedWorktrees = @($activeWorktrees | Where-Object { -not $UseCurrentWorktree -and $_ -ine $RepoRoot })

$logDir = Join-Path $RepoRoot 'Logs'
New-Item -ItemType Directory -Force $logDir | Out-Null
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'

if (-not [string]::IsNullOrWhiteSpace($Ref)) {
    $targetSha = (& git -C $RepoRoot rev-parse "$Ref^{commit}" 2>$null)
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($targetSha)) {
        Write-Host "Не удалось разрешить git ref '$Ref' в $RepoRoot." -ForegroundColor Red
        exit 2
    }
    $targetSha = $targetSha.Trim()
}

# Проверка всей группы до любых изменений: неожиданные правки отменяют -Ref, известные generated-файлы
# выделенных checkout возвращаются к индексу (DryRun только сообщает).
$preBuildGenerated = @{}
foreach ($wt in $activeWorktrees) {
    try {
        $blocking = @(Get-BlockingChanges $wt)
        if ($dedicatedWorktrees -contains $wt) { $preBuildGenerated[$wt] = @((Get-BuildCheckoutChanges $wt).Generated) }
    } catch {
        Write-Host ('Отказ сборки: ' + $_.Exception.Message) -ForegroundColor Red
        exit 2
    }
    if ($blocking.Count -gt 0 -and -not [string]::IsNullOrWhiteSpace($Ref)) {
        Write-Host "Сборочный worktree $wt содержит незакоммиченные правки в Assets/Packages/ProjectSettings; переключение на -Ref '$Ref' отменено." -ForegroundColor Red
        $blocking | Select-Object -First 10 | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
        exit 2
    }
}
foreach ($wt in $preBuildGenerated.Keys) {
    $paths = @($preBuildGenerated[$wt])
    if ($paths.Count -eq 0) { continue }
    if ($DryRun) {
        Write-Host ("[DryRun] Будут возвращены к индексу generated-файлы Unity в {0}: {1}" -f $wt, ($paths -join ', '))
        continue
    }
    try {
        Restore-GeneratedBuildChanges $wt $paths (Join-Path $logDir ('build-' + $stamp + '-generated-before-' + (Get-BuildCheckoutLabel $wt) + '.patch'))
    } catch {
        Write-Host ('Отказ сборки: ' + $_.Exception.Message) -ForegroundColor Red
        exit 2
    }
}

if (-not [string]::IsNullOrWhiteSpace($Ref)) {
    foreach ($wt in $activeWorktrees) {
        if (-not $DryRun) {
            Write-Host "Переключение $wt на $targetSha ($Ref)..."
            & git -C $wt checkout --detach $targetSha
            if ($LASTEXITCODE -ne 0) {
                Write-Host "Не удалось переключить $wt на $targetSha." -ForegroundColor Red
                exit 2
            }
            $lfsScript = Join-Path $RepoRoot 'Tools\agents\configure_lfs_worktree.py'
            if (Test-Path -LiteralPath $lfsScript) {
                & python -X utf8 $lfsScript hydrate --worktree $wt | Out-Null
                if ($LASTEXITCODE -ne 0) {
                    Write-Host "Не удалось гидратировать LFS в $wt." -ForegroundColor Red
                    exit 2
                }
            }
        }
    }
}

$worktreeHeads = @{}
$worktreeRoles = @{}

foreach ($wt in $activeWorktrees) {
    if (Test-EditorLocked $wt) {
        Write-Host "Проект открыт в редакторе ($wt). Закрой его или собирай из меню Tools/VR Battlegrounds/Release." -ForegroundColor Red
        exit 2
    }

    $lfsRole = (& git -C $wt config --worktree --get vrb.lfs.role 2>$null)
    if ($null -ne $lfsRole) { $lfsRole = "$lfsRole".Trim() } else { $lfsRole = '' }
    $worktreeRoles[$wt] = $lfsRole

    if ($lfsRole -eq 'agent') {
        Write-Host "Отказ сборки: worktree $wt имеет роль vrb.lfs.role=agent (pointer-only). Используй выделенные сборочные worktree build-$cfgLower-* или основной checkout." -ForegroundColor Red
        exit 2
    }

    try {
        $pointers = @(Get-LfsPointerFiles $wt)
    } catch {
        Write-Host ('Отказ сборки: ' + $_.Exception.Message) -ForegroundColor Red
        exit 2
    }
    if ($pointers.Count -gt 0) {
        $sample = ($pointers | Select-Object -First 5) -join ', '
        Write-Host "Отказ сборки: в $wt обнаружены негидратированные Git LFS pointer-файлы ($($pointers.Count) шт., например: $sample)." -ForegroundColor Red
        exit 2
    }

    $headSha = (& git -C $wt rev-parse HEAD 2>$null)
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($headSha)) {
        Write-Host "Не удалось определить HEAD SHA в $wt." -ForegroundColor Red
        exit 2
    }
    $worktreeHeads[$wt] = $headSha.Trim()
}

if ($activeWorktrees.Count -gt 1) {
    $uniqueHeads = @($worktreeHeads.Values | Select-Object -Unique)
    if ($uniqueHeads.Count -gt 1) {
        Write-Host 'Отказ сборки: сборочные worktree находятся на разных коммитах:' -ForegroundColor Red
        foreach ($wt in $activeWorktrees) {
            Write-Host ("  {0} -> {1}" -f $wt, $worktreeHeads[$wt]) -ForegroundColor Red
        }
        Write-Host 'Выровняй ревизии параметром -Ref <commit> или git checkout.' -ForegroundColor Red
        exit 2
    }

    foreach ($wt in $activeWorktrees) {
        try { $dirty = @(Get-BlockingChanges $wt) } catch { Write-Host ('Отказ сборки: ' + $_.Exception.Message) -ForegroundColor Red; exit 2 }
        if ($dirty.Count -gt 0) {
            Write-Host "Отказ парной сборки: в $wt есть незакоммиченные изменения в Assets/Packages/ProjectSettings, которых нет во втором сборочном worktree." -ForegroundColor Red
            $dirty | Select-Object -First 10 | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
            exit 2
        }
    }
}

$primaryWt = $activeWorktrees[0]
$shortSha = (& git -C $primaryWt rev-parse --short=12 HEAD 2>$null).Trim()
try { $primaryDirty = @(Get-BlockingChanges $primaryWt) } catch { Write-Host ('Отказ сборки: ' + $_.Exception.Message) -ForegroundColor Red; exit 2 }
$sharedRevision = if ($primaryDirty.Count -eq 0) { $shortSha } else { "$shortSha-dirty" }

Write-Host "Unity: $Unity"
Write-Host "Профили: $Targets, конфигурация: $Config, ревизия: $sharedRevision, запусков Unity: $($runs.Count)"
foreach ($wt in $activeWorktrees) {
    Write-Host ("Worktree: {0} (HEAD={1}, lfs.role={2})" -f $wt, $worktreeHeads[$wt].Substring(0, 12), $(if ($worktreeRoles[$wt]) { $worktreeRoles[$wt] } else { 'default' }))
}

if ($DryRun) {
    $index = 0
    foreach ($run in $runs) {
        $index++
        $log = Join-Path $logDir ('build-' + $stamp + '-' + $run.Name + '.log')
        $otherRoots = @($activeWorktrees | Where-Object { $_ -ne $run.ProjectPath } | ForEach-Object { Join-Path $_ 'Build' })
        $arguments = @(
            '-batchmode', '-quit',
            '-projectPath', "`"$($run.ProjectPath)`"",
            '-buildTarget', $run.BuildTarget,
            '-executeMethod', 'VrBattlegrounds.EditorTools.Release.GameBuilder.RunBatch',
            '-buildProfile', $run.Profiles,
            '-logFile', "`"$log`"",
            '-config', $Config
        )
        if ($Config -eq 'Test') {
            $arguments += @('-buildRevision', $sharedRevision)
        }
        foreach ($extRoot in $otherRoots) {
            $arguments += @('-compareBuildRoot', "`"$extRoot`"")
        }
        Write-Host ''
        Write-Host "[DryRun] Запуск $index/$($runs.Count): профили $($run.Profiles), -buildTarget $($run.BuildTarget), worktree $($run.ProjectPath)"
        Write-Host "[DryRun] Команда: `"$Unity`" $($arguments -join ' ')"
    }
    exit 0
}

$buildTerminal = @{}
$pairRunSuccess = $false
try {
$index = 0
foreach ($run in $runs) {
    $index++
    $log = Join-Path $logDir ('build-' + $stamp + '-' + $run.Name + '.log')
    $otherRoots = @($activeWorktrees | Where-Object { $_ -ne $run.ProjectPath } | ForEach-Object { Join-Path $_ 'Build' })

    $arguments = @(
        '-batchmode', '-quit',
        '-projectPath', "`"$($run.ProjectPath)`"",
        '-buildTarget', $run.BuildTarget,
        '-executeMethod', 'VrBattlegrounds.EditorTools.Release.GameBuilder.RunBatch',
        '-buildProfile', $run.Profiles,
        '-logFile', "`"$log`"",
        '-config', $Config
    )
    if ($Config -eq 'Test') {
        $arguments += @('-buildRevision', $sharedRevision)
    }
    foreach ($extRoot in $otherRoots) {
        $arguments += @('-compareBuildRoot', "`"$extRoot`"")
    }

    Write-Host ''
    Write-Host "Запуск $index/$($runs.Count): профили $($run.Profiles), -buildTarget $($run.BuildTarget), worktree $($run.ProjectPath)"
    Write-Host "Лог: $log"

    $terminal = @{ startedUtc = [DateTimeOffset]::UtcNow; endedUtc = $null; exitCode = $null }
    $buildTerminal[$run.Name] = $terminal
    $process = Start-Process -FilePath $Unity -ArgumentList $arguments -NoNewWindow -PassThru -Wait
    $code = $process.ExitCode
    $terminal.endedUtc = [DateTimeOffset]::UtcNow.ToString('o')
    $terminal.exitCode = $code

    Show-LogResult $log

    if ($code -ne 0) {
        Write-Host "Unity завершился с кодом $code (профили $($run.Profiles))." -ForegroundColor Red
        exit 1
    }
}

# Итоговая кросс-сверка uxr-ids.txt между всеми собранными профилями (включая разные worktree).
$builtProfileHashes = @{}
$profileFolderMap = @{
    'server' = 'Server'
    'quest'  = 'Quest'
    'tablet' = 'Tablet'
}
foreach ($run in $runs) {
    foreach ($p in ($run.Profiles -split ',')) {
        $pTrim = $p.Trim().ToLowerInvariant()
        $folder = $profileFolderMap[$pTrim]
        $buildDir = Join-Path $run.ProjectPath "Build\$folder"
        $h = Read-UxrIdHash $buildDir
        if ([string]::IsNullOrWhiteSpace($h)) {
            Write-Host "Не найден отпечаток uxr-ids.txt для профиля $folder в $buildDir." -ForegroundColor Red
            exit 1
        }
        $builtProfileHashes["$folder ($buildDir)"] = $h
    }
}

$uniqueUxrHashes = @($builtProfileHashes.Values | Select-Object -Unique)
if ($uniqueUxrHashes.Count -gt 1) {
    Write-Host 'Кросс-сверка uxr-ids.txt не сошлась между собранными профилями:' -ForegroundColor Red
    foreach ($k in $builtProfileHashes.Keys) {
        Write-Host ("  {0}: {1}" -f $k, $builtProfileHashes[$k]) -ForegroundColor Red
    }
    exit 1
}

$pairRunSuccess = $true
} finally {
    # Unity переписала generated-файлы при запуске и сборке; возвращаем их, чтобы checkout остался чистым.
    # Неожиданные изменения не трогаем: следующий -Ref на них откажет. Ошибка здесь не меняет итог сборки.
    # sha256 generated-файлов до возврата — доказательство для пометки dirty-записей builder manifests.
    $generatedAfterBuild = @{}
    if ($buildTerminal.Count -gt 0) {
        foreach ($wt in $dedicatedWorktrees) {
            try {
                if (Test-EditorLocked $wt) {
                    Write-Host "Unity ещё держит $wt; generated-файлы не возвращены." -ForegroundColor Yellow
                    continue
                }
                $after = Get-BuildCheckoutChanges $wt
                if ($after.Generated.Count -gt 0) {
                    $hashes = @{}
                    foreach ($path in $after.Generated) {
                        $hashes[$path] = (Get-FileHash -LiteralPath (Join-Path $wt $path) -Algorithm SHA256).Hash.ToLowerInvariant()
                    }
                    $generatedAfterBuild[$wt] = $hashes
                    Restore-GeneratedBuildChanges $wt $after.Generated (Join-Path $logDir ('build-' + $stamp + '-generated-after-' + (Get-BuildCheckoutLabel $wt) + '.patch'))
                }
                if ($after.Unexpected.Count -gt 0) {
                    Write-Host "После сборки в $wt остались изменения вне известных generated-файлов; следующий -Ref откажет:" -ForegroundColor Yellow
                    $after.Unexpected | Select-Object -First 10 | ForEach-Object { Write-Host "  $_" -ForegroundColor Yellow }
                }
            } catch {
                Write-Host ("Generated-файлы в {0} не возвращены: {1}" -f $wt, $_.Exception.Message) -ForegroundColor Yellow
            }
        }
    }
    try {
        $pairManifest = Write-BuildPairManifest $runs $worktreeHeads $buildTerminal $pairRunSuccess $null $generatedAfterBuild
        Write-Host ('Build manifest: ' + $pairManifest.Path)
        if ($pairRunSuccess -and -not $pairManifest.Passed) {
            Write-Host 'Provenance не подтверждает текущие успешные builder artifacts; смотри pair manifest.' -ForegroundColor Red
            exit 1
        }
    } catch {
        Write-Host ('Не удалось сохранить pair build manifest: ' + $_.Exception.Message) -ForegroundColor Red
        if ($pairRunSuccess) { exit 1 }
    }
}

Write-Host 'Готово. Собранные профили:' -ForegroundColor Green
foreach ($k in $builtProfileHashes.Keys) {
    Write-Host ("  {0} (uxr-ids: {1})" -f $k, $builtProfileHashes[$k]) -ForegroundColor Green
}
exit 0
