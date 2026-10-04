// Стенд компилирует реальный ограничитель пакета; Unity Editor не запускается.
const fs = require('node:fs');
const path = require('node:path');
const child = require('node:child_process');
const root = path.resolve(__dirname, '../../..');
const directory = path.join(root, 'tmp/mcp-native-output-tests');
fs.mkdirSync(directory, { recursive: true });
const cache = path.join(root, 'Library/PackageCache');
const packageName = fs.readdirSync(cache).find(name => name.startsWith('com.unity.nuget.newtonsoft-json@'));
if (!packageName) throw new Error('Newtonsoft.Json package is required');
const xml = value => value.replace(/&/g, '&amp;').replace(/</g, '&lt;');
const source = path.join(root, 'Tools/UnityMcp/OutputGuard/ExecuteCodeOutputGuard.cs');
const executeSource = process.argv[2] ? path.resolve(process.argv[2]) : path.join(root, 'tmp/mcp-output-patch/modified/ExecuteCode.cs');
const sdkList = child.spawnSync('dotnet', ['--list-sdks'], { encoding: 'utf8', windowsHide: true });
if (sdkList.status !== 0) throw new Error('Could not list .NET SDKs');
const sdkVersionResult = child.spawnSync('dotnet', ['--version'], { encoding: 'utf8', windowsHide: true });
if (sdkVersionResult.status !== 0) throw new Error('Could not resolve the selected .NET SDK');
const sdkVersion = sdkVersionResult.stdout.trim();
const sdkLine = sdkList.stdout.trim().split('\n').find(line => line.startsWith(sdkVersion + ' '));
if (!sdkLine) throw new Error('Selected .NET SDK not found in catalog');
const sdkRoot = sdkLine.match(/\[(.*)\]/)[1];
const codeDom = path.join(sdkRoot, sdkVersion, 'System.CodeDom.dll');
const dll = path.join(cache, packageName, 'Runtime/Newtonsoft.Json.dll');
fs.copyFileSync(path.join(__dirname, 'OutputGuardTests.cs.txt'), path.join(directory, 'Program.cs'));
fs.copyFileSync(path.join(__dirname, 'OutputGuardStubs.cs.txt'), path.join(directory, 'Stubs.cs'));
fs.writeFileSync(path.join(directory, 'Guard.csproj'), `<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><NoWarn>CA1416</NoWarn></PropertyGroup><ItemGroup><Compile Include="${xml(source)}"/><Compile Include="${xml(executeSource)}"/><Compile Include="${xml(path.join(root, 'Packages/com.coplaydev.unity-mcp/Editor/Helpers/Response.cs'))}"/><Compile Include="${xml(path.join(root, 'Packages/com.coplaydev.unity-mcp/Editor/Tools/McpForUnityToolAttribute.cs'))}"/><Reference Include="System.CodeDom"><HintPath>${xml(codeDom)}</HintPath></Reference><Reference Include="Newtonsoft.Json"><HintPath>${xml(dll)}</HintPath></Reference></ItemGroup></Project>`, 'utf8');
fs.writeFileSync(path.join(directory, 'NuGet.Config'), '<configuration><packageSources><clear /></packageSources></configuration>', 'utf8');
for (const args of [
  ['restore', path.join(directory, 'Guard.csproj'), '--configfile', path.join(directory, 'NuGet.Config'), '--verbosity', 'quiet'],
  ['run', '--project', path.join(directory, 'Guard.csproj'), '--no-restore', '--verbosity', 'quiet']
]) {
  const result = child.spawnSync('dotnet', args, { cwd: root, encoding: 'utf8', windowsHide: true,
    env: { ...process.env, APPDATA: directory, DOTNET_CLI_HOME: directory,
      NUGET_PACKAGES: path.join(directory, 'packages'), DOTNET_NOLOGO: '1', DOTNET_ADD_GLOBAL_TOOLS_TO_PATH: '0' } });
  process.stdout.write(result.stdout || '');
  process.stderr.write(result.stderr || '');
  if (result.status !== 0) process.exit(result.status || 1);
}
