using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VrBattlegrounds.Editor.Gameplay.SightSplit;
using VrBattlegrounds.Editor.Weapons.Sights;
using VrBattlegrounds.Weapons.Sights;

namespace VrBattlegrounds.Tests.Sights
{
    /// <summary>
    /// Инструмент «Родной прицел» окна Sight Auto-Zero (решение 2026-10-12 «Только инструменты»): стенд на временной
    /// копии ствола из SightSplitTable. Проверяется инструмент, не механика сведения в игре (её пользователь ещё не
    /// принял): одна подвижная деталь (у PPK — ни одной), решатель получает корректную геометрию, сцена стенда не
    /// сохраняется, префаб и меши-источники на диске не меняются.
    /// </summary>
    public sealed class NativeSightStandTests
    {
        private const double Z = 15;

        private static IEnumerable<string> Weapons => SightSplitTable.All.Select(s => s.WeaponId);

        [TestCaseSource(nameof(Weapons))]
        public void Stand_OneMovingPart_SolverGeometry_NoDiskWrites(string weaponId)
        {
            SightSplitSpec spec = SightSplitTable.Get(weaponId);
            var files = new[] { spec.PrefabPath }.Concat(spec.Parts.Select(p => p.SourceMeshPath)).Distinct()
                                                .SelectMany(p => new[] { p, p + ".meta" }).ToArray();
            var before = files.ToDictionary(f => f, Hash);

            var scene = EditorSceneManager.NewPreviewScene();
            NativeSightStand stand = null;
            try
            {
                stand = NativeSightStand.Build(spec, scene);
                Assert.AreEqual(2, stand.Weapon.GetComponentsInChildren<SightAdjustablePart>(true).Length);
                Assert.AreEqual(spec.MovingPart == null ? 0 : 1,
                                stand.Weapon.GetComponentsInChildren<SightAdjustablePart>(true).Count(p => p.IsAdjustable), "подвижных деталей");
                Assert.AreEqual(spec.MovingPart == null, stand.Moving == null);
                Assert.IsTrue(string.IsNullOrEmpty(scene.path), "сцена стенда временная, без пути");

                Transform muzzle = SightParts.FindShotSource(stand.Weapon);
                var result = SightAutoZeroSolver.Solve(SightParts.Input(stand.Rear, stand.Front, stand.Moving, muzzle, Z));
                TestContext.WriteLine($"{weaponId}: ok={result.Ok} сдвиг {SightAutoZeroSolver.Mm(result.RequiredWorldShift)} мм, " +
                                      $"ход −{result.TravelDown * 1e3:0.0}…+{result.TravelUp * 1e3:0.0} мм, {result.Error}");
                if (stand.Moving == null)
                    StringAssert.Contains("Подвижной детали нет", result.Error, "PPK: только измерение");
                else
                    Assert.IsTrue(result.Ok || result.Error.Contains("хода"),
                                  $"{weaponId}: геометрия прицела отвергнута решателем: {result.Error}");
            }
            finally
            {
                stand?.Dispose();
                EditorSceneManager.ClosePreviewScene(scene);
            }

            foreach (string file in files)
                Assert.AreEqual(before[file], Hash(file), $"{file} изменён на диске");
        }

        private static string Hash(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
                return System.BitConverter.ToString(sha.ComputeHash(stream));
        }
    }
}
