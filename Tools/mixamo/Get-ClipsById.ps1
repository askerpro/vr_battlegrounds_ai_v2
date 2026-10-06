# Скачать клипы Mixamo по id продукта (ссылки .../products/<id>?... со страницы клипа или из списка).
# Без «In Place» (с root motion), без скина, FBX 2019, 30 fps. Файл: <Prefix><Имя>_<первые 8 знаков id>.fbx — у
# одноимённых клипов Mixamo разные id.
# Пример: .\Tools\mixamo\Get-ClipsById.ps1 -ListFile clips.txt -Prefix Sit_ -OutDir tmp\mixamo_sit
#         .\Tools\mixamo\Get-ClipsById.ps1 -Ids c9cad347-b96c-11e4-a802-0aaa78deedf9 -OutDir tmp\mixamo_one
param(
  [string[]]$Ids,
  [string]$ListFile,
  [string]$Prefix = '',
  [Parameter(Mandatory)] [string]$OutDir
)
. (Join-Path $PSScriptRoot 'MixamoCommon.ps1')

$all = @($Ids | Where-Object { $_ })
if ($ListFile) {
  $all += [regex]::Matches((Get-Content -Raw $ListFile), 'products/([0-9a-f\-]{36})') | ForEach-Object { $_.Groups[1].Value }
}
$all = $all | Select-Object -Unique
if (-not $all) { throw 'Нет id: -Ids или -ListFile со ссылками .../products/<id>.' }

New-Item -ItemType Directory -Force $OutDir | Out-Null
foreach ($id in $all) {
  $d = Get-MixamoProduct $id
  $name = $Prefix + ((Get-Culture).TextInfo.ToTitleCase($d.name) -replace '[^A-Za-z0-9]', '') + '_' + $id.Substring(0, 8)
  $file = Join-Path $OutDir "$name.fbx"
  "{0} | {1} | {2} с: {3}" -f $name, $d.description, $d.details.duration, (Export-MixamoMotion $d.details.gms_hash $d.name $file)
}
