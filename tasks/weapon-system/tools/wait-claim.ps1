# Ждёт предложения тикета брокера и сразу забирает аренду (claim), чтобы предложение не истекло.
# Запуск в фоне: powershell -File tasks/weapon-system/tools/wait-claim.ps1 -Ticket <N> [-Owner <owner задачи>]
# Ответ claim — tasks/weapon-system/reports/lease/claim<N>.json (вне Git).
param([int]$Ticket, [string]$Owner = 'weapon-system-expert')
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
Set-Location $root
$dir = Join-Path $root 'tasks\weapon-system\reports\lease'
[IO.Directory]::CreateDirectory($dir) | Out-Null
for ($i = 0; $i -lt 12; $i++) {
    $out = & python -X utf8 Tools/agents/editor-broker.py watch-ticket --ticket $Ticket --owner $Owner --until offered --timeout 580 2>&1 | Out-String
    if ($out -match '"event": "offered"') {
        & python -X utf8 Tools/agents/editor-broker.py claim --ticket $Ticket --owner $Owner 2>&1 | Out-File -Encoding utf8 (Join-Path $dir "claim$Ticket.json")
        Write-Output "claimed $Ticket"
        exit 0
    }
    if ($out -notmatch '"phase": "QUEUED"') { Write-Output "stopped: $out"; exit 1 }
}
Write-Output "timeout waiting $Ticket"
exit 2
