using System;
using System.Collections.Generic;

namespace VrBattlegrounds.Editor.HandPoseReview
{
    /// <summary>Сохранённая материальная область кисти; состав не зависит от оружия и текущей позы.</summary>
    [Serializable]
    public sealed class HandPosePotentialContactMask
    {
        public const int CurrentVersion = 1;
        public int Version = CurrentVersion;
        public HandPosePotentialContactMaskMetadata Metadata = new HandPosePotentialContactMaskMetadata();
        public List<HandPosePotentialContactMaskRegion> Regions = new List<HandPosePotentialContactMaskRegion>();
        public List<HandPosePotentialContactMaskTriangle> Triangles = new List<HandPosePotentialContactMaskTriangle>();

        public static bool IsRegion(string region) => region == "palm" || region == "thumb" ||
            region == "index" || region == "middle" || region == "ring" || region == "little";
    }

    [Serializable]
    public sealed class HandPosePotentialContactMaskMetadata
    {
        // Вызывающий код объединяет GUID/local id меша, dependency hash, LOD и сторону кисти.
        public string BindingFingerprint;
        public bool Reviewed;
        public string Provenance;
    }

    [Serializable]
    public sealed class HandPosePotentialContactMaskRegion
    {
        public string Region;
    }

    [Serializable]
    public sealed class HandPosePotentialContactMaskTriangle
    {
        public string Source;
        public int SourceSubMesh;
        public int SourceTriangle;
        public string Region;
        // Равномерная площадная плотность по всей грани. Геометрию произвольной подчасти
        // scalar-вес не задаёт: для точного контура нужны выбранные целые мелкие грани.
        public double Weight = 1;
        // Площадь всей грани в эталонной позе до применения Weight, мм².
        public double? ReferenceAreaMm2;
    }

    /// <summary>Точный ключ исходной грани; не зависит от порядка baked-треугольников.</summary>
    public readonly struct HandPosePotentialContactTriangleKey : IEquatable<HandPosePotentialContactTriangleKey>
    {
        public readonly string Source;
        public readonly int SourceSubMesh;
        public readonly int SourceTriangle;

        public HandPosePotentialContactTriangleKey(string source, int subMesh, int triangle)
        { Source = source; SourceSubMesh = subMesh; SourceTriangle = triangle; }

        public bool Equals(HandPosePotentialContactTriangleKey other) =>
            StringComparer.Ordinal.Equals(Source, other.Source) && SourceSubMesh == other.SourceSubMesh &&
            SourceTriangle == other.SourceTriangle;
        public override bool Equals(object obj) => obj is HandPosePotentialContactTriangleKey other && Equals(other);
        public override int GetHashCode()
        {
            unchecked { return ((Source == null ? 0 : StringComparer.Ordinal.GetHashCode(Source)) * 397 ^ SourceSubMesh) * 397 ^ SourceTriangle; }
        }
        public override string ToString() => Source + " [" + SourceSubMesh + ":" + SourceTriangle + "]";
    }
}
