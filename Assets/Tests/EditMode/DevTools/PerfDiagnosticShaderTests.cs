using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace VrBattlegrounds.Tests.DevTools
{
    /// <summary>
    /// Диагностический шейдер render_diag (PerfPlanarFloor) не лежит в Resources — иначе попал бы в Prod, где e2e-харнесс
    /// вырезан (замечание ревью scene-detail, задача scene-detail-followups). В Development-сборку его кладёт
    /// PerfDiagnosticShaderBuildProcessor, рантайм находит его через Shader.Find.
    /// </summary>
    public sealed class PerfDiagnosticShaderTests
    {
        private const string ShaderName = "VR Battlegrounds/Diagnostics/PerfPlanarFloor";
        private const string ShaderPath = "Assets/Scripts/Debug/E2E/Scenarios/PerfDiagnostics/PerfPlanarFloor.shader";

        [Test]
        public void PlanarFloorShader_IsOutsideResources_AndFoundByName()
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            Assert.IsNotNull(shader, "путь совпадает с PerfDiagnosticShaderBuildProcessor.ShaderPath");
            Assert.AreEqual(ShaderName, shader.name, "имя совпадает с PerfRenderDiagnosticControl.PlanarShaderName");
            Assert.AreSame(shader, Shader.Find(ShaderName));
            foreach (string guid in AssetDatabase.FindAssets("PerfPlanarFloor t:Shader"))
                StringAssert.DoesNotContain("/Resources/", AssetDatabase.GUIDToAssetPath(guid));
        }
    }
}
