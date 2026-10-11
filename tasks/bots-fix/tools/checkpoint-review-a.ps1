$ErrorActionPreference = 'Stop'
$packetRoot = Join-Path (Get-Location).Path 'tasks/bots-fix/reports/review-packet-a'
$packetManifest = Get-Content -LiteralPath (Join-Path $packetRoot 'manifest.json') -Raw -Encoding utf8 | ConvertFrom-Json
$packetGitDir = (& git rev-parse --absolute-git-dir).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Не найден Git worktree.' }
$packetCommonDir = (& git rev-parse --path-format=absolute --git-common-dir).Trim()
if ($LASTEXITCODE -ne 0 -or $packetGitDir -eq $packetCommonDir) { throw 'Checkpoint допустим только в linked worktree.' }
$packetHead = (& git rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $packetHead -ne $packetManifest.baseline) { throw 'База пакета отличается от HEAD; сначала пересобрать предложение.' }
foreach ($entry in $packetManifest.paths) {
    if ($entry.path -notlike 'Assets/*' -or $entry.path.Contains('..')) { throw 'Недопустимый путь пакета.' }
    $file = Join-Path (Join-Path $packetRoot 'source') $entry.path
    if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant() -ne $entry.sha256) {
        throw ('Изменён файл review-пакета: ' + $entry.path)
    }
}
$packetIndex = Join-Path $packetRoot ('technical-index-' + [Guid]::NewGuid().ToString('N'))
$packetPriorIndex = $env:GIT_INDEX_FILE
try {
    # Только отдельный индекс. Рабочие исходники и основной индекс сохраняются.
    $env:GIT_INDEX_FILE = $packetIndex
    & git read-tree $packetManifest.baseline
    if ($LASTEXITCODE -ne 0) { throw 'read-tree не выполнен.' }
    & git apply --cached --whitespace=nowarn (Join-Path $packetRoot 'proposed.patch')
    if ($LASTEXITCODE -ne 0) { throw 'Предложенный patch не применился.' }
    $packetPaths = @(& git diff --cached --name-only $packetManifest.baseline)
    if ($LASTEXITCODE -ne 0 -or $packetPaths.Count -ne $packetManifest.paths.Count -or
        @(Compare-Object @($packetManifest.paths.path | Sort-Object) @($packetPaths | Sort-Object)).Count -ne 0) {
        throw 'Изменён состав technical checkpoint.'
    }
    $packetTree = (& git write-tree).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'write-tree не выполнен.' }
    $packetSha = (& git commit-tree $packetTree -p $packetManifest.baseline -m 'checkpoint: NPC body review A; unaccepted').Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Technical commit-tree не выполнен.' }
    $packetRef = 'refs/heads/codex/tmp/bots-body-review-a-' + [Guid]::NewGuid().ToString('N')
    & git update-ref $packetRef $packetSha
    if ($LASTEXITCODE -ne 0) { throw 'Technical ref не создан.' }
    $packetResult = [pscustomobject]@{sha=$packetSha; tree=$packetTree; ref=$packetRef; baseline=$packetManifest.baseline; paths=$packetPaths; accepted=$false}
    $packetJson = $packetResult | ConvertTo-Json -Depth 4
    [IO.File]::WriteAllText((Join-Path $packetRoot 'checkpoint.json'), $packetJson, [Text.UTF8Encoding]::new($false))
    $packetJson
}
finally {
    $env:GIT_INDEX_FILE = $packetPriorIndex
    if (Test-Path -LiteralPath $packetIndex) { Remove-Item -LiteralPath $packetIndex }
}
