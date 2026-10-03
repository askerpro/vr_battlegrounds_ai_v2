using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading.Tasks;
using MCPForUnity.Editor.Tools;
using MCPForUnity.Editor.Services;
using MCPForUnity.Editor.Resources;
using Newtonsoft.Json.Linq;

namespace Probe
{
 [McpForUnityTool("probe_sync", Description="Custom probe", AutoRegister=false, RequiresPolling=true, PollAction="poll", MaxPollSeconds=7, Group="testing")]
 public class CustomSync
 {
  public class Parameters { [ToolParameter("Value", Required=true)] public int? value { get; set; } }
  public static object HandleCommand(JObject p) => Convert.ToInt32(p["value"]) + 1;
 }
 [McpForUnityTool("probe_async")] public class CustomAsync { public static async Task<object> HandleCommand(JObject p) { await Task.Yield(); return 42; } }
 [McpForUnityResource("probe_resource", Description="Resource probe")] public class CustomResource { public static object HandleCommand(JObject p) => "resource-ok"; }
 [McpForUnityTool] public class AutoName { public static object HandleCommand(JObject p) => "auto-ok"; }
 [McpForUnityTool("probe_duplicate")] public class ACollision { public static object HandleCommand(JObject p) => "A"; }
 [McpForUnityTool("probe_duplicate")] public class ZCollision { public static object HandleCommand(JObject p) => "Z"; }
 public class BrokenAttribute : McpForUnityToolAttribute { public BrokenAttribute() { throw new InvalidOperationException("broken attribute"); } }
 [Broken] public class AABroken { public static object HandleCommand(JObject p) => "bad"; }
}
internal static class Program
{
 private static int _passed, _failed;
 private static void Test(string name, Action body)
 {
  try { body(); _passed++; Console.WriteLine("PASS " + name); }
  catch(Exception ex) { _failed++; Console.WriteLine("FAIL " + name + ": " + ex.Message); }
 }
 private static void Require(bool value, string message) { if(!value) throw new Exception(message); }
 private static void ResetRegistry(bool worker=false)
 {
  var flags=BindingFlags.Static|BindingFlags.NonPublic;
  typeof(CommandRegistry).GetField("_initialized",flags).SetValue(null,false);
  typeof(CommandRegistry).GetField("_cachedIsAssetImportWorker",flags).SetValue(null,null);
  ((System.Collections.IDictionary)typeof(CommandRegistry).GetField("_handlers",flags).GetValue(null)).Clear();
  MCPForUnity.Editor.Helpers.McpLog.Errors.Clear(); UnityEditor.AssetDatabase.Worker=worker;
 }
 private static int Main()
 {
  var pluginA=SameNameType("APlugin", "A"); var pluginZ=SameNameType("ZPlugin", "Z");
  UnityEditor.TypeCache.Tools = new[] { pluginZ,pluginA,typeof(Probe.ZCollision),typeof(Probe.CustomSync),typeof(Probe.AABroken),typeof(Probe.ACollision),typeof(Probe.CustomAsync),typeof(Probe.AutoName) };
  UnityEditor.TypeCache.Resources = new[] { typeof(Probe.CustomResource) };
  Test("Bad attribute does not abort healthy handlers",()=>{ ResetRegistry(); CommandRegistry.Initialize(); Require(MCPForUnity.Editor.Helpers.McpLog.Errors.Count==0,"Discovery aborted"); Require((int)CommandRegistry.GetHandler("probe_sync")(new JObject{{"value",4}})==5,"Healthy handler missing"); });
  Test("Custom sync and async handlers preserve payload/result",()=>{ Require((int)CommandRegistry.InvokeCommandAsync("probe_async",new JObject()).GetAwaiter().GetResult()==42,"Async result changed"); Require((string)CommandRegistry.GetHandler("probe_resource")(new JObject())=="resource-ok","Resource handler lost"); });
  Test("Tool parameter and polling metadata survive",()=>{ var metadata=new ToolDiscoveryService().GetToolMetadata("probe_sync"); Require(metadata!=null,"Custom metadata missing"); Require(metadata.Description=="Custom probe"&&!metadata.AutoRegister&&metadata.RequiresPolling&&metadata.PollAction=="poll"&&metadata.MaxPollSeconds==7&&metadata.Group=="testing","Tool metadata changed"); var p=metadata.Parameters.Single(); Require(p.Name=="value"&&p.Type=="integer"&&p.Required,"Parameter schema changed"); });
  Test("Duplicate metadata and handler choose the same winner",()=>{ var metadata=new ToolDiscoveryService().GetToolMetadata("probe_duplicate"); Require(metadata.ClassName=="ZCollision"&&(string)CommandRegistry.GetHandler("probe_duplicate")(new JObject())=="Z","Collision winner differs"); });
  Test("Identical full names use assembly identity as tie-break",()=>{ var metadata=new ToolDiscoveryService().GetToolMetadata("probe_same_fullname"); Require(metadata.AssemblyName=="ZPlugin"&&(string)CommandRegistry.GetHandler("probe_same_fullname")(new JObject())=="Z","Assembly tie-break differs"); });
  Test("Automatic name and resource description survive",()=>{ Require(new ToolDiscoveryService().GetToolMetadata("auto_name")!=null&&(string)CommandRegistry.GetHandler("auto_name")(new JObject())=="auto-ok","Automatic name changed"); Require(new ResourceDiscoveryService().GetResourceMetadata("probe_resource").Description=="Resource probe","Resource metadata changed"); });
  Test("Explicit rescan discovers updated TypeCache",()=>{ var service=new ToolDiscoveryService(); Require(service.GetToolMetadata("probe_async")!=null,"Precondition failed"); UnityEditor.TypeCache.Tools=new[]{typeof(Probe.CustomSync)}; service.InvalidateCache(); Require(service.GetToolMetadata("probe_async")==null,"Rescan ignored cache changes"); });
  Test("Asset import worker never registers handlers",()=>{ ResetRegistry(true); CommandRegistry.Initialize(); bool absent=false; try {CommandRegistry.GetHandler("probe_sync");} catch(InvalidOperationException){absent=true;} Require(absent,"Worker registered tools"); });
  Console.WriteLine($"{_passed}/{_passed+_failed} passed (actual package sources, platform APIs stubbed)"); return _failed==0?0:1;
 }
 private static Type SameNameType(string assemblyName, string result)
 {
  var assembly=AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(assemblyName),AssemblyBuilderAccess.Run);
  var type=assembly.DefineDynamicModule("ProbeModule").DefineType("Probe.SameName",TypeAttributes.Public);
  type.SetCustomAttribute(new CustomAttributeBuilder(typeof(McpForUnityToolAttribute).GetConstructor(new[]{typeof(string)}),new object[]{"probe_same_fullname"}));
  var method=type.DefineMethod("HandleCommand",MethodAttributes.Public|MethodAttributes.Static,typeof(object),new[]{typeof(JObject)});
  var il=method.GetILGenerator(); il.Emit(OpCodes.Ldstr,result); il.Emit(OpCodes.Ret);
  return type.CreateType();
 }
}
