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
    [switch] $DryRun
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
function Write-BuildPairManifest($Runs, $Heads, $TerminalRuns, [bool] $BuildPassed) {
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
            $manifestPath = Join-Path $root 'build.json'
            $record = $null
            $reason = 'Сборка этого профиля не запускалась в текущей попытке'
            if ($null -ne $terminal) {
                try {
                    $manifest = Get-Content -LiteralPath $manifestPath -Encoding UTF8 -Raw | ConvertFrom-Json
                    if ($manifest.schemaVersion -ne 1 -or $manifest.sourceSha -cne $Heads[$run.ProjectPath]) {
                        throw 'Версия или полный sourceSha builder manifest не совпали с текущим checkout'
                    }
                    $candidates = @($manifest.profiles | Where-Object { $_.profile -ceq $profile })
                    if ($candidates.Count -ne 1) { throw 'Builder manifest не содержит единственный ожидаемый профиль' }
                    $record = $candidates[0]
                    if ([IO.Path]::GetFullPath([string]$record.outputRoot) -ine $root) { throw 'Builder outputRoot не совпал с фактическим путём профиля' }
                    $start = [DateTimeOffset]::MinValue
                    $end = [DateTimeOffset]::MinValue
                    if (-not [DateTimeOffset]::TryParse([string]$record.terminal.startedUtc, [ref]$start) -or
                        -not [DateTimeOffset]::TryParse([string]$record.terminal.endedUtc, [ref]$end) -or
                        $start -lt $terminal.startedUtc -or $end -lt $start -or $end -gt [DateTimeOffset]::UtcNow) {
                        throw 'Builder manifest не принадлежит временному интервалу текущего запуска'
                    }
                    if ($record.success -isnot [bool] -or -not $record.success -or $record.terminal.result -cne 'Succeeded' -or
                        $terminal.exitCode -ne 0 -or @($record.files).Count -eq 0) {
                        $passed = $false
                        # Failure evidence сохраняется, но inventory старой/неуспешной сборки не принимается.
                        $record.success = $false
                        $record.files = @()
                        $record.actual.unavailableReasons | Add-Member -NotePropertyName files -NotePropertyValue 'Текущий terminal builder не подтвердил успешный artifact; inventory не принимается' -Force
                    }
                    $origins.Add([ordered]@{ path = $manifestPath; sha256 = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant() })
                    if ($manifest.dirty.value -isnot [bool]) { $dirtyKnown = $false }
                    elseif ($manifest.dirty.value) { $dirtyValue = $true }
                    foreach ($entry in @($manifest.dirty.entries)) {
                        $dirtyEntries.Add([ordered]@{ checkout = $run.ProjectPath; evidence = $entry })
                    }
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
                    profile = $profile; outputRoot = $root; files = @(); success = $false
                    actual = $unknownActual
                    terminal = @{ startedUtc = $(if ($terminal) { $terminal.startedUtc.ToString('o') } else { $null }); endedUtc = $(if ($terminal) { $terminal.endedUtc } else { $null }); result = $null; reason = $reason }
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
        dirty = @{ value = $dirty; entries = $dirtyEntries.ToArray(); reason = $(if (-not $dirtyKnown) { 'Часть builder manifests не предоставила dirty evidence' } else { $null }) }
        profiles = $profiles.ToArray(); sourceManifests = $origins.ToArray(); success = $passed
        generatedUtc = [DateTimeOffset]::UtcNow.ToString('o')
    }
    $id = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffffffZ') + '-' + [Guid]::NewGuid().ToString('N')
    $directory = Join-Path $RepoRoot ('Build/manifests/build-' + $id)
    $null = New-Item -ItemType Directory -Path $directory -Force
    $path = Join-Path $directory 'build.json'
    $json = ($pair | ConvertTo-Json -Depth 40) -replace "`r`n", "`n"
    [IO.File]::WriteAllText($path, $json + "`n", (New-Object Text.UTF8Encoding($false)))
    return [pscustomobject]@{ Path = $path; Passed = $passed }
}

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
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

if (-not [string]::IsNullOrWhiteSpace($Ref)) {
    $targetSha = (& git -C $RepoRoot rev-parse "$Ref^{commit}" 2>$null)
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($targetSha)) {
        Write-Host "Не удалось разрешить git ref '$Ref' в $RepoRoot." -ForegroundColor Red
        exit 2
    }
    $targetSha = $targetSha.Trim()

    foreach ($wt in $activeWorktrees) {
        $dirty = @(& git -C $wt status --porcelain -- Assets Packages ProjectSettings 2>$null)
        if ($dirty.Count -gt 0) {
            Write-Host "Сборочный worktree $wt содержит незакоммиченные правки в Assets/Packages/ProjectSettings; переключение на -Ref '$Ref' отменено." -ForegroundColor Red
            exit 2
        }
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
        $dirty = @(& git -C $wt status --porcelain -- Assets Packages ProjectSettings 2>$null)
        if ($dirty.Count -gt 0) {
            Write-Host "Отказ парной сборки: в $wt есть незакоммиченные изменения в Assets/Packages/ProjectSettings, которых нет во втором сборочном worktree." -ForegroundColor Red
            exit 2
        }
    }
}

$primaryWt = $activeWorktrees[0]
$shortSha = (& git -C $primaryWt rev-parse --short=12 HEAD 2>$null).Trim()
$primaryDirty = @(& git -C $primaryWt status --porcelain -- Assets Packages ProjectSettings 2>$null)
$sharedRevision = if ($primaryDirty.Count -eq 0) { $shortSha } else { "$shortSha-dirty" }

$logDir = Join-Path $RepoRoot 'Logs'
New-Item -ItemType Directory -Force $logDir | Out-Null
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'

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
    try {
        $pairManifest = Write-BuildPairManifest $runs $worktreeHeads $buildTerminal $pairRunSuccess
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
