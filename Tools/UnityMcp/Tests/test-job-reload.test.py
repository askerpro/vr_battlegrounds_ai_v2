"""Реальные C# manager/service/locator в новом AssemblyLoadContext, без Unity. --previous воспроизводит MCP2 RED."""
from pathlib import Path
import argparse,hashlib,os,subprocess,tempfile,zipfile
from xml.sax.saxutils import escape
root=Path(__file__).resolve().parents[3];tests=Path(__file__).resolve().parent
p=argparse.ArgumentParser(description=__doc__);p.add_argument('archive',type=Path);p.add_argument('--previous',action='store_true');p.add_argument('--callback-only',action='store_true',help='RED прежнего callback-only patch + pristine service');args=p.parse_args();assert not(args.previous and args.callback_only)
fixture=Path(tempfile.mkdtemp(prefix='mcp-reload-harness-',dir=root/'tmp')).resolve();assert fixture.parent==(root/'tmp').resolve()
source=fixture/'source';(source/'Editor/Services').mkdir(parents=True)
pins={'TestJobManager.cs':'80be4984f63398ba8d3484a514e131f131b9a562757e713b07d0ac73fc164476',
 'TestRunnerService.cs':'b5f25bffbcee394d746f4a67b7cfaba1bf3e24279f581d79fec7c20098d6ee23',
 'ITestRunnerService.cs':'73ecf527d561f2d474d7e5da06724ea529708bcbe7569c7b1b918b20766c0151',
 'MCPServiceLocator.cs':'a2f4a419dcbb0d2c985a8647ede6562c811220e80fd811d174d952712ca110cc'}
with zipfile.ZipFile(args.archive) as z:
 for name in ('TestJobManager.cs','TestRunnerService.cs','ITestRunnerService.cs','MCPServiceLocator.cs'):
  matches=[v for v in z.namelist() if v.endswith('/MCPForUnity/Editor/Services/'+name)];assert len(matches)==1
  original=z.read(matches[0]).replace(b'\r\n',b'\n');assert hashlib.sha256(original).hexdigest()==pins[name], 'Неизвестный pinned source '+name
  (source/'Editor/Services'/name).write_bytes(original)
patch=tests/'Fixtures/test-job-lifecycle-mcp2.patch' if args.previous else tests/'Fixtures/test-job-lifecycle-callback-only.patch' if args.callback_only else root/'Tools/UnityMcp/test-job-lifecycle-10.2.0.patch'
subprocess.run(['git','-c','core.autocrlf=false','apply','--directory='+source.relative_to(root).as_posix(),str(patch)],cwd=root,check=True)
manager=source/'Editor/Services/TestJobManager.cs';manager.write_bytes(manager.read_bytes().replace(b'\r\n',b'\n'))
if not(args.previous or args.callback_only):assert hashlib.sha256((source/'Editor/Services/TestRunnerService.cs').read_bytes().replace(b'\r\n',b'\n')).hexdigest()=='291801256c293ef07f51bf53668fec4a5a64e5cede7c89f755614d75f5b545f9'
if not args.previous:assert hashlib.sha256(manager.read_bytes()).hexdigest()=='cbc4952435ab4ef0a710726038eb4d4edd254e30592916d9b4c3d92e1bb0eaca'
if args.previous:assert hashlib.sha256((source/'Editor/Services/TestJobManager.cs').read_bytes()).hexdigest()=='395272a8507666a4d88d3b72e69023a03f25499712f1cb895877dfb2ccff4a01'
project=fixture/'TestJobReloadHarness.csproj'
project.write_bytes((tests/'TestJobReloadHarness.csproj.txt').read_text(encoding='utf-8-sig').replace('__SOURCE__',escape(source.as_posix())).replace('__HARNESS__',escape(tests.as_posix())).encode())
env=dict(os.environ,DOTNET_CLI_HOME=str(root/'tmp/mcp-test-job-dotnet-home'),DOTNET_CLI_TELEMETRY_OPTOUT='1',DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1',DOTNET_GENERATE_ASPNET_CERTIFICATE='false')
for name,command in [('restore',['dotnet','restore',str(project),'--configfile',str(tests/'NuGet.Config')]),('red' if args.previous else 'green',['dotnet','run','--no-restore','--project',str(project),'-p:UseSharedCompilation=false'])]:
 result=subprocess.run(command,cwd=root,env=env,capture_output=True,text=True,encoding='utf-8',errors='replace',timeout=90)
 (fixture/(name+'.log')).write_text(result.stdout+result.stderr,encoding='utf-8');print(result.stdout+result.stderr,end='')
 if result.returncode:print('Evidence fixture:',fixture);raise SystemExit(result.returncode)
print('Evidence fixture:',fixture)
