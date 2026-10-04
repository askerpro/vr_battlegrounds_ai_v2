using System;
using UnityEngine;

namespace VrBattlegrounds.LevelDesign
{
    /// <summary>Единственный паспорт физической арены; начало не зависит от препятствий.</summary>
    [DisallowMultipleComponent]
    public sealed class PhysicalArenaDefinition : MonoBehaviour
    {
        public string arenaId = "";
        public Vector2 gridOrigin;
        public float gridStep = .3f;
        public float safetyMargin = .1f;
        public Collider floor;
        private void Reset() => arenaId = Guid.NewGuid().ToString("N");
        public Vector2 WorldOrigin { get { var p=transform.TransformPoint(new Vector3(gridOrigin.x,0,gridOrigin.y)); return new Vector2(p.x,p.z); } }
        // floor оставлен для чтения старых сцен; новый чертёж использует только данные floorShape.
        public PhysicalArenaShape floorShape;
        public Bounds FloorBounds => TryFloorBounds(out var bounds) ? bounds : default;
        public bool TryFloorBounds(out Bounds bounds)
        {
            if (floorShape != null) return floorShape.TryBounds(out bounds);
            return PhysicalArenaGeometry.TryBounds(floor, out bounds);
        }
        public bool Valid(out string reason)
        {
            reason="";
            if(string.IsNullOrWhiteSpace(arenaId)) reason="Не задан ID арены.";
            else if(!Finite(gridOrigin.x)||!Finite(gridOrigin.y)||!Finite(gridStep)||Mathf.Abs(gridStep-.3f)>.00001f) reason="MVP поддерживает сетку 0.3 м с конечным origin.";
            else if(!Finite(safetyMargin)||safetyMargin<0) reason="Запас должен быть конечным и неотрицательным.";
            else if(Quaternion.Angle(transform.rotation,Quaternion.identity)>.001f||(transform.lossyScale-Vector3.one).sqrMagnitude>.000001f) reason="Корень арены должен иметь мировой поворот 0 и масштаб 1.";
            else if(floorShape!=null)
            {
                if(!floorShape.transform.IsChildOf(transform)||Quaternion.Angle(floorShape.transform.rotation,Quaternion.identity)>.001f||!floorShape.TryBounds(out _))
                    reason="Нужна конечная прямоугольная форма пола внутри разметки без поворота.";
            }
            else if(floor==null||floor.isTrigger||!floor.transform.IsChildOf(transform)||!(floor is BoxCollider)
                ||Quaternion.Angle(floor.transform.rotation,Quaternion.identity)>.001f||!PhysicalArenaGeometry.TryBounds(floor,out _))
                reason="Нужны данные формы пола; Collider поддерживается только для старых сцен.";
            return reason.Length==0;
        }
        public static bool Finite(float value) => !float.IsNaN(value)&&!float.IsInfinity(value);
    }
}
