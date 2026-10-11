$ErrorActionPreference = 'Stop'
$handoffRoot = Join-Path (Get-Location).Path 'tasks/bots-fix/reports/weapon-binding-handoff'
$handoffManifest = [IO.File]::ReadAllText((Join-Path $handoffRoot 'manifest.json'), [Text.Encoding]::UTF8) | ConvertFrom-Json
$handoffGitDir = (& git rev-parse --absolute-git-dir).Trim()
if ($LASTEXITCODE -ne 0) { throw 'No Git worktree.' }
$handoffCommonDir = (& git rev-parse --path-format=absolute --git-common-dir).Trim()
if ($LASTEXITCODE -ne 0 -or $handoffGitDir -eq $handoffCommonDir) { throw 'Linked worktree required.' }
$handoffHead = (& git rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $handoffHead -ne $handoffManifest.baseline) { throw 'Candidate baseline differs from HEAD.' }
foreach ($entry in $handoffManifest.paths) {
    if ($entry.path -notlike 'Assets/*' -or $entry.path.Contains('..')) { throw 'Invalid candidate path.' }
    $file = Join-Path (Join-Path $handoffRoot 'candidate') $entry.path
    if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant() -ne $entry.candidateSha256) {
        throw ('Candidate content changed: ' + $entry.path)
    }
}
$handoffIndex = Join-Path $handoffRoot ('technical-index-' + [Guid]::NewGuid().ToString('N'))
$handoffPriorIndex = $env:GIT_INDEX_FILE
try {
    # Отдельный индекс: основной index, HEAD и рабочие исходники не меняются.
    $env:GIT_INDEX_FILE = $handoffIndex
    & git read-tree $handoffManifest.baseline
    if ($LASTEXITCODE -ne 0) { throw 'read-tree failed.' }
    & git apply --cached --whitespace=nowarn (Join-Path $handoffRoot 'proposed.patch')
    if ($LASTEXITCODE -ne 0) { throw 'Candidate patch failed.' }
    $handoffPaths = @(& git diff --cached --name-only $handoffManifest.baseline)
    if ($LASTEXITCODE -ne 0 -or $handoffPaths.Count -ne $handoffManifest.paths.Count -or
        @(Compare-Object @($handoffManifest.paths.path | Sort-Object) @($handoffPaths | Sort-Object)).Count -ne 0) {
        throw 'Checkpoint path set differs from manifest.'
    }
    $handoffTree = (& git write-tree).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'write-tree failed.' }
    $handoffSha = (& git commit-tree $handoffTree -p $handoffManifest.baseline -m 'checkpoint: equipment binding development candidate; unaccepted').Trim()
    if ($LASTEXITCODE -ne 0) { throw 'commit-tree failed.' }
    $handoffRef = 'refs/heads/codex/tmp/bots-binding-handoff-' + [Guid]::NewGuid().ToString('N')
    & git update-ref $handoffRef $handoffSha
    if ($LASTEXITCODE -ne 0) { throw 'Technical ref failed.' }
    $handoffResult = [pscustomobject]@{sha=$handoffSha;tree=$handoffTree;ref=$handoffRef;baseline=$handoffManifest.baseline;paths=$handoffPaths;accepted=$false}
    $handoffJson = $handoffResult | ConvertTo-Json -Depth 4
    [IO.File]::WriteAllText((Join-Path $handoffRoot 'checkpoint.json'), $handoffJson, [Text.UTF8Encoding]::new($false))
    $handoffJson
}
finally {
    $env:GIT_INDEX_FILE = $handoffPriorIndex
    if (Test-Path -LiteralPath $handoffIndex) { Remove-Item -LiteralPath $handoffIndex }
}
