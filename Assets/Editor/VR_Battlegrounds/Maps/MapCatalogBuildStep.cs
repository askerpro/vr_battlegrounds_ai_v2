using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace VrBattlegrounds.EditorTools
{
    /// <summary>
    /// Каждая сборка запекает отпечатки карт в <c>MapRuntimeCatalog</c> и прогоняет preflight всего реестра.
    /// Сломанная карта (MapRoot, ссылки, режимы, сетевые префабы) останавливает сборку здесь,
    /// а не превращается в отказ запуска карты на шлеме.
    /// </summary>
    public sealed class MapCatalogBuildStep : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            object result = MapBootstrapMigration.RebakeCatalog(out bool passed);
            if (!passed)
                throw new BuildFailedException("[MapCatalogBuildStep] Preflight карт не пройден: " +
                    Newtonsoft.Json.JsonConvert.SerializeObject(result));
        }
    }
}
