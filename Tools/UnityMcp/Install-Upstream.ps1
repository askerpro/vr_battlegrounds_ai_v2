param([Parameter(Mandatory=$true)][string]$LockOwner, [string]$ArchivePath)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$commit = '30d22075093d1d35dfb0091c1c7550e9ad948577'
$target = Join-Path $projectRoot 'Packages/com.coplaydev.unity-mcp'
$patch = Join-Path $PSScriptRoot 'discovery-10.2.0.patch'
$expected = @{
 'Editor/MCPForUnity.Editor.asmdef' = '04EE726B8AF51854B8D7B5AD74859F9A0DC2BB04065068C2FA59472920E79A6A'
 'Editor/Services/ToolDiscoveryService.cs' = '15A013E071D1CED799D4DA23103286DABBBA50F816BB5CA49957C3AE1F02BB10'
 'Editor/Tools/CommandRegistry.cs' = 'D2CBAEDE1D68E5C54877E1E1976284EE86829AAF669E77F3333DA93E4AB61B86'
}
function Assert-OwnLock {
 $info = @{}
 Get-Content -LiteralPath (Join-Path $projectRoot 'tmp/unity-lock/info') | ForEach-Object {
  $pair = $_ -split '=', 2
  if ($pair.Count -eq 2) { $info[$pair[0]] = $pair[1] }
 }
 if ($info.owner -ne $LockOwner -or [long]$info.until -le [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()) {
  throw 'Нужен действующий Unity lock указанного владельца.'
 }
}
Assert-OwnLock
if (Test-Path -LiteralPath $target) { throw 'Embedded-пакет уже существует. Не перезаписываю локальные правки.' }
$stage = Join-Path $projectRoot ('tmp/UnityMcpInstall/' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage -Force | Out-Null
if (!$ArchivePath) {
 $ArchivePath = Join-Path $stage 'upstream.zip'
 Invoke-WebRequest "https://api.github.com/repos/CoplayDev/unity-mcp/zipball/$commit" -Headers @{'User-Agent'='VR-Battlegrounds-MCP'} -OutFile $ArchivePath -TimeoutSec 60 -UseBasicParsing
}
Expand-Archive -LiteralPath $ArchivePath -DestinationPath (Join-Path $stage 'source')
$roots = @(Get-ChildItem -LiteralPath (Join-Path $stage 'source') -Directory)
if ($roots.Count -ne 1) { throw 'Неожиданная структура upstream archive.' }
$packageRoot = Join-Path $roots[0].FullName 'MCPForUnity'
if ((Get-Content -LiteralPath "$packageRoot/package.json" -Raw | ConvertFrom-Json).version -ne '10.2.0') {
 throw 'Неожиданная версия пакета.'
}
foreach ($path in $expected.Keys) {
 if ((Get-FileHash -LiteralPath (Join-Path $packageRoot $path)).Hash -ne $expected[$path]) {
  throw "Исходник $path отличается от проверенного upstream; установка отменена."
 }
}
$relativeRoot = $packageRoot.Substring($projectRoot.Length + 1).Replace([IO.Path]::DirectorySeparatorChar, [char]'/')
Push-Location $projectRoot
try {
 & git -c core.autocrlf=false apply --check "--directory=$relativeRoot" $patch
 if ($LASTEXITCODE -ne 0) { throw 'Патч не применим.' }
 & git -c core.autocrlf=false apply "--directory=$relativeRoot" $patch
 if ($LASTEXITCODE -ne 0) { throw 'Патч не применён.' }
 Assert-OwnLock
 # Копируется готовый пакет, а не редактируются активные исходники по одному.
 Copy-Item -LiteralPath $packageRoot -Destination $target -Recurse
}
finally { Pop-Location }
Write-Output "Установлен embedded MCP 10.2.0 из $commit. Теперь выполните Unity Refresh и проверки."
