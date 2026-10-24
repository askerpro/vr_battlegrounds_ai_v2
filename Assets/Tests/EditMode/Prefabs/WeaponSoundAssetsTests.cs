using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Interaction;
using VrBattlegrounds.Weapons;

namespace VrBattlegrounds.Tests.Prefabs
{
    /// <summary>
    /// Звуковые данные оружия, принятые в шлеме 2026-10-24: дефолты категорий стволов и ролей якорей заполнены, нарезки
    /// звуков начинаются без тишины.
    ///
    /// <para>
    /// <b>Дефолты.</b> Решение пользователя 2026-10-23: дефолты звука — данные в ассетах, не константы в коде. Пустое поле
    /// дефолта молча делает действие беззвучным у всех стволов категории (или у всех якорей роли), поэтому каждое поле,
    /// на которое опирается цепочка выбора клипа (<see cref="AnchorSound" />, <c>WeaponEjecta</c>, <c>ImpactSound</c>),
    /// заполнено.
    /// </para>
    ///
    /// <para>
    /// <b>Тишина в начале.</b> Тишина до основного удара в VR слышна как запаздывание (шлем 2026-10-24: зарядка Herrington
    /// 580 мс, выстрелы PPK и Browning ~100 мс). Нарезчик <c>KinemationAudio.Cut</c> начинает кусок за 10 мс до первого
    /// окна 10 мс громче 30 % пика и не срезает запас меньше 15 мс, поэтому у нарезки такое окно начинается не позже
    /// <see cref="MaxOnsetSeconds" />. Тест читает WAV с диска сам (RIFF, PCM 16 бит), не через импорт Unity: сжатие
    /// импорта не должно влиять на проверку.
    /// </para>
    /// </summary>
    public class WeaponSoundAssetsTests
    {
        private static readonly string[] CategoryDefaults =
        {
            "Assets/Data/WeaponSystem/RifleFeedbackDefaults.asset",
            "Assets/Data/WeaponSystem/ShotgunFeedbackDefaults.asset",
            "Assets/Data/WeaponSystem/PistolFeedbackDefaults.asset",
            "Assets/Data/WeaponSystem/SniperFeedbackDefaults.asset",
        };

        /// <summary>Роли якорей со своими дефолтами вставки и доставания (гнездо магазина берёт дефолт категории ствола).</summary>
        private static readonly AnchorRoleKind[] RolesWithDefaults =
        {
            AnchorRoleKind.World, AnchorRoleKind.Primary, AnchorRoleKind.Secondary, AnchorRoleKind.Magazine
        };

        private const float WindowSeconds   = 0.01f;
        private const float OnsetShare      = 0.3f;
        private const float MaxOnsetSeconds = 0.02f;

        [Test]
        public void Дефолты_звуков_категорий_заполнены()
        {
            var failures = new List<string>();
            foreach (string path in CategoryDefaults)
            {
                var defaults = AssetDatabase.LoadAssetAtPath<WeaponFeedbackDefaults>(path);
                if (defaults == null) { failures.Add($"{path}: ассет не найден"); continue; }

                if (defaults.MagazineInsert == null) failures.Add($"{path}: пуст MagazineInsert (вставка магазина)");
                if (defaults.MagazineTakeOut == null) failures.Add($"{path}: пуст MagazineTakeOut (снятие магазина)");
                CheckSet(failures, $"{path}: EjectaImpacts (касание патронов и гильз)", defaults.EjectaImpacts);
                CheckSet(failures, $"{path}: DropImpacts (падение ствола)", defaults.DropImpacts);
            }

            Assert.That(failures, Is.Empty, "Дефолты звука категорий стволов не заполнены:\n" + string.Join("\n", failures));
        }

        [Test]
        public void Дефолты_ударов_и_ролей_якорей_заполнены()
        {
            var failures = new List<string>();

            ImpactSoundDefaults impacts = ImpactSoundDefaults.Instance;
            if (impacts == null)
            {
                failures.Add("Нет Resources/ImpactSoundDefaults.asset");
            }
            else
            {
                foreach (ImpactSoundKind kind in new[] { ImpactSoundKind.Weapon, ImpactSoundKind.Magazine })
                    CheckSet(failures, $"ImpactSoundDefaults.{kind}", impacts.For(kind));
            }

            MagazineAnchorSoundDefaults anchors = MagazineAnchorSoundDefaults.Instance;
            if (anchors == null)
            {
                failures.Add("Нет Resources/MagazineAnchorSoundDefaults.asset");
            }
            else
            {
                foreach (AnchorRoleKind role in RolesWithDefaults)
                {
                    if (anchors.RolePlaceClip(role) == null) failures.Add($"MagazineAnchorSoundDefaults: у роли {role} пуст звук вставки (place)");
                    if (anchors.RoleTakeOutClip(role) == null) failures.Add($"MagazineAnchorSoundDefaults: у роли {role} пуст звук доставания (takeOut)");
                }
            }

            Assert.That(failures, Is.Empty, "Дефолты звуков ударов и якорей не заполнены:\n" + string.Join("\n", failures));
        }

