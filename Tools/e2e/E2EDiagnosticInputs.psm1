# Общий transport RunSpec: исходные байты не нормализуются и не становятся actual evidence.
$ErrorActionPreference = 'Stop'

function Get-E2EFileSha256([string] $Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256 -ErrorAction Stop).Hash.ToLowerInvariant()
}

function Assert-E2ESafePath([string] $Root, [string] $Path) {
    $rootPath = [IO.Path]::GetFullPath($Root).TrimEnd('\', '/')
    $full = [IO.Path]::GetFullPath($Path)
    if (-not $full.StartsWith($rootPath + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw ('путь вне разрешённого каталога: ' + $Path)
    }
    $cursor = $full
    while ($cursor) {
        if (Test-Path -LiteralPath $cursor) {
            if (((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw ('reparse point в пути: ' + $cursor)
            }
        }
        $cursor = [IO.Path]::GetDirectoryName($cursor)
    }
    return $full
}

function Resolve-E2EBundlePath([string] $Root, [string] $Relative) {
    if ([string]::IsNullOrWhiteSpace($Relative) -or [IO.Path]::IsPathRooted($Relative) -or
        $Relative -match '[:\\]' -or @($Relative.Split('/') | Where-Object { $_ -in @('', '.', '..') }).Count -gt 0) {
        throw ('небезопасный относительный путь bundle: ' + $Relative)
    }
    return Assert-E2ESafePath $Root (Join-Path $Root $Relative)
}

# Лексический проход нужен до ConvertFrom-Json: тот молча принимает duplicate keys/нестрогие числа.
function Read-E2EStrictJson([string] $Path) {
    $bytes = [IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -gt 1048576) { throw ('JSON превышает 1 MiB: ' + $Path) }
    $utf8 = New-Object Text.UTF8Encoding($false, $true)
    $json = $utf8.GetString($bytes).TrimStart([char]0xfeff)
    $state = @{ text = $json; offset = 0; depth = 0 }
    function Skip-Space { while ($state.offset -lt $state.text.Length -and $state.text[$state.offset] -cmatch '[ \t\r\n]') { $state.offset++ } }
    function Read-StringToken {
        $match = [regex]::Match($state.text.Substring($state.offset), '^"(?:[^"\\\x00-\x1f]|\\(?:["\\/bfnrt]|u[0-9a-fA-F]{4}))*"')
        if (-not $match.Success) { throw 'неверная JSON строка' }
        $state.offset += $match.Length
        return @(ConvertFrom-Json ('[' + $match.Value + ']'))[0]
    }
    function Read-ValueToken {
        Skip-Space
        $state.depth++
        if ($state.depth -gt 64 -or $state.offset -ge $state.text.Length) { throw 'неверная глубина/конец JSON' }
        $c = $state.text[$state.offset]
        if ($c -eq '{' -or $c -eq '[') {
            $object = $c -eq '{'
            $end = ']'; if ($object) { $end = '}' }
            $keys = New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
            $state.offset++; Skip-Space
            if ($state.offset -lt $state.text.Length -and $state.text[$state.offset] -eq $end) { $state.offset++ }
            else {
                while ($true) {
                    Skip-Space
                    if ($object) {
                        $key = Read-StringToken
                        if (-not $keys.Add($key)) { throw ('повтор JSON key: ' + $key) }
                        Skip-Space
                        if ($state.offset -ge $state.text.Length -or $state.text[$state.offset] -ne ':') { throw 'нет JSON двоеточия' }
                        $state.offset++
                    }
                    Read-ValueToken; Skip-Space
                    if ($state.offset -ge $state.text.Length) { throw 'незакрытый JSON container' }
                    $separator = $state.text[$state.offset]; $state.offset++
                    if ($separator -eq $end) { break }
                    if ($separator -ne ',') { throw 'нет JSON запятой' }
                }
            }
        }
        elseif ($c -eq '"') { $null = Read-StringToken }
        else {
            $match = [regex]::Match($state.text.Substring($state.offset), '^(?:true|false|null|-?(?:0|[1-9][0-9]*)(?:\.[0-9]+)?(?:[eE][+-]?[0-9]+)?)')
            if (-not $match.Success) { throw 'неверный JSON token' }
            if ($match.Value -match '^[0-9-]') {
                $number = [double]::Parse($match.Value, [Globalization.CultureInfo]::InvariantCulture)
                if ([double]::IsInfinity($number) -or [double]::IsNaN($number)) { throw 'nonfinite JSON number' }
            }
            $state.offset += $match.Length
        }
        $state.depth--
    }
    Read-ValueToken; Skip-Space
    if ($state.offset -ne $json.Length) { throw 'лишние JSON tokens' }
    return ConvertFrom-Json $json
}

function Assert-E2EFields($Value, [string[]] $Required, [string[]] $Optional = @()) {
    if ($Value -isnot [Management.Automation.PSCustomObject]) { throw 'ожидался JSON object' }
    foreach ($name in $Required) { if ($null -eq $Value.PSObject.Properties[$name]) { throw ('нет поля: ' + $name) } }
    foreach ($p in $Value.PSObject.Properties) { if (($Required + $Optional) -cnotcontains $p.Name) { throw ('неизвестное поле: ' + $p.Name) } }
}

function Assert-E2ENumber($Value, [string] $Name, [double] $Minimum, [switch] $Integer) {
    if ($null -eq $Value -or $Value -is [bool] -or $Value -is [string] -or
        $Value -isnot [ValueType] -or [double]::IsNaN([double]$Value) -or [double]::IsInfinity([double]$Value) -or
        [double]$Value -lt $Minimum -or ($Integer -and [double]$Value -ne [Math]::Floor([double]$Value))) { throw ('неверное число: ' + $Name) }
}

function Test-E2ECanonicalScene($Value) {
    return ($Value -is [string] -and -not [string]::IsNullOrWhiteSpace($Value) -and
        $Value.StartsWith('Assets/', [StringComparison]::Ordinal) -and $Value.EndsWith('.unity', [StringComparison]::Ordinal) -and
        $Value -notmatch '[\\:]' -and @($Value.Split('/') | Where-Object { $_ -in @('', '.', '..') }).Count -eq 0)
}

function Get-E2EHeadPose([string] $Value) {
    if ($Value -ieq 'off') { return [pscustomobject]@{ mode = 'off'; eye = $null; clientArg = $null } }
    if ($Value -ieq 'calib') { return [pscustomobject]@{ mode = 'calib'; eye = $null; clientArg = 'calib' } }
    if ($Value -notmatch '^\d+(\.\d+)?$') { throw 'HeadPose: нужны off, calib или высота глаз в метрах' }
    $eye = [double]::Parse($Value, [Globalization.CultureInfo]::InvariantCulture)
    if ($eye -lt 0.9 -or $eye -gt 2.5) { throw 'HeadPose: высота вне 0.9..2.5' }
    return [pscustomobject]@{ mode = 'eye'; eye = $eye; clientArg = 'eye=' + $eye.ToString('R', [Globalization.CultureInfo]::InvariantCulture) }
}

function Read-E2EViews([string] $Path) {
    $data = Read-E2EStrictJson $Path
    Assert-E2EFields $data @('views')
    if ($data.views -isnot [Array] -or $data.views.Count -eq 0) { throw 'views: нужен непустой массив' }
    $seen = New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    foreach ($view in $data.views) {
        Assert-E2EFields $view @('name', 'x', 'z', 'yaw') @('y')
        if ($view.name -isnot [string] -or $view.name -notmatch '^[A-Za-z0-9_.:/=+@%-]+$' -or -not $seen.Add($view.name)) { throw 'views: неверное/повторное имя' }
        foreach ($key in @('x', 'z', 'yaw', 'y')) {
            if ($null -ne $view.PSObject.Properties[$key]) { Assert-E2ENumber $view.$key $key ([double]::MinValue) }
        }
    }
    return [pscustomobject]@{ count = $data.views.Count; names = @($data.views | ForEach-Object { $_.name }); error = $null }
}

function Read-E2ERunSpec([string] $Path) {
    $pathFull = (Resolve-Path -LiteralPath $Path -ErrorAction Stop).Path
    $root = Split-Path -Parent $pathFull
    $null = Assert-E2ESafePath $root $pathFull
    $specHash = Get-E2EFileSha256 $pathFull
    $spec = Read-E2EStrictJson $pathFull
    if ((Get-E2EFileSha256 $pathFull) -cne $specHash) { throw 'RunSpec изменился при чтении' }
    Assert-E2EFields $spec @('schemaVersion','runId','pairId','variant','repetition','mode','sourceSha','buildManifestSha256','scenario','requestedSceneId','workloadId','phasePlanSha256','views','headPose','windows','quality','capture')
    if ($spec.schemaVersion -is [string] -or $spec.schemaVersion -is [bool] -or $spec.schemaVersion -ne 1) { throw 'RunSpec: неизвестная schemaVersion' }
    foreach ($name in @('runId')) {
        if ($spec.$name -isnot [string] -or $spec.$name -notmatch '^[A-Za-z0-9._-]{1,128}$' -or $spec.$name -in @('.', '..')) { throw ('RunSpec: неверный ' + $name) }
    }
    foreach ($name in @('pairId','variant','workloadId','scenario','requestedSceneId')) { if ($spec.$name -isnot [string] -or [string]::IsNullOrWhiteSpace($spec.$name) -or $spec.$name -match '[\x00-\x1f\x7f]') { throw ('RunSpec: неверный ' + $name) } }
    if (-not (Test-E2ECanonicalScene $spec.requestedSceneId)) { throw 'RunSpec: requestedSceneId требует canonical Assets/...unity' }
    if ($spec.mode -cnotin @('live','instrumented') -or $spec.sourceSha -isnot [string] -or $spec.sourceSha -cnotmatch '^[0-9a-fA-F]{40}$') { throw 'RunSpec: неверный mode/sourceSha' }
    foreach ($name in @('buildManifestSha256','phasePlanSha256')) { if ($spec.$name -isnot [string] -or $spec.$name -cnotmatch '^[0-9a-fA-F]{64}$') { throw ('RunSpec: неверный ' + $name) } }
    Assert-E2ENumber $spec.repetition 'repetition' 1 -Integer
    Assert-E2EFields $spec.headPose @('mode','eyeMeters')
    if ($spec.headPose.mode -cnotin @('off','eye')) { throw 'RunSpec: headPose mode только off|eye' }
    if ($spec.headPose.mode -ceq 'eye') { Assert-E2ENumber $spec.headPose.eyeMeters 'eyeMeters' 0.9; if ($spec.headPose.eyeMeters -gt 2.5) { throw 'eyeMeters >2.5' } }
    elseif ($null -ne $spec.headPose.eyeMeters) { throw 'headPose off требует eyeMeters=null' }
    Assert-E2EFields $spec.windows @('width','height','windowed')
    foreach ($n in @('width','height')) { Assert-E2ENumber $spec.windows.$n $n 1 -Integer; if ($spec.windows.$n -gt [int]::MaxValue) { throw 'window dimension overflow' } }
    if ($spec.windows.windowed -isnot [bool]) { throw 'windowed должен быть bool' }
    Assert-E2EFields $spec.quality @('profileId','requestedFingerprint')
    foreach ($n in @('profileId','requestedFingerprint')) { if ($spec.quality.$n -isnot [string] -or [string]::IsNullOrWhiteSpace($spec.quality.$n)) { throw ('quality: неверный ' + $n) } }
    Assert-E2EFields $spec.capture @('kind','minimumWindowSeconds')
    if ($spec.capture.kind -cnotin @('none','stage','draw')) { throw 'неизвестный capture kind' }
    Assert-E2ENumber $spec.capture.minimumWindowSeconds 'minimumWindowSeconds' 0
    $viewsPath = $null; $viewsInfo = $null
    if ($null -ne $spec.views) {
        Assert-E2EFields $spec.views @('path','sha256')
        $viewsPath = Resolve-E2EBundlePath $root $spec.views.path
        if ($spec.views.path -ieq 'build.json' -or $spec.views.path -ieq 'run-spec.json' -or $spec.views.path -match '^(?i)source(?:/|$)' -or $spec.views.path -match '^(?i)(?:build.json|run-spec.json)/') { throw 'views path конфликтует с source manifest' }
        if ($spec.views.sha256 -notmatch '^[0-9a-fA-F]{64}$' -or (Get-E2EFileSha256 $viewsPath) -ine $spec.views.sha256) { throw 'views SHA256 mismatch' }
        $viewsInfo = Read-E2EViews $viewsPath
    }
    $buildPath = Resolve-E2EBundlePath $root 'build.json'
    if ((Get-E2EFileSha256 $buildPath) -ine $spec.buildManifestSha256) { throw 'build.json SHA256 mismatch' }
    $build = Read-E2EStrictJson $buildPath
    if ((Get-E2EFileSha256 $buildPath) -ine $spec.buildManifestSha256) { throw 'build.json изменился при чтении' }
    if ($build.schemaVersion -is [bool] -or $build.schemaVersion -is [string] -or $build.schemaVersion -ne 1 -or $build.sourceSha -ine $spec.sourceSha -or $build.sourceKind -cnotin @('accepted','checkpoint','unknown') -or $build.profiles -isnot [Array]) { throw 'build identity/schema mismatch' }
    return [pscustomobject]@{ spec = $spec; routingMap = [IO.Path]::GetFileNameWithoutExtension($spec.requestedSceneId); path = $pathFull; sha256 = $specHash; buildPath = $buildPath; build = $build; viewsPath = $viewsPath; viewsInfo = $viewsInfo }
}

function Assert-E2EInputConflicts($InputBundle, $Bound, [string] $Scenario, [string] $Map, [string] $ViewsFile, [string] $HeadPose, [string[]] $ArgKeys) {
    $s = $InputBundle.spec
    if ($Bound.ContainsKey('Scenario') -and $Scenario -cne $s.scenario) { throw 'Scenario конфликтует с RunSpec' }
    # Legacy сценарии используют scene.name; полный путь остаётся frozen identity в исходном spec.
    if ($Bound.ContainsKey('Map') -and $Map -cne $s.requestedSceneId -and $Map -cne $InputBundle.routingMap) { throw 'Map конфликтует с RunSpec' }
    if ($ArgKeys -icontains 'views') { throw 'Arg views конфликтует с RunSpec' }
    if ($Bound.ContainsKey('ViewsFile')) {
        if (-not $ViewsFile -or -not $InputBundle.viewsPath -or (Get-E2EFileSha256 $ViewsFile) -ine $s.views.sha256) { throw 'ViewsFile конфликтует с RunSpec' }
    }
    if ($Bound.ContainsKey('HeadPose')) {
        $pose = Get-E2EHeadPose $HeadPose
        if ($pose.mode -cne $s.headPose.mode -or $pose.eye -ne $s.headPose.eyeMeters) { throw 'HeadPose конфликтует с RunSpec' }
    }
}

# Проверяется реальный запускаемый файл, а не short revision или requested CLI.
function Assert-E2EBuildArtifact($InputBundle, [string] $Profile, [string] $Artifact, [ValidateSet('server','client')] [string] $Role = 'client') {
    $profileNames = New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    foreach ($entry in $InputBundle.build.profiles) {
        if ($entry -isnot [Management.Automation.PSCustomObject] -or $entry.profile -cnotin @('server','quest','tablet','e2e') -or
            -not $profileNames.Add($entry.profile)) { throw 'build.json: неверный/повторный profile' }
    }
    # Для server role runtime допускает ровно один из server/e2e, без приоритета одному имени.
    $matches = @($InputBundle.build.profiles | Where-Object {
        if ($Role -ceq 'server') { $_.profile -cin @('server','e2e') } else { $_.profile -ceq $Profile }
    })
    if ($matches.Count -ne 1) { throw ('build.json: отсутствующий/неоднозначный profile для role=' + $Role + ', profile=' + $Profile) }
    $p = $matches[0]
    if ($p.success -isnot [bool] -or -not $p.success) { throw 'build.json: profile не подтверждает успешный artifact' }
    if ($p.actual.scenePaths -isnot [Array]) { throw 'build.json: нет actual.scenePaths evidence' }
    foreach ($scene in $p.actual.scenePaths) { if (-not (Test-E2ECanonicalScene $scene)) { throw 'build.json: неверный actual scene path' } }
    if ($p.actual.scenePaths -cnotcontains $InputBundle.spec.requestedSceneId) { throw 'build.json: requestedSceneId отсутствует в actual.scenePaths выбранной роли' }
    if (-not [IO.Path]::IsPathRooted([string]$p.outputRoot) -or $p.files -isnot [Array]) { throw 'build.json: нет actual outputRoot/files' }
    $artifactPath = [IO.Path]::GetFullPath($Artifact)
    $found = $false
    $declared = New-Object 'Collections.Generic.Dictionary[string,object]' ([StringComparer]::OrdinalIgnoreCase)
    foreach ($file in $p.files) {
        $full = Resolve-E2EBundlePath $p.outputRoot ([string]$file.path)
        if ($file.path -ieq 'build.json') { throw 'build.json inventory включает сам manifest' }
        if ($declared.ContainsKey([string]$file.path)) { throw ('build.json: повтор inventory path: ' + $file.path) }
        if ($file.sha256 -notmatch '^[0-9a-fA-F]{64}$') { throw 'build.json: неверный artifact SHA256' }
        Assert-E2ENumber $file.bytes 'artifact bytes' 0 -Integer
        if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { throw ('artifact inventory missing: ' + $full) }
        $declared.Add([string]$file.path, $file)
        if ($full -ieq $artifactPath) { $found = $true }
    }
    # Сверяется весь физический комплект, включая файлы, не перечисленные автором manifest.
    # Единственное исключение — собственный root build.json; wildcard service exceptions нет.
    $actualFiles = @(Get-E2ERawInventory $p.outputRoot | Where-Object { $_.path -ine 'build.json' })
    foreach ($actualFile in $actualFiles) {
        if (-not $declared.ContainsKey($actualFile.path)) { throw ('artifact inventory: неучтённый файл: ' + $actualFile.path) }
        $expectedFile = $declared[$actualFile.path]
        if ($actualFile.bytes -ne $expectedFile.bytes -or $actualFile.sha256 -ine $expectedFile.sha256) { throw ('artifact inventory mismatch: ' + $actualFile.path) }
    }
    if ($actualFiles.Count -ne $declared.Count) { throw 'artifact inventory: физический набор файлов не совпал с manifest' }
    if (-not $found) { throw ('запускаемый artifact отсутствует в успешном provenance: ' + $Artifact) }
    return $p
}

function Get-E2EPersistentDataPath([string] $RepoRoot) {
    $settings = Join-Path $RepoRoot 'ProjectSettings/ProjectSettings.asset'
    $company = Select-String -LiteralPath $settings -Pattern '^\s*companyName:\s*(.+?)\s*$' | Select-Object -First 1
    $product = Select-String -LiteralPath $settings -Pattern '^\s*productName:\s*(.+?)\s*$' | Select-Object -First 1
    if ($null -eq $company -or $null -eq $product) { throw 'нет companyName/productName для persistentDataPath' }
    return Join-Path $env:USERPROFILE ('AppData/LocalLow/' + $company.Matches[0].Groups[1].Value + '/' + $product.Matches[0].Groups[1].Value)
}

function Get-E2EBuildDataPath($Profile) {
    $company = $Profile.actual.companyName; $product = $Profile.actual.productName
    foreach ($value in @($company, $product)) {
        if ($value -isnot [string] -or [string]::IsNullOrWhiteSpace($value) -or $value -match '[\\/:<>"|?*\x00-\x1f]' -or $value -in @('.', '..')) { throw 'build actual: неизвестный/небезопасный companyName/productName' }
    }
    return Join-Path $env:USERPROFILE ('AppData/LocalLow/' + $company + '/' + $product)
}

function Copy-E2EVerifiedFile([string] $Source, [string] $Destination, [string] $Sha256) {
    if ((Get-E2EFileSha256 $Source) -ine $Sha256) { throw ('исходный файл изменился: ' + $Source) }
    New-Item -ItemType Directory -Path (Split-Path -Parent $Destination) -Force | Out-Null
    if (Test-Path -LiteralPath $Destination) { throw ('staging уже существует: ' + $Destination) }
    Copy-Item -LiteralPath $Source -Destination $Destination -ErrorAction Stop
    if ((Get-E2EFileSha256 $Destination) -ine $Sha256) { throw ('staging SHA256 mismatch: ' + $Destination) }
}

function New-E2EDiagnosticStage($InputBundle, [string] $PersistentRoot, [string] $Role, [string] $ProcessToken) {
    if ($Role -notmatch '^(server|client-[1-9][0-9]*)$' -or $ProcessToken -notmatch '^[A-Za-z0-9_-]+$') { throw 'неверная stage identity' }
    $relative = 'e2e-diagnostics/' + $InputBundle.spec.runId + '/' + $Role + '/' + $ProcessToken
    $root = Assert-E2ESafePath $PersistentRoot (Join-Path $PersistentRoot $relative)
    $specPath = Join-Path $root 'run-spec.json'
    Copy-E2EVerifiedFile $InputBundle.path $specPath $InputBundle.sha256
    Copy-E2EVerifiedFile $InputBundle.buildPath (Join-Path $root 'build.json') $InputBundle.spec.buildManifestSha256
    $viewsRelative = $null
    if ($InputBundle.viewsPath) {
        Copy-E2EVerifiedFile $InputBundle.viewsPath (Resolve-E2EBundlePath $root $InputBundle.spec.views.path) $InputBundle.spec.views.sha256
        $viewsRelative = $relative + '/' + $InputBundle.spec.views.path
    }
    return [pscustomobject]@{ role = $Role; root = $root; relative = $relative; path = $specPath; sha256 = $InputBundle.sha256; viewsRelative = $viewsRelative }
}

function Write-E2EDiagnosticJson([string] $Path, $Value) {
    [IO.File]::WriteAllText($Path, ($Value | ConvertTo-Json -Depth 32), (New-Object Text.UTF8Encoding($false)))
}

# Inventory снимается с raw source; сам inventory не включается в себя.
function Get-E2ERawInventory([string] $Root) {
    $null = Assert-E2ESafePath (Split-Path -Parent ([IO.Path]::GetFullPath($Root))) $Root
    $items = New-Object Collections.Generic.List[object]
    # Проверяем каталоги до рекурсии: не следуем junction/symlink даже ради inventory.
    $pending = New-Object Collections.Generic.Queue[string]
    $pending.Enqueue([IO.Path]::GetFullPath($Root))
    $allFiles = New-Object Collections.Generic.List[object]
    while ($pending.Count -gt 0) {
        foreach ($entry in Get-ChildItem -LiteralPath $pending.Dequeue() -Force -ErrorAction Stop) {
            $null = Assert-E2ESafePath $Root $entry.FullName
            if ($entry.PSIsContainer) { $pending.Enqueue($entry.FullName) }
            else { $allFiles.Add($entry) }
        }
    }
    foreach ($file in @($allFiles.ToArray() | Sort-Object FullName)) {
        $null = Assert-E2ESafePath $Root $file.FullName
        $items.Add([pscustomobject]@{ path = $file.FullName.Substring($Root.TrimEnd('\','/').Length + 1).Replace('\','/'); bytes = $file.Length; sha256 = Get-E2EFileSha256 $file.FullName })
    }
    return $items.ToArray()
}

function Copy-E2ESourceBundle($InputBundle, [string] $RunDirectory) {
    $source = Join-Path $RunDirectory 'source'
    Copy-E2EVerifiedFile $InputBundle.path (Join-Path $source 'run-spec.json') $InputBundle.sha256
    Copy-E2EVerifiedFile $InputBundle.buildPath (Join-Path $source 'build.json') $InputBundle.spec.buildManifestSha256
    if ($InputBundle.viewsPath) { Copy-E2EVerifiedFile $InputBundle.viewsPath (Join-Path $source 'views.json') $InputBundle.spec.views.sha256 }
}

function Get-E2EDiagnosticProblems([string] $HarvestRoot, $InputBundle, [string] $Role) {
    $problems = New-Object Collections.Generic.List[string]
    try {
        $inventory = @(Get-E2ERawInventory $HarvestRoot)
        $actualFiles = @($inventory | Where-Object { $_.path -match '(^|/)source/processes/[^/]+/actual.json$' })
        if ($actualFiles.Count -ne 1) { throw 'нет единственного actual.json текущего process' }
        $actualPath = Join-Path $HarvestRoot $actualFiles[0].path
        $actual = Read-E2EStrictJson $actualPath
        if ($actual.runId -cne $InputBundle.spec.runId -or $actual.role -cne $Role -or
            $actual.specSha256 -ine $InputBundle.sha256 -or $actual.buildManifestSha256 -ine $InputBundle.spec.buildManifestSha256) { throw 'actual identity/hash mismatch' }
        if ($actual.completed -cne $true -or $actual.status -cne 'completed') { $problems.Add($Role + ': diagnostic lifecycle incomplete (' + $actual.status + ')') }
        $eventsPath = Join-Path (Split-Path -Parent $actualPath) 'events.jsonl'
        if (-not (Test-Path -LiteralPath $eventsPath -PathType Leaf) -or (Get-Item -LiteralPath $eventsPath).Length -eq 0) { $problems.Add($Role + ': нет events.jsonl') }
        else {
            $stream = [IO.File]::OpenRead($eventsPath)
            try { $null = $stream.Seek(-1, [IO.SeekOrigin]::End); if ($stream.ReadByte() -ne 10) { $problems.Add($Role + ': неполный JSONL tail') } }
            finally { $stream.Dispose() }
        }
        $stageRoot = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $actualPath)))
        if ((Get-E2EFileSha256 (Join-Path $stageRoot 'run-spec.json')) -ine $InputBundle.sha256 -or
            (Get-E2EFileSha256 (Join-Path $stageRoot 'build.json')) -ine $InputBundle.spec.buildManifestSha256) { $problems.Add($Role + ': harvested bundle SHA256 mismatch') }
        if ($InputBundle.viewsPath -and (Get-E2EFileSha256 (Resolve-E2EBundlePath $stageRoot $InputBundle.spec.views.path)) -ine $InputBundle.spec.views.sha256) { $problems.Add($Role + ': harvested views SHA256 mismatch') }
    }
    catch { $problems.Add($Role + ': ' + $_.Exception.Message) }
    return $problems.ToArray()
}

function Save-E2ELocalArtifact([string] $Source, [string] $AllowedRoot, [string] $DestinationRoot) {
    $receipt = [ordered]@{ source = $Source; local = $null; ok = $false; detail = $null; files = @() }
    try {
        $full = Assert-E2ESafePath $AllowedRoot $Source
        if (-not (Test-Path -LiteralPath $full)) { throw 'нет на диске' }
        New-Item -ItemType Directory -Path $DestinationRoot -Force | Out-Null
        $local = Join-Path $DestinationRoot (([guid]::NewGuid().ToString('N')) + '-' + (Split-Path -Leaf $full))
        $isDirectory = Test-Path -LiteralPath $full -PathType Container
        if ($isDirectory) { $before = @(Get-E2ERawInventory $full) }
        else { $before = @([pscustomobject]@{ path = Split-Path -Leaf $full; bytes = (Get-Item -LiteralPath $full).Length; sha256 = Get-E2EFileSha256 $full }) }
        $receipt.local = $local
        Copy-Item -LiteralPath $full -Destination $local -Recurse -ErrorAction Stop
        if ($isDirectory) { $after = @(Get-E2ERawInventory $local) }
        else { $after = @([pscustomobject]@{ path = Split-Path -Leaf $full; bytes = (Get-Item -LiteralPath $local).Length; sha256 = Get-E2EFileSha256 $local }) }
        $receipt.files = $after
        if (($before | ConvertTo-Json -Depth 8 -Compress) -cne ($after | ConvertTo-Json -Depth 8 -Compress)) { throw 'скопированный inventory не совпал с source' }
        $receipt.ok = $true
    }
    catch { $receipt.detail = $_.Exception.Message }
    return [pscustomobject]$receipt
}

function Save-E2EDiagnosticHarvest([string] $RunDirectory, [object[]] $Receipts, [string[]] $Problems) {
    $source = Join-Path $RunDirectory 'source'
    New-Item -ItemType Directory -Path $source -Force | Out-Null
    Write-E2EDiagnosticJson (Join-Path $source 'harvest-receipts.json') ([ordered]@{ schemaVersion = 1; complete = ($Problems.Count -eq 0); problems = @($Problems); receipts = @($Receipts) })
    try { $inventory = @(Get-E2ERawInventory $RunDirectory | Where-Object { $_.path -ne 'source/raw-inventory.json' }) }
    catch {
        $inventoryError = $_.Exception.Message
        Write-E2EDiagnosticJson (Join-Path $source 'raw-inventory.json') ([ordered]@{ schemaVersion = 1; complete = $false; problems = @($Problems) + @('inventory: ' + $inventoryError); files = @() })
        throw
    }
    Write-E2EDiagnosticJson (Join-Path $source 'raw-inventory.json') ([ordered]@{ schemaVersion = 1; complete = ($Problems.Count -eq 0); problems = @($Problems); files = $inventory })
}

Export-ModuleMember -Function Get-E2EFileSha256,Assert-E2ESafePath,Read-E2ERunSpec,Assert-E2EInputConflicts,Assert-E2EBuildArtifact,Read-E2EViews,Get-E2EHeadPose,Get-E2EPersistentDataPath,Get-E2EBuildDataPath,Copy-E2EVerifiedFile,New-E2EDiagnosticStage,Write-E2EDiagnosticJson,Get-E2ERawInventory,Copy-E2ESourceBundle,Get-E2EDiagnosticProblems,Save-E2ELocalArtifact,Save-E2EDiagnosticHarvest
