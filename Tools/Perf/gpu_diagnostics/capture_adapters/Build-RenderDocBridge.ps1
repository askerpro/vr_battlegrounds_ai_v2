# Сборка узкого headless adapter против официального SDK, без установки backend.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$BackendRoot,
    [Parameter(Mandatory)][string]$SdkRoot,
    [Parameter(Mandatory)][string]$Out
)
$ErrorActionPreference = 'Stop'
$backend = (Resolve-Path -LiteralPath $BackendRoot).Path
$sdk = (Resolve-Path -LiteralPath $SdkRoot).Path
$output = [IO.Path]::GetFullPath($Out)
if (Test-Path -LiteralPath $output) { throw 'Выходной каталог уже существует; результаты прежней сборки не перезаписываются' }
$dll = Join-Path $backend 'renderdoc.dll'
if ((Get-AuthenticodeSignature -LiteralPath $dll).Status -ne 'Valid') { throw 'Нужна действительная подпись официального renderdoc.dll' }
if ((Get-Item -LiteralPath $dll).VersionInfo.FileVersion -ne '1.46.0.0') { throw 'Этот adapter закреплён за RenderDoc1.46 и совпадающими SDK headers' }
if (-not (Test-Path -LiteralPath (Join-Path $sdk 'renderdoc_replay.h'))) { throw 'SdkRoot должен содержать renderdoc_replay.h' }
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$vs = (& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath).Trim()
if (-not $vs) { throw 'Не найдены MSVC x64 build tools' }
$vc = (Get-ChildItem (Join-Path $vs 'VC/Tools/MSVC') -Directory | Sort-Object Name -Descending | Select-Object -First 1).FullName
$kit = (Get-ItemProperty 'HKLM:/SOFTWARE/Microsoft/Windows Kits/Installed Roots').KitsRoot10
$kitVersion = (Get-ChildItem (Join-Path $kit 'Include') -Directory | Where-Object { Test-Path (Join-Path $_.FullName 'ucrt') } | Sort-Object Name -Descending | Select-Object -First 1).Name
$bin = Join-Path $vc 'bin/Hostx64/x64'
[IO.Directory]::CreateDirectory($output) | Out-Null
$dump = & (Join-Path $bin 'dumpbin.exe') /NOLOGO /EXPORTS $dll
if ($LASTEXITCODE -ne 0) { throw 'Не удалось прочитать exports официального DLL' }
$exports = @($dump | ForEach-Object {
    if ($_ -match '^\s+\d+\s+[0-9A-F]+\s+[0-9A-F]+\s+(\S+)') {
        $name = $Matches[1]
        if ($name -in @('DllCanUnloadNow','DllGetClassObject')) { $name + ' PRIVATE' } else { $name }
    }
})
if ($exports.Count -eq 0) { throw 'DLL не содержит ожидаемой export table' }
$definition = Join-Path $output 'renderdoc.def'
[IO.File]::WriteAllText($definition, "LIBRARY renderdoc`nEXPORTS`n" + ($exports -join "`n") + "`n", [Text.UTF8Encoding]::new($false))
$library = Join-Path $output 'renderdoc.lib'
& (Join-Path $bin 'lib.exe') /NOLOGO /MACHINE:X64 "/DEF:$definition" "/OUT:$library" 2>&1 | Tee-Object -FilePath (Join-Path $output 'library.log')
if ($LASTEXITCODE -ne 0) { throw 'Не удалось создать import library из официального DLL' }
$compileArgs = @('/nologo','/std:c++17','/EHsc','/MD','/utf-8','/DRENDERDOC_PLATFORM_WIN32',
    "/I$sdk", "/I$(Join-Path $vc 'include')", "/I$(Join-Path $kit "Include/$kitVersion/ucrt")",
    "/I$(Join-Path $kit "Include/$kitVersion/um")", "/I$(Join-Path $kit "Include/$kitVersion/shared")",
    "/Fo$(Join-Path $output 'renderdoc_bridge.obj')", "/Fe$(Join-Path $output 'renderdoc_bridge.exe')",
    (Join-Path $PSScriptRoot 'renderdoc_bridge.cpp'), $library, '/link',
    "/LIBPATH:$(Join-Path $vc 'lib/x64')", "/LIBPATH:$(Join-Path $kit "Lib/$kitVersion/ucrt/x64")",
    "/LIBPATH:$(Join-Path $kit "Lib/$kitVersion/um/x64")")
$previousPath = $env:PATH
try {
    $env:PATH = $bin + [IO.Path]::PathSeparator + $previousPath
    & (Join-Path $bin 'cl.exe') @compileArgs 2>&1 | Tee-Object -FilePath (Join-Path $output 'compile.log')
    if ($LASTEXITCODE -ne 0) { throw 'Компиляция RenderDoc bridge не прошла' }
} finally { $env:PATH = $previousPath }
# Зависимости остаются возле нашего exe в новом каталоге отчёта.
foreach ($name in @('renderdoc.dll','dbghelp.dll','d3dcompiler_47.dll')) {
    Copy-Item -LiteralPath (Join-Path $backend $name) -Destination (Join-Path $output $name)
}
$receipt = [pscustomobject]@{ status='compiled'; backendVersion='1.46'; output=$output;
    compiler=$vc; windowsSdk=$kitVersion;
    sourceSha256=(Get-FileHash (Join-Path $PSScriptRoot 'renderdoc_bridge.cpp') -Algorithm SHA256).Hash;
    sdkHeaderSha256=(Get-FileHash (Join-Path $sdk 'renderdoc_replay.h') -Algorithm SHA256).Hash;
    backendSha256=(Get-FileHash $dll -Algorithm SHA256).Hash;
    executableSha256=(Get-FileHash (Join-Path $output 'renderdoc_bridge.exe') -Algorithm SHA256).Hash }
$receipt | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'build.json') -Encoding UTF8
$receipt | ConvertTo-Json -Compress
