[CmdletBinding()]
param([string]$Ticket, [string]$Token, [string]$StateDir, [switch]$OfflineEditor,
      [string]$PythonExecutable = 'python', [string]$ArchivePath)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$commit = '30d22075093d1d35dfb0091c1c7550e9ad948577'
$target = Join-Path $projectRoot 'Packages/com.coplaydev.unity-mcp'
$patch = Join-Path $PSScriptRoot 'discovery-10.2.0.patch'
$outputPatch = Join-Path $PSScriptRoot 'output-guard-10.2.0.patch'
$codexPatch = Join-Path $PSScriptRoot 'codex-config-10.2.0.patch'
$scriptLfPatch = Join-Path $PSScriptRoot 'script-lf-10.2.0.patch'
$expected = @{
 'Editor/MCPForUnity.Editor.asmdef' = '04EE726B8AF51854B8D7B5AD74859F9A0DC2BB04065068C2FA59472920E79A6A'
 'Editor/Services/ToolDiscoveryService.cs' = '15A013E071D1CED799D4DA23103286DABBBA50F816BB5CA49957C3AE1F02BB10'
 'Editor/Tools/CommandRegistry.cs' = 'D2CBAEDE1D68E5C54877E1E1976284EE86829AAF669E77F3333DA93E4AB61B86'
 'Editor/Tools/ExecuteCode.cs' = '53552BB02B3568F0B91E7289AD269469990E368F0238A115F7C968F7CF885726'
 'Editor/Tools/ManageScript.cs' = '792403CF258942342A4ED0BE95C01B9EB38A0E4AE8536C8A313F5596DB4B5C11'
}
function Assert-EditorAccess {
 $guardArgs = @((& $PythonExecutable -c "import sys; sys.path.insert(0, r'$projectRoot/Tools/agents'); import broker_runtime; print(broker_runtime.require() / 'editor_broker' / 'client_guard.py')").Trim(), '--project', $projectRoot)
 if ($Ticket) { $guardArgs += @('--ticket', $Ticket) }
 if ($Token) { $guardArgs += @('--token', $Token) }
 if ($StateDir) { $guardArgs += @('--state-dir', $StateDir) }
 if ($OfflineEditor) { $guardArgs += '--offline-editor' }
 $reply = & $PythonExecutable @guardArgs
 if ($LASTEXITCODE -ne 0) { throw "Editor broker отказал в записи: $reply" }
}
Assert-EditorAccess
if (Test-Path -LiteralPath $target) { throw 'Embedded-пакет уже существует. Не перезаписываю локальные правки.' }
$stage = Join-Path $projectRoot ('tmp/UnityMcpInstall/' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage -Force | Out-Null
if (!$ArchivePath) {
 $ArchivePath = Join-Path $stage 'upstream.zip'
 Invoke-WebRequest "https://api.github.com/repos/CoplayDev/unity-mcp/zipball/$commit" -Headers @{'User-Agent'='VR-Battlegrounds-MCP'} -OutFile $ArchivePath -TimeoutSec 60 -UseBasicParsing
}
# Распаковываем только Unity-пакет: TestProjects upstream превышают MAX_PATH в PowerShell 5.1.
Add-Type -AssemblyName System.IO.Compression.FileSystem
$sourceDirectory = Join-Path $stage 'source'
$archive = [IO.Compression.ZipFile]::OpenRead([IO.Path]::GetFullPath($ArchivePath))
try {
 foreach ($entry in $archive.Entries) {
  if ($entry.FullName -notmatch '^[^/]+/MCPForUnity/') { continue }
  $rootName = ($entry.FullName -split '/', 2)[0]
  $packagePrefix = [IO.Path]::GetFullPath((Join-Path $sourceDirectory "$rootName/MCPForUnity")) + [IO.Path]::DirectorySeparatorChar
  $destination = [IO.Path]::GetFullPath((Join-Path $sourceDirectory $entry.FullName))
  if (!$destination.StartsWith($packagePrefix, [StringComparison]::OrdinalIgnoreCase) -and $destination -ne $packagePrefix.TrimEnd([IO.Path]::DirectorySeparatorChar)) {
   throw 'Путь ZIP выходит за пределы Unity-пакета.'
  }
  if ($entry.FullName.EndsWith('/')) {
   [IO.Directory]::CreateDirectory($destination) | Out-Null
  } else {
   [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination)) | Out-Null
   [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $destination, $false)
  }
 }
}
finally { $archive.Dispose() }
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
 # Git checkout на Windows может дать патчам CRLF, а ZIP содержит LF.
 & git --no-pager -c core.autocrlf=false apply --ignore-space-change --check "--directory=$relativeRoot" $patch
 if ($LASTEXITCODE -ne 0) { throw 'Патч не применим.' }
 & git --no-pager -c core.autocrlf=false apply --ignore-space-change "--directory=$relativeRoot" $patch
 if ($LASTEXITCODE -ne 0) { throw 'Патч не применён.' }
 & git --no-pager -c core.autocrlf=false apply --ignore-space-change --check "--directory=$relativeRoot" $outputPatch
 if ($LASTEXITCODE -ne 0) { throw 'Патч ограничения вывода не применим.' }
 & git --no-pager -c core.autocrlf=false apply --ignore-space-change "--directory=$relativeRoot" $outputPatch
 if ($LASTEXITCODE -ne 0) { throw 'Патч ограничения вывода не применён.' }
 & git --no-pager -c core.autocrlf=false apply --ignore-space-change --check "--directory=$relativeRoot" $codexPatch
 if ($LASTEXITCODE -ne 0) { throw 'Патч конфигурации Codex не применим.' }
 & git --no-pager -c core.autocrlf=false apply --ignore-space-change "--directory=$relativeRoot" $codexPatch
 if ($LASTEXITCODE -ne 0) { throw 'Патч конфигурации Codex не применён.' }
 & git --no-pager -c core.autocrlf=false apply --ignore-space-change --check "--directory=$relativeRoot" $scriptLfPatch
 if ($LASTEXITCODE -ne 0) { throw 'Патч LF-писателя не применим.' }
 & git --no-pager -c core.autocrlf=false apply --ignore-space-change "--directory=$relativeRoot" $scriptLfPatch
 if ($LASTEXITCODE -ne 0) { throw 'Патч LF-писателя не применён.' }
 Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'OutputGuard/ExecuteCodeOutputGuard.cs') -Destination (Join-Path $packageRoot 'Editor/Helpers/ExecuteCodeOutputGuard.cs')
 Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'OutputGuard/ExecuteCodeOutputGuard.cs.meta.txt') -Destination (Join-Path $packageRoot 'Editor/Helpers/ExecuteCodeOutputGuard.cs.meta')
 Assert-EditorAccess
 # Копируется готовый пакет, а не редактируются активные исходники по одному.
 Copy-Item -LiteralPath $packageRoot -Destination $target -Recurse
}
finally { Pop-Location }
Write-Output "Установлен embedded MCP 10.2.0 из $commit. Unity Refresh и проверки выполняйте через editor-broker в своей аренде."
