# Визуальное сравнение вариантов render_diag на Windows e2e-плеере: штатный Run-E2E по каждому bundle,
# во время измеряемой фазы — скриншот окна клиента (capture_client_window.ps1). Не замер.
param(
    [Parameter(Mandatory = $true)][string] $SeriesDir,
    [Parameter(Mandatory = $true)][string] $Player,
    [int] $ShotDelaySeconds = 45
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$capture = Join-Path $PSScriptRoot 'capture_client_window.ps1'
foreach ($bundle in Get-ChildItem -LiteralPath $SeriesDir -Directory | Where-Object { $_.Name -notmatch 'build-inputs' } | Sort-Object Name) {
    $launch = Get-Content -LiteralPath (Join-Path $bundle.FullName 'launch-args.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $log = Join-Path $SeriesDir ($bundle.Name + '.runner.txt')
    $argList = @('-NoProfile', '-File', (Join-Path $repo 'Tools/e2e/Run-E2E.ps1'), '-Scenario', 'perf-stress', '-Map', 'Lobby',
        '-Clients', '1', '-Timeout', '300', '-Player', $Player, '-RunSpec', $launch.RunSpec, '-Arg', ($launch.Arg -join ','))
    $quoted = $argList | ForEach-Object { if ($_ -match '[\s"]') { '"' + ($_ -replace '"', '\"') + '"' } else { $_ } }
    $run = Start-Process -FilePath (Get-Process -Id $PID).Path -ArgumentList ($quoted -join ' ') -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput $log -RedirectStandardError ($log + '.err')
    $null = $run.Handle
    # Ждём окно клиента, затем момент внутри измеряемой фазы.
    $deadline = (Get-Date).AddSeconds(120)
    do {
        Start-Sleep -Seconds 2
        $client = Get-CimInstance Win32_Process -Filter "Name='VrBattlegrounds.exe'" | Where-Object { $_.CommandLine -match 'client-1' }
    } while ($null -eq $client -and (Get-Date) -lt $deadline -and -not $run.HasExited)
    if ($null -ne $client) {
        try { & $capture -Out (Join-Path $SeriesDir ($bundle.Name + '.png')) -WaitSeconds $ShotDelaySeconds } catch { Write-Output ('скриншот не снят: ' + $_.Exception.Message) }
    } else { Write-Output ($bundle.Name + ': окно клиента не появилось') }
    $run.WaitForExit()
    $verdict = Select-String -LiteralPath $log -Pattern 'ИТОГ:\s*(.+)' | Select-Object -Last 1
    Write-Output ($bundle.Name + ': exit ' + $run.ExitCode + ' ' + $(if ($verdict) { $verdict.Matches[0].Groups[1].Value } else { '' }))
}
