param(
    [string]$UnityEditor = 'C:/Program Files/Unity/Hub/Editor/6000.4.1f1/Editor',
    [string]$OutputDirectory = (Join-Path ([IO.Path]::GetTempPath()) ('editor-broker-compile-' + [guid]::NewGuid().ToString('N'))),
    [switch]$IncludeSmoke,
    [string]$McpEditorAssembly
)
$ErrorActionPreference = 'Stop'
$sdkRoot = Split-Path (Get-Command dotnet).Source
$sdkVersion = & dotnet --version
$compiler = Join-Path $sdkRoot ('sdk/' + $sdkVersion + '/Roslyn/bincore/csc.dll')
$references = @()
$references += Get-ChildItem -LiteralPath (Join-Path $UnityEditor 'Data/NetStandard/ref/2.1.0') -Filter '*.dll'
$references += Get-ChildItem -LiteralPath (Join-Path $UnityEditor 'Data/NetStandard/compat/2.1.0/shims/netfx') -Filter '*.dll'
$references += Get-ChildItem -LiteralPath (Join-Path $UnityEditor 'Data/Managed/UnityEngine') -Filter '*.dll'
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$response = Join-Path $OutputDirectory 'compile.rsp'
$source = Join-Path $OutputDirectory 'EditorBrokerBridge.cs'
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'EditorBrokerBridge.cs.txt') -Destination $source
$panelSource = Join-Path $OutputDirectory 'EditorBrokerHumanPanel.cs'
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'EditorBrokerHumanPanel.cs.txt') -Destination $panelSource
$arguments = @('/nostdlib+', '/target:library', '/langversion:9.0', '/warn:4',
    ('/out:"' + (Join-Path $OutputDirectory 'EditorBrokerLocal.dll') + '"'), ('"' + $source + '"'), ('"' + $panelSource + '"'))
$arguments += $references | ForEach-Object { '/reference:"' + $_.FullName + '"' }
if ($McpEditorAssembly) {
    $bootstrapSource = Join-Path $OutputDirectory 'WorkerMcpBootstrap.cs'
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'WorkerMcpBootstrap.cs.txt') -Destination $bootstrapSource
    $arguments += '"' + $bootstrapSource + '"'
    $arguments += '/reference:"' + [IO.Path]::GetFullPath($McpEditorAssembly) + '"'
}
if ($IncludeSmoke) {
    $smokeSource = Join-Path $OutputDirectory 'EditorBrokerSmoke.cs'
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'EditorBrokerSmoke.cs.txt') -Destination $smokeSource
    $arguments += '"' + $smokeSource + '"'
}
Set-Content -LiteralPath $response -Value $arguments -Encoding utf8
& dotnet $compiler '/noconfig' ('@' + $response) 2>&1 | Tee-Object -FilePath (Join-Path $OutputDirectory 'compile.log')
if ($LASTEXITCODE -ne 0) { throw 'Компиляция Unity bridge не прошла' }
@{passed=$true; unity_editor=$UnityEditor; include_smoke=[bool]$IncludeSmoke} |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputDirectory 'compile-result.json') -Encoding utf8
Write-Output ('compiled=' + (Join-Path $OutputDirectory 'EditorBrokerLocal.dll'))
