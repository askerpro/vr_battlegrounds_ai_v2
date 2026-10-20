# Actuator официального SDK. Сетевой сценарий и жизненный цикл остаются в Run-E2E.
function ConvertTo-RenderDocCommandLine([string[]]$Tokens) {
    (@($Tokens | ForEach-Object {
        if ($_ -notmatch '[\s"]') { $_ }
        else { '"' + ([regex]::Replace([regex]::Replace($_, '(\\*)"', '$1$1\"'), '(\\+)$', '$1$1')) + '"' }
    }) -join ' ')
}

function Start-E2ERenderDocPlayer {
    param([string]$Bridge, [string]$Player, [string[]]$PlayerArguments, [string]$RunDirectory,
          [AllowEmptyCollection()][Collections.IList]$OwnedProcesses)
    $directory = Join-Path $RunDirectory 'renderdoc'
    [IO.Directory]::CreateDirectory($directory) | Out-Null
    $receipt = Join-Path $directory 'launch.json'
    $command = ConvertTo-RenderDocCommandLine $PlayerArguments
    $tokens = @('launch', $Player, (Split-Path $Player), $command, (Join-Path $directory 'frame'), $receipt)
    $options = @{ FilePath=$Bridge; ArgumentList=(ConvertTo-RenderDocCommandLine $tokens);
        WorkingDirectory=(Split-Path $Bridge); WindowStyle='Hidden'; PassThru=$true;
        RedirectStandardOutput=(Join-Path $directory 'launch.stdout.log'); RedirectStandardError=(Join-Path $directory 'launch.stderr.log') }
    $helper = Start-Process @options
    # Регистрируем живого родителя до ожидания ответа SDK: cleanup видит и неизвестный исход.
    [void]$OwnedProcesses.Add($helper)
    $deadline = (Get-Date).AddSeconds(60)
    while (-not (Test-Path -LiteralPath $receipt)) {
        if ($helper.HasExited) { throw 'RenderDoc launch actuator завершился без receipt; см. launch.stdout.log' }
        if ((Get-Date) -ge $deadline) { throw 'RenderDoc launch timeout; повтор запрещён, cleanup принадлежит runner' }
        Start-Sleep -Milliseconds 200
    }
    $launch = Get-Content -LiteralPath $receipt -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($launch.status -cne 'launched' -or $launch.backendVersion -cne '1.46' -or $launch.playerPid -le 0 -or $launch.targetControlIdent -le 0) {
        throw 'Некорректный RenderDoc launch receipt'
    }
    $playerProcess = Get-Process -Id ([int]$launch.playerPid) -ErrorAction Stop
    [pscustomobject]@{ process=$playerProcess; bridge=$Bridge; directory=$directory; launch=$launch;
        bridgeSha256=(Get-FileHash -LiteralPath $Bridge -Algorithm SHA256).Hash.ToLowerInvariant();
        attempted=$false; ready=$null; eventsPath=$null; problem=$null }
}

function Update-E2ERenderDocCapture {
    param($State, [string]$StageRoot, [string]$RunId, [AllowEmptyCollection()][Collections.IList]$OwnedProcesses)
    if ($null -eq $State -or $State.attempted -or $State.problem) { return }
    $processes = Join-Path $StageRoot 'source/processes'
    if (-not (Test-Path -LiteralPath $processes)) { return }
    $candidates = @()
    foreach ($file in @(Get-ChildItem -LiteralPath $processes -Filter events.jsonl -Recurse -File)) {
        $events = @()
        foreach ($line in @(Get-Content -LiteralPath $file.FullName -Encoding UTF8)) {
            if (-not $line) { continue }
            try { $event = $line | ConvertFrom-Json } catch { continue } # Последняя строка ещё может записываться.
            if ($event.runId -ceq $RunId -and $event.role -ceq 'client-1') { $events += $event }
        }
        foreach ($event in @($events | Where-Object kind -ceq 'workload.ready')) {
            $invalidated = @($events | Where-Object { $_.kind -ceq 'workload.invalidated' -and $_.payload.readyId -ceq $event.payload.readyId })
            if ($invalidated.Count -eq 0) { $candidates += [pscustomobject]@{ event=$event; path=$file.FullName } }
        }
    }
    if ($candidates.Count -eq 0) { return }
    if ($candidates.Count -ne 1) { $State.problem='Неоднозначный active workload.ready для RenderDoc'; return }
    $State.attempted = $true
    $State.ready = $candidates[0].event
    $State.eventsPath = $candidates[0].path
    $State.ready | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath (Join-Path $State.directory 'ready.json') -Encoding UTF8
    $tokens = @('capture', [string]$State.launch.targetControlIdent, [string]$State.launch.playerPid,
                (Join-Path $State.directory 'capture.json'), '30')
    try {
        $options = @{ FilePath=$State.bridge; ArgumentList=(ConvertTo-RenderDocCommandLine $tokens);
            WorkingDirectory=(Split-Path $State.bridge); WindowStyle='Hidden'; PassThru=$true;
            RedirectStandardOutput=(Join-Path $State.directory 'capture.stdout.log'); RedirectStandardError=(Join-Path $State.directory 'capture.stderr.log') }
        $capture = Start-Process @options
        [void]$OwnedProcesses.Add($capture)
    } catch { $State.problem=$_.Exception.Message }
}

function Complete-E2ERenderDocCapture {
    param($State, [string]$RunId)
    if ($null -eq $State) { return $null }
    $problem = $State.problem
    $capture = $null
    $path = Join-Path $State.directory 'capture.json'
    if (-not $problem) {
        try {
            if (-not $State.attempted -or -not (Test-Path -LiteralPath $path)) { throw 'Capture receipt отсутствует; повтор не выполнялся' }
            $capture = Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json
            if ($capture.status -cne 'captured' -or $capture.playerPid -ne $State.launch.playerPid -or $capture.targetControlIdent -ne $State.launch.targetControlIdent) {
                throw 'Capture identity не совпала с launch'
            }
            $raw = [IO.Path]::GetFullPath([string]$capture.path)
            $prefix = [IO.Path]::GetFullPath($State.directory).TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar
            if (-not $raw.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Capture вне собственного run directory' }
            $file = Get-Item -LiteralPath $raw -ErrorAction Stop
            if ($file.Length -ne $capture.byteSize -or $file.Length -le 0) { throw 'Физический capture не совпал с SDK byteSize' }
            $capture | Add-Member -NotePropertyName sha256 -NotePropertyValue ((Get-FileHash -LiteralPath $raw -Algorithm SHA256).Hash.ToLowerInvariant())
        } catch { $problem=$_.Exception.Message }
    }
    [pscustomobject]@{ schemaVersion=1; status=$(if($problem){'incomplete'}else{'captured'});
        problem=$problem; runId=$RunId; mode='instrumented'; backend='RenderDoc'; backendVersion='1.46';
        bridgeSha256=$State.bridgeSha256; launch=$State.launch; capture=$capture;
        readyEventId=$State.ready.eventId; processInstanceId=$State.ready.processInstanceId;
        phaseGeneration=$State.ready.phaseGeneration;
        phaseMapping='candidate ready correlation; captured source-frame/clock mapping not verified';
        sourceIntervalsKnown=$false; replayExport='pending; execute bridge export after Player cleanup' }
}

Export-ModuleMember -Function Start-E2ERenderDocPlayer,Update-E2ERenderDocCapture,Complete-E2ERenderDocCapture
