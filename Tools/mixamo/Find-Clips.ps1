# Поиск клипов Mixamo: имя | описание | длительность. Одноимённых клипов много — выбирать по описанию и длительности.
# Пример: .\Tools\mixamo\Find-Clips.ps1 -Query 'Walking Backwards'
#         .\Tools\mixamo\Find-Clips.ps1 -Query 'rifle' -Type MotionPack
param([Parameter(Mandatory)] [string]$Query, [ValidateSet('Motion', 'MotionPack')] [string]$Type = 'Motion')
. (Join-Path $PSScriptRoot 'MixamoCommon.ps1')

foreach ($p in Find-MixamoProducts $Query $Type) {
  if ($Type -eq 'MotionPack') { "{0} | {1} | id={2}" -f $p.name, $p.description, $p.id; continue }
  $d = Get-MixamoProduct $p.id
  "{0} | {1} | {2} с | id={3}" -f $p.name, $p.description, $d.details.duration, $p.id
}
