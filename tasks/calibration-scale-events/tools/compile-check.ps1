# Офлайн-компиляция сборок UltimateXR и VrBattlegrounds из ТЕКУЩЕГО worktree без Unity.
# Метаданные (csproj, DefineConstants, ссылки Library/ScriptAssemblies) — из основного checkout, только чтение.
# Выход — в $Work (по умолчанию %TEMP%). Это проверка компиляции C#, не замена Unity (weaver, импорт, тесты).
# Образец — tasks/bots-fix/tools/eq-round1/compile-check.ps1 (bots).
param(
    [string]$Work = (Join-Path $env:TEMP 'calibration-scale-events-compile'),
    [string]$MetadataRoot = 'F:/UnityProjects/Vr_Battlegrounds_ai'
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$csc = 'C:/Program Files/dotnet/sdk/9.0.311/Roslyn/bincore/csc.dll'
if (Test-Path -LiteralPath $Work) { $Work = $Work + '-' + [DateTime]::UtcNow.Ticks }
$out = Join-Path $Work 'out'
[IO.Directory]::CreateDirectory($out) | Out-Null
$built = @{}

function Get-Refs([xml]$project) {
    $refs = @($project.Project.ItemGroup.Reference | ForEach-Object {
        if ($_.HintPath) {
            $path = [string]$_.HintPath
            if (-not [IO.Path]::IsPathRooted($path)) { $path = Join-Path $MetadataRoot $path }
            $name = [IO.Path]::GetFileNameWithoutExtension($path)
            if ($built.ContainsKey($name)) { $path = $built[$name] }
            if (Test-Path -LiteralPath $path) { '/reference:"' + $path + '"' }
        }
    })
    $refs += @($project.SelectNodes('//ProjectReference') | ForEach-Object {
        $name = [IO.Path]::GetFileNameWithoutExtension($_.GetAttribute('Include'))
        $path = if ($built.ContainsKey($name)) { $built[$name] } else { Join-Path $MetadataRoot "Library/ScriptAssemblies/$name.dll" }
        '/reference:"' + $path + '"'
    })
    return $refs
}

function Compile([string]$assembly, [string]$sourceDirectory) {
    [xml]$project = Get-Content -Encoding utf8 -LiteralPath (Join-Path $MetadataRoot "$assembly.csproj")
    $refs = Get-Refs $project
    # Вложенные asmdef/asmref — отдельные сборки, их исходники исключаются.
    $nested = @(rg --files $sourceDirectory --glob '*.asmdef' --glob '*.asmref' |
        Where-Object { [IO.Path]::GetFileName($_) -ne "$assembly.asmdef" } |
        ForEach-Object { (Join-Path $repo ([IO.Path]::GetDirectoryName($_))) + '\' })
    $sources = @(rg --files $sourceDirectory --glob '*.cs' | ForEach-Object {
        $path = Join-Path $repo $_
        if (-not @($nested | Where-Object { $path.StartsWith($_, [StringComparison]::OrdinalIgnoreCase) }).Count) { '"' + $path + '"' }
    })
    $defines = [string]$project.SelectSingleNode('//DefineConstants').InnerText
    $dll = Join-Path $out "$assembly.dll"
    $options = @('/nologo', '/target:library', '/langversion:9', '/unsafe+', '/nostdlib+', '/warn:0', ('/define:' + $defines), ('/out:"' + $dll + '"'))
    $rsp = Join-Path $out "$assembly.rsp"
    [IO.File]::WriteAllLines($rsp, @($options + ($refs | Select-Object -Unique) + $sources), [Text.UTF8Encoding]::new($false))
    $log = Join-Path $out "$assembly.log"
    & dotnet $csc ('@' + $rsp) *> $log
    $code = $LASTEXITCODE
    $errors = @(Select-String -LiteralPath $log -Pattern ': error ')
    [pscustomobject]@{ assembly = $assembly; exitCode = $code; errors = $errors.Count; sources = $sources.Count; log = $log } | ConvertTo-Json -Compress
    if ($code -ne 0) { $errors | Select-Object -First 10 | ForEach-Object { $_.Line }; throw "Offline compilation failed: $assembly" }
    $built[$assembly] = $dll
}

Push-Location $repo
try {
    [pscustomobject]@{ source = $repo; head = (& git rev-parse HEAD); work = $Work } | ConvertTo-Json -Compress
    Compile 'UltimateXR' 'Assets/ThirdParty/UltimateXR/Runtime'
    Compile 'VrBattlegrounds' 'Assets/Scripts'
}
finally { Pop-Location }
