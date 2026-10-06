# Скачать пакет Mixamo (MotionPack) по клипу: экспорт пакета целиком сервер часто отвергает («The job failed»).
# Без «In Place», без скина; прыжки, смерти, перезарядка и т.п. пропускаются (-Exclude).
# Пример: .\Tools\mixamo\Get-Pack.ps1 -PackId d8c7c8cb-6fdd-4590-a62a-1ff743a7cffd -Prefix Rifle_ -OutDir tmp\mixamo_rifle
#   d8c7c8cb-6fdd-4590-a62a-1ff743a7cffd — Pro Rifle Pack (= Rifle 8-Way Locomotion Pack c9d598f8-…)
#   c9d59aa3-b96c-11e4-a802-0aaa78deedf9 — Pistol/Handgun Locomotion Pack
#   c9d57b3f-b96c-11e4-a802-0aaa78deedf9 — Locomotion Pack (без оружия)
param(
  [Parameter(Mandatory)] [string]$PackId,
  [Parameter(Mandatory)] [string]$Prefix,
  [Parameter(Mandatory)] [string]$OutDir,
  [string]$Exclude = 'jump|death|dying|hit|reload|shoot|fire|equip|draw'
)
. (Join-Path $PSScriptRoot 'MixamoCommon.ps1')

New-Item -ItemType Directory -Force $OutDir | Out-Null
$d = Get-MixamoProduct $PackId
$keep = $d.details.motions | Where-Object { $_.name -notmatch $Exclude }
"{0}: {1} из {2} клипов" -f $d.name, $keep.Count, $d.details.motions.Count
foreach ($m in $keep) {
  $base = $Prefix + ((Get-Culture).TextInfo.ToTitleCase($m.name) -replace '[^A-Za-z0-9]', '')
  $file = Join-Path $OutDir "$base.fbx"
  $n = 2
  while (Test-Path $file) { $file = Join-Path $OutDir ("{0}_{1}.fbx" -f $base, $n); $n++ }
  "{0} ({1} с): {2}" -f [IO.Path]::GetFileNameWithoutExtension($file), $m.duration, (Export-MixamoMotion $m.gms_hash $d.name $file)
}