        [Test]
        public void Нарезки_звуков_без_тишины_в_начале()
        {
            List<string> files = CutFiles();
            Assert.That(files, Is.Not.Empty, "Не найдено ни одной нарезки звука.");

            var failures = new List<string>();
            var skipped  = new List<string>();
            int checkedCount = 0;
            foreach (string path in files)
            {
                if (!TryReadPcm16Mono(path, out float[] mono, out int rate, out string reason))
                {
                    skipped.Add($"{path}: {reason}");
                    continue;
                }

                checkedCount++;
                float onset = OnsetSeconds(mono, rate);
                if (onset < 0f) failures.Add($"{path}: тишина целиком");
                else if (onset > MaxOnsetSeconds + 1e-6f)
                    failures.Add($"{path}: основной звук (окно 10 мс ≥ {OnsetShare * 100f:0} % пика) начинается на {onset * 1000f:0} мс, " +
                                 $"допустимо до {MaxOnsetSeconds * 1000f:0} мс");
            }

            if (skipped.Count > 0)
                TestContext.Out.WriteLine("Не PCM 16 бит, не проверены:\n" + string.Join("\n", skipped));

            Assert.That(checkedCount, Is.GreaterThan(0), "Ни одна нарезка не прочитана как PCM 16 бит:\n" + string.Join("\n", skipped));
            Assert.That(failures, Is.Empty,
                        $"Нарезки с тишиной до основного удара ({failures.Count} из {checkedCount}) — перерезать KinemationAudio.Cut:\n" +
                        string.Join("\n", failures));
        }

        private static void CheckSet(List<string> failures, string label, AudioClip[] clips)
        {
            if (clips == null || clips.Length == 0) failures.Add($"{label}: набор пуст");
            else if (clips.Any(c => c == null)) failures.Add($"{label}: в наборе пустой элемент");
        }

        /// <summary>Нарезки нарезчика: звуки паков HandsPack и KINEMATION, моно-нарезки Handling и Impacts, гильзы.</summary>
        private static List<string> CutFiles()
        {
            var files = new List<string>();
            void Add(string folder, string pattern, SearchOption option)
            {
                if (!Directory.Exists(folder)) return;
                files.AddRange(Directory.GetFiles(folder, pattern, option).Select(p => p.Replace('\\', '/')));
            }

            Add("Assets/Audio/SFX/Weapons/HandsPack", "*.wav", SearchOption.AllDirectories);
            Add("Assets/Audio/SFX/Weapons/Kinemation", "*.wav", SearchOption.AllDirectories);
            Add("Assets/Audio/SFX/Handling", "*_mono.wav", SearchOption.TopDirectoryOnly);
            Add("Assets/Audio/SFX/Impacts", "*_mono.wav", SearchOption.TopDirectoryOnly);
            Add("Assets/Audio/SFX/Casings", "*.wav", SearchOption.TopDirectoryOnly);
            return files.Distinct().OrderBy(p => p).ToList();
        }

        /// <summary>
        /// Начало первого окна <see cref="WindowSeconds" />, где модуль сэмпла достигает <see cref="OnsetShare" /> пика файла
        /// (окна от начала файла, как в нарезчике); −1 — файл тихий целиком.
        /// </summary>
        private static float OnsetSeconds(float[] mono, int rate)
        {
            float peak = 0f;
            foreach (float s in mono) peak = Mathf.Max(peak, Mathf.Abs(s));
            if (peak <= 0f) return -1f;

            int window = Mathf.Max(1, Mathf.RoundToInt(WindowSeconds * rate));
            for (int w = 0; w < mono.Length; w += window)
            {
                int end = Mathf.Min(mono.Length, w + window);
                for (int i = w; i < end; i++)
                {
                    if (Mathf.Abs(mono[i]) >= peak * OnsetShare) return w / (float)rate;
                }
            }
            return -1f;
        }

        /// <summary>Читает WAV PCM 16 бит (в том числе WAVE_FORMAT_EXTENSIBLE) и сводит каналы в моно средним.</summary>
        private static bool TryReadPcm16Mono(string path, out float[] mono, out int rate, out string reason)
        {
            mono = null;
            rate = 0;
            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.Length < 12 || Tag(bytes, 0) != "RIFF" || Tag(bytes, 8) != "WAVE")
            {
                reason = "не RIFF/WAVE (LFS-указатель?)";
                return false;
            }

            int format = -1, channels = 0, bits = 0, dataOffset = -1, dataSize = 0;
            int p = 12;
            while (p + 8 <= bytes.Length)
            {
                string id = Tag(bytes, p);
                int size = System.BitConverter.ToInt32(bytes, p + 4);
                int body = p + 8;
                if (id == "fmt " && size >= 16)
                {
                    format   = System.BitConverter.ToUInt16(bytes, body);
                    channels = System.BitConverter.ToUInt16(bytes, body + 2);
                    rate     = System.BitConverter.ToInt32(bytes, body + 4);
                    bits     = System.BitConverter.ToUInt16(bytes, body + 14);
                    // WAVE_FORMAT_EXTENSIBLE: настоящий формат — первые 2 байта SubFormat.
                    if (format == 0xFFFE && size >= 40) format = System.BitConverter.ToUInt16(bytes, body + 24);
                }
                else if (id == "data")
                {
                    dataOffset = body;
                    dataSize   = Mathf.Min(size, bytes.Length - body);
                }
                p = body + size + (size & 1);
            }

            if (format != 1 || bits != 16)
            {
                reason = $"формат {format}, {bits} бит";
                return false;
            }
            if (dataOffset < 0 || channels <= 0 || rate <= 0)
            {
                reason = "нет блока fmt или data";
                return false;
            }

            int frames = dataSize / (2 * channels);
            mono = new float[frames];
            for (int f = 0; f < frames; f++)
            {
                float sum = 0f;
                for (int c = 0; c < channels; c++)
                    sum += System.BitConverter.ToInt16(bytes, dataOffset + (f * channels + c) * 2) / 32768f;
                mono[f] = sum / channels;
            }
            reason = null;
            return true;
        }

        private static string Tag(byte[] bytes, int offset) => System.Text.Encoding.ASCII.GetString(bytes, offset, 4);
    }
}
