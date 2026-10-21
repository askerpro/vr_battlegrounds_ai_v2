using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.Editor.Performance
{
    /// <summary>
    /// Кладёт диагностический шейдер E2E (PerfPlanarFloor, render_diag) только в Development-сборки (конфигурация Test).
    /// Шейдер лежит вне Resources, поэтому в Prod не попадает: e2e-харнесс там вырезан (VRBG_NO_E2E).
    /// В копию первой сцены сборки добавляется скрытый объект с выключенным MeshRenderer и материалом на этом шейдере —
    /// ссылка из сцены включает шейдер в сборку, и рантайм находит его через Shader.Find. Исходная сцена не сохраняется.
    /// </summary>
    public sealed class PerfDiagnosticShaderBuildProcessor : IProcessSceneWithReport
    {
        public const string ShaderPath = "Assets/Scripts/Debug/E2E/Scenarios/PerfDiagnostics/PerfPlanarFloor.shader";
        public const string HolderName = "PerfDiagnosticShaders";

        // После GpuSceneIdentityBuildProcessor (100): держатель не входит в таблицу identity и никогда не рисуется.
        public int callbackOrder => 200;

        public void OnProcessScene(Scene scene, BuildReport report)
        {
            if (!BuildPipeline.isBuildingPlayer || report == null || (report.summary.options & BuildOptions.Development) == 0 ||
                scene.buildIndex != 0) return;

            var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            if (shader == null)
            {
                GameLog.Error("[PerfDiagnosticShaderBuildProcessor] нет " + ShaderPath + " — render_diag planar в этой сборке недоступен.");
                return;
            }

            var holder = new GameObject(HolderName) { hideFlags = HideFlags.HideInHierarchy };
            holder.SetActive(false);
            SceneManager.MoveGameObjectToScene(holder, scene);
            var renderer = holder.AddComponent<MeshRenderer>();
            renderer.enabled = false;
            renderer.sharedMaterial = new Material(shader) { name = "PerfPlanarFloor (build)" };
        }
    }
}
