# Скачать один клип Mixamo без «In Place» (с root motion), без скина, FBX 30 fps.
# Пример: .\Tools\mixamo\Get-Clip.ps1 -Name 'Walking' -Description 'Walking With A Swagger' -OutFile tmp\mixamo\Walking.fbx
#         -Mirror — зеркальная версия (вторая сторона диагонали).
param(
  [Parameter(Mandatory)] [string]$Name,
  [Parameter(Mandatory)] [string]$Description,
  [Parameter(Mandatory)] [string]$OutFile,
  [switch]$Mirror
)
. (Join-Path $PSScriptRoot 'MixamoCommon.ps1')

$p = Find-MixamoProducts $Name | Where-Object { $_.name -eq $Name -and $_.description -eq $Description } | Select-Object -First 1
if (-not $p) { throw "Не найден '$Name' с описанием '$Description' — посмотри варианты через Find-Clips.ps1." }
$d = Get-MixamoProduct $p.id
New-Item -ItemType Directory -Force (Split-Path -Parent $OutFile) | Out-Null
"{0}: {1}" -f $OutFile, (Export-MixamoMotion $d.details.gms_hash $Name $OutFile $Mirror.IsPresent)
