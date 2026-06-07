using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace VRBattlegrounds.Editor
{
    public static class FbxDependencyIsolator
    {
        [MenuItem("Assets/CodReaper/Isolate FBX Dependencies", false, 30)]
        public static void IsolateDependencies()
        {
            GameObject selectedFbx = Selection.activeObject as GameObject;
            string fbxPath = AssetDatabase.GetAssetPath(selectedFbx);

            if (string.IsNullOrEmpty(fbxPath) || !fbxPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogWarning("[FBX Isolator] Please select an FBX file first.");
                return;
            }

            // Получаем все рекурсивные зависимости (FBX -> Материалы -> Текстуры)
            string[] dependencies = AssetDatabase.GetDependencies(fbxPath, true);
            
            List<string> materialsToMove = new List<string>();
            List<string> texturesToMove = new List<string>();
            StringBuilder report = new StringBuilder();

            foreach (string depPath in dependencies)
            {
                // Игнорируем сам FBX и стандартные ассеты Unity
                if (depPath == fbxPath) continue;
                if (depPath.StartsWith("Packages/") || depPath.StartsWith("Resources/")) continue;
                if (depPath.EndsWith(".cs") || depPath.EndsWith(".dll")) continue;
                if (depPath.EndsWith(".shader")) continue;

                Type assetType = AssetDatabase.GetMainAssetTypeAtPath(depPath);
                if (assetType == typeof(Material))
                {
                    materialsToMove.Add(depPath);
                }
                else if (assetType != null && (assetType == typeof(Texture) || assetType.IsSubclassOf(typeof(Texture))))
                {
                    texturesToMove.Add(depPath);
                }
            }

            if (materialsToMove.Count == 0 && texturesToMove.Count == 0)
            {
                Debug.LogWarning("[FBX Isolator] No external dependencies found. Did you click 'Extract Materials' in FBX import settings?");
                return;
            }

            string baseDir = Path.GetDirectoryName(fbxPath);
            string gatherDir = baseDir + "/" + selectedFbx.name + "_IsolatedInfo";
            string matDir = gatherDir + "/Materials";
            string texDir = gatherDir + "/Textures";

            CreateFolderIfMissing(baseDir, selectedFbx.name + "_IsolatedInfo");
            CreateFolderIfMissing(gatherDir, "Materials");
            CreateFolderIfMissing(gatherDir, "Textures");

            report.AppendLine($"--- Isolated Dependencies for {selectedFbx.name} ---");
            report.AppendLine($"Target Folder: {gatherDir}\n");
            
            int movedMatCount = MoveAssets(materialsToMove, matDir, report, "Materials");
            int movedTexCount = MoveAssets(texturesToMove, texDir, report, "Textures");

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // Автоматический ремаппинг материалов для FBX
            ModelImporter fbxImporter = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
            if (fbxImporter != null)
            {
                fbxImporter.materialLocation = ModelImporterMaterialLocation.External;
                fbxImporter.SearchAndRemapMaterials(ModelImporterMaterialName.BasedOnMaterialName, ModelImporterMaterialSearch.RecursiveUp);
                fbxImporter.SaveAndReimport();
                report.AppendLine("\n[Auto-Remap] Successfully remapped and linked materials to the FBX!");
            }

            Debug.Log($"[FBX Isolator] Moved {movedMatCount} materials and {movedTexCount} textures.\n" +
                      $"All USED assets are now in: {gatherDir}.\n" +
                      $"You can now SAFELY DELETE the old source folders! \n\n" + report.ToString());
        }

        [MenuItem("Assets/CodReaper/Isolate FBX Dependencies", true)]
        public static bool ValidateIsolateDependencies()
        {
            if (Selection.activeObject == null) return false;
            string path = AssetDatabase.GetAssetPath(Selection.activeObject);
            return !string.IsNullOrEmpty(path) && path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase);
        }

        private static void CreateFolderIfMissing(string parentPath, string folderName)
        {
            string targetFolder = parentPath + "/" + folderName;
            if (!AssetDatabase.IsValidFolder(targetFolder))
            {
                AssetDatabase.CreateFolder(parentPath, folderName);
            }
        }

        private static int MoveAssets(List<string> assetPaths, string targetDirectory, StringBuilder report, string sectionName)
        {
            if (assetPaths.Count == 0)
            {
                report.AppendLine($"\n--- {sectionName}: NONE FOUND ---");
                return 0;
            }

            report.AppendLine($"\n--- {sectionName} ({assetPaths.Count}) ---");
            int count = 0;
            foreach (string oldPath in assetPaths)
            {
                string fileName = Path.GetFileName(oldPath);
                string newPath = targetDirectory + "/" + fileName;

                if (oldPath != newPath)
                {
                    string result = AssetDatabase.MoveAsset(oldPath, newPath);
                    if (string.IsNullOrEmpty(result))
                    {
                        report.AppendLine($"[Moved] {fileName}");
                        count++;
                    }
                    else
                    {
                        report.AppendLine($"[Error] Could not move {fileName}: {result}");
                    }
                }
                else
                {
                    report.AppendLine($"[Already mapped] {fileName}");
                    count++;
                }
            }
            return count;
        }
    }
}
