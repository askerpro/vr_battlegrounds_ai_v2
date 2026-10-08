// Экспорт входов штатного компилятора для проверки indexed source вне Editor.
// До запуска сохранить индекс через prepare-checkpoint.py manifest из локального пакета.
if (UnityEditor.EditorApplication.isPlaying || UnityEditor.EditorApplication.isCompiling)
    throw new System.InvalidOperationException("Index compile requires idle Editor.");
var assembly = System.Linq.Enumerable.Single(UnityEditor.Compilation.CompilationPipeline.GetAssemblies(UnityEditor.Compilation.AssembliesType.Editor), a => a.name == "VrBattlegrounds");
string folder = "tasks/map-runtime-bootstrap/reports/index-game-sources/Assets/Scripts";
var sources = System.IO.Directory.GetFiles(folder, "*.cs", System.IO.SearchOption.AllDirectories);
var paths = new System.Collections.Generic.HashSet<string>(assembly.compiledAssemblyReferences, System.StringComparer.OrdinalIgnoreCase);
foreach (var reference in assembly.assemblyReferences) paths.Add(reference.outputPath);
string reportPath = "tasks/map-runtime-bootstrap/reports/index-compile-input.json";
System.IO.File.WriteAllText(reportPath, Newtonsoft.Json.JsonConvert.SerializeObject(new {
    editorPath = UnityEditor.EditorApplication.applicationPath, sources, references = paths, defines = assembly.defines,
    limits = "Indexed game sources against current compiled external SDK/package assemblies and Editor defines; separate Android workspace compile required. No gameplay acceptance."
}, Newtonsoft.Json.Formatting.Indented));
return new { sourceCount = sources.Length, referenceCount = paths.Count, reportPath };
