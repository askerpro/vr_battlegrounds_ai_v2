# Продление допуска этапа coordination, пока пользователь проверяет в шлеме (TTL 7200, раз в 30 мин).
# Токен этапа (из ответа begin) положить в tasks/weapon-system/reports/permit.txt.
# Запуск в фоне: powershell -File tasks/weapon-system/tools/permit-renew.ps1
# Остановить: удалить tasks/weapon-system/reports/permit-renew.flag (хук может не дать остановить задачу иначе).
param([string]$TokenFile = 'tasks\weapon-system\reports\permit.txt')
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
Set-Location $root
$flag = Join-Path $root 'tasks\weapon-system\reports\permit-renew.flag'
New-Item -ItemType File -Force $flag | Out-Null
while (Test-Path $flag) {
    $token = (Get-Content (Join-Path $root $TokenFile) -Raw).Trim()
    $out = & python -X utf8 Tools/agents/coordination.py renew --token $token --ttl 7200 2>&1 | Out-String
    $line = (Get-Date -Format 'HH:mm:ss') + ' ' + $out.Substring(0, [Math]::Min(200, $out.Length))
    Set-Content -Path (Join-Path $root 'tasks\weapon-system\reports\permit-renew.last') -Value $line -Encoding utf8
    for ($i = 0; $i -lt 360 -and (Test-Path $flag); $i++) { Start-Sleep -Seconds 5 }
}
