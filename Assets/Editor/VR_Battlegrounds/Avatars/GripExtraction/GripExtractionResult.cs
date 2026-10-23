using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UltimateXR.Manipulation.HandPoses;
using UnityEngine;

namespace VrBattlegrounds.Editor.Avatars
{
    /// <summary>Округление метрик для отчёта (JSON без типов Unity).</summary>
    public static class GripMath
    {
        public static double Round(double value) => Math.Round(value, 4);

        public static double[] Round(Vector3 v) => new[] { Round(v.x), Round(v.y), Round(v.z) };

        public static double[] Round(Quaternion q) => new[] { Round(q.x), Round(q.y), Round(q.z), Round(q.w) };
    }

    /// <summary>Расхождение двух поз align: мм и градусы; <see cref="Path" /> — что сравнивалось.</summary>
    public sealed class GripDelta
    {
        public string Path;
        public double Mm;
        public double Deg;
        public string Error;
    }

    /// <summary>Существующий align на префабе предмета против нового расчёта.</summary>
    public sealed class GripExistingAlign
    {
        public string        Path;
        public double        DeltaMm;
        public double        DeltaDeg;
        public GripPalmCheck Palm;
    }

    /// <summary>Итог Hand Pose Fit после Apply и подсказки следующей итерации (сдвиг по нормали, curl).</summary>
    public sealed class GripFitSummary
    {
        public string Status;
        public string Error;
        public string Overall;
        public string PalmSeverity;
        public string ReportPath;
        public string ReviewSheet;

        /// <summary>Подсказка анализатора по нормали ладони, мм (+ — ладонь ближе к предмету), относительно текущей посадки.</summary>
        public double? SuggestedNormalOffsetMm;

        /// <summary>Текущий сдвиг + подсказка — значение для normalOffsetMmByHand следующего прогона.</summary>
        public double? SuggestedTotalNormalOffsetMm;

        public bool NormalOffsetRecommended;

        /// <summary>Curl по пальцам с Recommended (FitCorrectionHints.ToEdit), градусы — для curlDegByHand.</summary>
        public Dictionary<string, float> SuggestedCurlDeg;
    }

    /// <summary>Одна рука точки: align, поза, метрики.</summary>
    public sealed class GripHandResult
    {
        public string Side;
        public string Anchor;
        public double NormalOffsetMm;
        public bool   NormalOffsetClamped;

        /// <summary>Align относительно корня предмета: позиция в локальных единицах корня, поворот (x, y, z, w) и Эйлер.</summary>
        public double[] AlignLocalPosition;
        public double[] AlignLocalRotation;
        public double[] AlignLocalEuler;

        /// <summary>Центр ладони источника против центра ладони цели (со сдвигом) для нового align.</summary>
        public GripPalmCheck Palm;

        /// <summary>Новый align против опоры по запястью на той же цели (прежняя формула).</summary>
        public GripDelta VsLegacyWrist;

        /// <summary>Align с именем профиля <c>Grabs/&lt;источник&gt;/&lt;поза&gt;/&lt;аватар&gt;/&lt;сторона&gt;</c>, если есть.</summary>
        public GripExistingAlign ExistingNamed;

        /// <summary>Align, на который ссылается текущая запись хвата цели, если есть.</summary>
        public GripExistingAlign ExistingRecord;

        /// <summary>R0: новый align против прямого вызова HandsPackGripAligner.Place (только опора по запястью).</summary>
        public GripDelta VsHandsPackAligner;

        public double? RuntimeVsWorldDeg;
        public double? ShapeDeg;

        /// <summary>Поза против существующего ассета по пути профиля (мировой путь на риге цели), градусы.</summary>
        public double? PoseVsExistingDeg;

        /// <summary>Пястная информация у index, middle, ring, little.</summary>
        public bool[] MetacarpalInfo;

        public Dictionary<string, float> CurlDeg;
        public GripFitSummary            Fit;

        [JsonIgnore] public GripLocalPose     Align;
        [JsonIgnore] public UxrHandDescriptor Descriptor;
    }

    /// <summary>Результат точки хвата: метрики обеих рук, путь позы, записанное.</summary>
    public sealed class GripExtractionResult
    {
        public string Pose;
        public string SourceKind;
        public string SourceSide;
        public string Weapon;
        public string BodyPath;
        public int    GrabPoint;
        public double K;
        public string PosePath;
        public bool   ExistingPose;
        public string Error;

        public List<GripHandResult> Hands   = new List<GripHandResult>();
        public List<string>         Written = new List<string>();

        [JsonIgnore] public GripSourcePoint Point;

        public GripHandResult Hand(string side) => Hands.Find(h => h.Side == side);
    }

    /// <summary>Полный отчёт прогона (пишется в outDir).</summary>
    public sealed class GripExtractionReport
    {
        public int    SchemaVersion = 1;
        public string CreatedUtc;
        public string Status;
        public string Error;
        public string Mode;
        public string Source;
        public string Target;
        public string Anchor;
        public string Encoding;
        public string RigPrefab;
        public string RecordAvatar;
        public string RecordGuid;
        public string PoseRegistry;
        public string OutputDirectory;
        public string ReportPath;
        public string Meaning = "align — поза граббера в системе корня предмета; palm.errorHandMm — [вправо, вверх (тыл), вперёд (пальцы)] в осях кисти цели; normalOffsetMm > 0 — ладонь ближе к предмету";

        public GripExtractionRequest      Request;
        public List<GripExtractionResult> Points   = new List<GripExtractionResult>();
        public List<string>               Warnings = new List<string>();
        public List<string>               Written  = new List<string>();
    }
}
