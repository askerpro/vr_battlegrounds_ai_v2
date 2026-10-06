# Общее для скриптов Mixamo: токен и персонаж — из mixamo.txt в корне проекта (вне git, см. README.md).
$ErrorActionPreference = 'Stop'
$script:ProjectRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$tokenFile = Join-Path $script:ProjectRoot 'mixamo.txt'
if (-not (Test-Path $tokenFile)) { throw "Нет $tokenFile — сохрани туда 'Copy as cURL (cmd)' запроса export с mixamo.com (README.md)." }
$raw = Get-Content -Raw $tokenFile
$script:Token = [regex]::Match($raw, 'Bearer ([A-Za-z0-9\-_\.]+)').Groups[1].Value
$script:CharacterId = [regex]::Match($raw, 'character_id[^0-9a-f]+([0-9a-f\-]{36})').Groups[1].Value
if (-not $script:Token -or -not $script:CharacterId) { throw "В mixamo.txt не найден Bearer-токен или character_id." }

$script:Headers = @{
  'Authorization' = "Bearer $($script:Token)"; 'X-Api-Key' = 'mixamo2'; 'Accept' = 'application/json'
  'Origin' = 'https://www.mixamo.com'; 'Referer' = 'https://www.mixamo.com/'
}
$script:Api = 'https://www.mixamo.com/api/v1'

function Get-MixamoProduct([string]$id) {
  Invoke-RestMethod -Uri "$($script:Api)/products/$id`?similar=0&character_id=$($script:CharacterId)" -Headers $script:Headers -TimeoutSec 60
}

function Find-MixamoProducts([string]$query, [string]$type = 'Motion') {
  $q = [uri]::EscapeDataString($query)
  (Invoke-RestMethod -Uri "$($script:Api)/products?page=1&limit=96&type=$type&query=$q" -Headers $script:Headers -TimeoutSec 60).results
}

# Экспорт одного клипа (gms_hash из деталей продукта или пакета) и скачивание FBX. Без скина, 30 fps, без In Place.
function Export-MixamoMotion($gms, [string]$productName, [string]$file, [bool]$mirror = $false) {
  # Значения параметров клипа (Speed, Posture, Overdrive…) — по одному на пару, точкой. Конвейер PowerShell разворачивает
  # вложенные массивы: при нескольких парах значений уходило вдвое больше, и сервер падал «Unknown error while generating motion».
  $pairs = @($gms.params)
  if ($pairs.Count -gt 0 -and $pairs[0] -is [string]) { $pairs = @(, $pairs) } # одна пара — JSON развернул её в плоский массив
  $values = for ($i = 0; $i -lt $pairs.Count; $i++) { ([double]$pairs[$i][1]).ToString([Globalization.CultureInfo]::InvariantCulture) }
  $params = @($values) -join ','
  $trim = if ($gms.trim -is [string]) { @($gms.trim -split ' ' | ForEach-Object { [int][double]$_ }) } else { @($gms.trim) }
  $body = @{
    gms_hash = @(@{ 'model-id' = $gms.'model-id'; mirror = $mirror; trim = $trim; overdrive = 0; params = $params; 'arm-space' = 0; inplace = $false })
    preferences = @{ format = 'fbx7_2019'; skin = 'false'; fps = '30'; reducekf = '0' }
    character_id = $script:CharacterId; type = 'Motion'; product_name = $productName
  } | ConvertTo-Json -Depth 6 -Compress
  for ($attempt = 1; $attempt -le 3; $attempt++) {
    Invoke-RestMethod -Method Post -Uri "$($script:Api)/animations/export" -Headers $script:Headers -ContentType 'application/json; charset=UTF-8' -Body $body -TimeoutSec 60 | Out-Null
    for ($i = 0; $i -lt 60; $i++) {
      Start-Sleep -Seconds 2
      $m = Invoke-RestMethod -Uri "$($script:Api)/characters/$($script:CharacterId)/monitor" -Headers $script:Headers -TimeoutSec 60
      if ($m.status -eq 'completed') { Invoke-WebRequest -Uri $m.job_result -OutFile $file -TimeoutSec 120; return 'ok' }
      if ($m.status -eq 'failed') { break }
    }
    Start-Sleep -Seconds 3
  }
  return 'FAILED'
}
