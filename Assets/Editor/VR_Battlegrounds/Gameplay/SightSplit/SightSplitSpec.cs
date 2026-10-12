using System;
using System.Collections.Generic;
using UnityEngine;
using VrBattlegrounds.Weapons.Sights;

namespace VrBattlegrounds.Editor.Gameplay.SightSplit
{
    /// <summary>
    /// Селектор острова меша: остров (связная по сваренным позициям группа треугольников) с ровно этим числом
    /// различных вершин Unity и треугольников, целиком лежащий в окне (AABB острова ± 0,5 мм в метрах префаба,
    /// переведённые в единицы меша). Номер острова из разбора не используется — порядок островов не контракт.
    /// </summary>
    public sealed class SightIslandSelector
    {
        public readonly string Role;
        public readonly int Vertices;
        public readonly int Triangles;
        public readonly Vector3 WindowMin;
        public readonly Vector3 WindowMax;

        public SightIslandSelector(string role, int vertices, int triangles, Vector3 windowMin, Vector3 windowMax)
        {
            Role = role; Vertices = vertices; Triangles = triangles; WindowMin = windowMin; WindowMax = windowMax;
        }

        public bool Matches(MeshIslands.Island island) => island.Vertices == Vertices && island.Triangles == Triangles
            && island.Min.x >= WindowMin.x && island.Min.y >= WindowMin.y && island.Min.z >= WindowMin.z
            && island.Max.x <= WindowMax.x && island.Max.y <= WindowMax.y && island.Max.z <= WindowMax.z;

        public override string ToString() => $"«{Role}» nv{Vertices}/nt{Triangles}";
    }

    /// <summary>
    /// Половина прицела в узле префаба. Подвижная выносится из меша узла в свой меш (<see cref="Extract"/>); у
    /// неподвижной создаётся только опорная точка с <see cref="SightAdjustablePart"/> нулевого хода, её селекторы —
    /// контроль, что исходная геометрия не изменилась.
    /// </summary>
    public sealed class SightSplitPartSpec
    {
        public readonly SightPartRole Role;
        /// <summary>Путь узла с мешем относительно корня префаба (без имени корня).</summary>
        public readonly string NodePath;
        /// <summary>Ожидаемый ассет исходного меша узла (контроль дрейфа источника).</summary>
        public readonly string SourceMeshPath;
        /// <summary>localFileID меша в ассете (у FBX имя меша не совпадает с именем узла).</summary>
        public readonly long SourceMeshLocalId;
        public readonly int SourceVertices;
        public readonly int SourceTriangles;
        public readonly bool Movable;
        /// <summary>Точка прицеливания R/F в единицах меша узла.</summary>
        public readonly Vector3 PointMesh;
        /// <summary>Та же точка в метрах префаба из разбора (корень с масштабом, без позиции); null — разбор её не дал.</summary>
        public readonly Vector3? PointPrefabM;
        /// <summary>Ход вниз/вверх, метры мира (рамка детали — единичного масштаба).</summary>
        public readonly float TravelDown;
        public readonly float TravelUp;
        /// <summary>Оценка нужного сдвига из разбора, мм; null — нет оценки.</summary>
        public readonly float? RequiredShiftMm;
        public readonly IReadOnlyList<SightIslandSelector> Selectors;

        public SightSplitPartSpec(SightPartRole role, string nodePath, string sourceMeshPath, long sourceMeshLocalId,
                                  int sourceVertices, int sourceTriangles, bool movable, Vector3 pointMesh, Vector3? pointPrefabM,
                                  float travelDown, float travelUp, float? requiredShiftMm, params SightIslandSelector[] selectors)
        {
            if (movable && (selectors == null || selectors.Length == 0))
                throw new ArgumentException($"{role}: у подвижной детали нет селекторов островов");
            if (!movable && (travelDown != 0f || travelUp != 0f))
                throw new ArgumentException($"{role}: у неподвижной половины ход должен быть 0");
            Role = role; NodePath = nodePath; SourceMeshPath = sourceMeshPath; SourceMeshLocalId = sourceMeshLocalId;
            SourceVertices = sourceVertices; SourceTriangles = sourceTriangles; Movable = movable; PointMesh = pointMesh;
            PointPrefabM = pointPrefabM; TravelDown = travelDown; TravelUp = travelUp; RequiredShiftMm = requiredShiftMm;
            Selectors = selectors ?? new SightIslandSelector[0];
        }

        /// <summary>Выносится ли геометрия в отдельный меш: только у подвижной детали.</summary>
        public bool Extract => Movable;

        public string FrameName => $"Sight{Role}Frame";
        public string PartName => $"Sight{Role}";
        public string MeshObjectName => $"Sight{Role}Mesh";
    }

    /// <summary>
    /// Подъём сложенной рамки (Scar): локальная поза узла до и после подъёма плюс ось поворота в пространстве префаба
    /// (для перекрёстной проверки: поднятая поза = поворот сложенной вокруг оси шарнира).
    /// </summary>
    public sealed class SightNodeRaise
    {
        public readonly string NodePath;
        public readonly Vector3 FoldedLocalPosition;
        public readonly Quaternion FoldedLocalRotation;
        public readonly Vector3 RaisedLocalPosition;
        public readonly Quaternion RaisedLocalRotation;
        public readonly Vector3 AxisPointPrefabM;
        public readonly Vector3 AxisDirPrefab;
        public readonly float AngleDeg;

        public SightNodeRaise(string nodePath, Vector3 foldedLocalPosition, Quaternion foldedLocalRotation,
                              Vector3 raisedLocalPosition, Quaternion raisedLocalRotation,
                              Vector3 axisPointPrefabM, Vector3 axisDirPrefab, float angleDeg)
        {
            NodePath = nodePath; FoldedLocalPosition = foldedLocalPosition; FoldedLocalRotation = foldedLocalRotation;
            RaisedLocalPosition = raisedLocalPosition; RaisedLocalRotation = raisedLocalRotation;
            AxisPointPrefabM = axisPointPrefabM; AxisDirPrefab = axisDirPrefab; AngleDeg = angleDeg;
        }
    }

    /// <summary>Спецификация разделения прицела одного ствола: две половины (Rear, Front) и подъёмы рамок.</summary>
    public sealed class SightSplitSpec
    {
        public readonly string WeaponId;
        public readonly string PrefabPath;
        public readonly string Note;
        public readonly IReadOnlyList<SightNodeRaise> Raises;
        public readonly SightSplitPartSpec Rear;
        public readonly SightSplitPartSpec Front;

        public SightSplitSpec(string weaponId, string prefabPath, string note, SightNodeRaise[] raises,
                              SightSplitPartSpec rear, SightSplitPartSpec front)
        {
            if (rear == null || rear.Role != SightPartRole.Rear || front == null || front.Role != SightPartRole.Front)
                throw new ArgumentException($"{weaponId}: нужны половины Rear и Front");
            if (rear.Movable && front.Movable)
                throw new ArgumentException($"{weaponId}: подвижной может быть только одна половина");
            WeaponId = weaponId; PrefabPath = prefabPath; Note = note;
            Raises = raises ?? new SightNodeRaise[0]; Rear = rear; Front = front;
        }

        public IEnumerable<SightSplitPartSpec> Parts { get { yield return Rear; yield return Front; } }

        public SightSplitPartSpec MovingPart => Rear.Movable ? Rear : Front.Movable ? Front : null;
    }
}
