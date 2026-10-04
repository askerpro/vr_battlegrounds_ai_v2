// Компилирует точный хвост execute_code вне Unity; генерируемые файлы — только tmp/.
const fs = require('node:fs');
const path = require('node:path');
const child = require('node:child_process');
const root = path.resolve(__dirname, '../../..');
const cache = path.join(root, 'Library/PackageCache');
const packageName = fs.readdirSync(cache).find(name => name.startsWith('com.unity.nuget.newtonsoft-json@'));
if (!packageName) throw new Error('В Library/PackageCache отсутствует Newtonsoft.Json');
const dll = path.join(cache, packageName, 'Runtime/Newtonsoft.Json.dll');
const directory = path.join(root, 'tmp/mcp-output-guard-tests');
fs.mkdirSync(directory, { recursive: true });
const tail = fs.readFileSync(path.join(root, 'Tools/UnityMcp/ReportSummary.cs.txt'), 'utf8');
const program = `using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
class Program {
  static object Summarize(object report) {
${tail}
  }
  static void Verify(bool condition, string message) {
    if (!condition) throw new Exception(message);
  }
  static void Main() {
    var failures = Enumerable.Range(0, 8880).Select(i => "native component delta " + (-24536354 - i)).ToArray();
    var summary = (JObject)Summarize(new { components = 17416, assets = 5, failures, ready = true });
    Verify((int)summary["failureCount"] == 8880, "lost failure count");
    Verify(!(bool)summary["passed"], "failed check became green");
    Verify(((JArray)summary["failureSample"]).Count == 10, "unbounded samples");
    Verify((int)summary["failureGroups"][0]["count"] == 8880, "incorrect grouping");
    var complete = JObject.Parse(File.ReadAllText((string)summary["reportPath"]));
    Verify(((JArray)complete["failures"]).Count == 8880, "incomplete file");
    var success = (JObject)Summarize(new { failures = new string[0] });
    Verify((bool)success["passed"], "empty successful check");
    var explicitFailure = (JObject)Summarize(new { passed = false, failures = new string[0] });
    Verify(!(bool)explicitFailure["passed"], "explicit failure lost");
    var contradictory = (JObject)Summarize(new { passed = true, failures = new[] { "error" } });
    Verify(!(bool)contradictory["passed"], "contradictory success accepted");
    var sampled = (JObject)Summarize(new { passed = true, failureCount = 8880, failures = new[] { "sample" } });
    Verify((int)sampled["failureCount"] == 8880 && !(bool)sampled["passed"], "explicit total lost");
    bool rejected = false;
    try { Summarize(new { ready = true }); } catch (InvalidOperationException) { rejected = true; }
    Verify(rejected, "missing failures accepted");
    Console.WriteLine("PASS source summary: complete file, bounded sample, grouping, status, malformed report");
    Console.WriteLine("summaryChars=" + summary.ToString(Newtonsoft.Json.Formatting.None).Length);
  }
}`;
const xmlPath = dll.replace(/&/g, '&amp;').replace(/</g, '&lt;');
fs.writeFileSync(path.join(directory, 'Program.cs'), program, 'utf8');
fs.writeFileSync(path.join(directory, 'Guard.csproj'), `<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework></PropertyGroup><ItemGroup><Reference Include="Newtonsoft.Json"><HintPath>${xmlPath}</HintPath></Reference></ItemGroup></Project>`, 'utf8');
fs.writeFileSync(path.join(directory, 'NuGet.Config'), '<configuration><packageSources><clear /></packageSources></configuration>', 'utf8');
for (const args of [
  ['restore', path.join(directory, 'Guard.csproj'), '--configfile', path.join(directory, 'NuGet.Config'), '--verbosity', 'quiet'],
  ['run', '--project', path.join(directory, 'Guard.csproj'), '--no-restore', '--verbosity', 'quiet']
]) {
  const result = child.spawnSync('dotnet', args, {
    cwd: root, encoding: 'utf8', windowsHide: true,
    // Изолируем пользовательские кэши стенда внутри tmp; системные настройки не меняем.
    env: { ...process.env, APPDATA: directory, DOTNET_CLI_HOME: directory,
      NUGET_PACKAGES: path.join(directory, 'packages'), DOTNET_NOLOGO: '1',
      DOTNET_ADD_GLOBAL_TOOLS_TO_PATH: '0' }
  });
  process.stdout.write(result.stdout || '');
  process.stderr.write(result.stderr || '');
  if (result.status !== 0) process.exit(result.status || 1);
}
