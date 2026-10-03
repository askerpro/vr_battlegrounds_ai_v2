using System;
using System.Collections.Generic;
using System.Reflection;
namespace UnityEditor
{
 public static class TypeCache
 {
  public static Type[] Tools, Resources;
  public static Type[] GetTypesWithAttribute<T>() where T : Attribute => typeof(T) == typeof(MCPForUnity.Editor.Tools.McpForUnityToolAttribute) ? Tools : Resources;
 }
 public static class AssetDatabase { public static bool Worker; public static bool IsAssetImportWorkerProcess() => Worker; }
 public static class EditorPrefs
 {
  private static readonly Dictionary<string, bool> Values = new Dictionary<string, bool>();
  public static bool HasKey(string key) => Values.ContainsKey(key);
  public static bool GetBool(string key, bool fallback) => Values.TryGetValue(key, out var value) ? value : fallback;
  public static void SetBool(string key, bool value) => Values[key] = value;
 }
}
namespace MCPForUnity.Runtime.Helpers
{
 public static class UnityAssembliesCompat { public static Assembly[] GetLoadedAssemblies() => AppDomain.CurrentDomain.GetAssemblies(); }
}
namespace MCPForUnity.Editor.Helpers
{
 public static class McpLog
 {
  public static readonly List<string> Errors = new List<string>();
  public static void Info(string message, bool flag = false) { }
  public static void Warn(string message) { }
  public static void Error(string message) => Errors.Add(message);
 }
}
namespace MCPForUnity.Editor.Constants
{
 public static class EditorPrefKeys { public const string ToolEnabledPrefix = "tool:"; public const string ResourceEnabledPrefix = "resource:"; }
}
namespace Newtonsoft.Json.Linq { public class JObject : Dictionary<string, object> { } }
namespace Newtonsoft.Json { public static class JsonConvert { public static string SerializeObject(object value) => System.Text.Json.JsonSerializer.Serialize(value); } }
