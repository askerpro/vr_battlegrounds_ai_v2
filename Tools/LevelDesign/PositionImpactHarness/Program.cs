using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using VrBattlegrounds.Tests.Maps;

// Независимый прогон чистой математики; не замена Unity EditMode/Android-проверки.
int failures = 0, count = 0;
foreach (var fixture in new object[] { new PositionImpactTests(), new MapEvaluationTests() })
foreach (var method in fixture.GetType().GetMethods().Where(m => m.IsDefined(typeof(TestAttribute), false)))
{
    count++;
    try { method.Invoke(fixture, null); Console.WriteLine("PASS " + method.Name); }
    catch (TargetInvocationException e) { failures++; Console.WriteLine("FAIL " + method.Name + ": " + e.InnerException); }
}
Console.WriteLine($"Tests={count}; Failed={failures}");
return count == 0 || failures != 0 ? 1 : 0;
