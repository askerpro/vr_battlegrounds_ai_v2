// Резервная прямая проверка существующих assertions при отказе запуска Test Runner.
// Это не нативный прогон NUnit: результат отмечается отдельно в отчёте.
var fixtureType = System.AppDomain.CurrentDomain.GetAssemblies()
    .Select(a => a.GetType("VrBattlegrounds.Tests.Prefabs.AvatarRendererOwnershipTests", false))
    .First(t => t != null);
var results = new System.Collections.Generic.List<object>();
foreach (var name in new[] {
    "EditorOnlySubtreeIsExcludedAndItsRemovalKeepsRenderModeValid",
    "PermanentInactiveGeometryAndObjectNamedGhostAreIncluded",
    "InactiveHandIntegrationIsExcluded",
    "NestedAvatarOwnsItsRenderers" })
{
    var fixture = System.Activator.CreateInstance(fixtureType);
    string error = null;
    try
    {
        fixtureType.GetMethod("CreateInactiveAvatar").Invoke(fixture, null);
        fixtureType.GetMethod(name).Invoke(fixture, null);
    }
    catch (System.Exception exception)
    {
        error = (exception.InnerException ?? exception).ToString();
    }
    finally
    {
        fixtureType.GetMethod("DestroyControl").Invoke(fixture, null);
    }
    results.Add(new { name = name, passed = error == null, error = error });
}
return new { validation = "direct-existing-assertions", results = results };
