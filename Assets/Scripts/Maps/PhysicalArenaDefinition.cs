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
        public bool Valid(out string reason)
        {
            reason="";
            if(string.IsNullOrWhiteSpace(arenaId)) reason="Не задан ID арены.";
            else if(!Finite(gridOrigin.x)||!Finite(gridOrigin.y)||!Finite(gridStep)||Mathf.Abs(gridStep-.3f)>.00001f) reason="MVP поддерживает сетку 0.3 м с конечным origin.";
            else if(!Finite(safetyMargin)||safetyMargin<0) reason="Запас должен быть конечным и неотрицательным.";
            else if(Quaternion.Angle(transform.rotation,Quaternion.identity)>.001f||(transform.lossyScale-Vector3.one).sqrMagnitude>.000001f) reason="Корень арены должен иметь мировой поворот 0 и масштаб 1.";
            else if(floor==null||!floor.enabled||!floor.gameObject.activeInHierarchy||floor.isTrigger||!floor.transform.IsChildOf(transform)) reason="Нужен активный не-trigger пол внутри арены.";
            else if(!(floor is BoxCollider)||Quaternion.Angle(floor.transform.rotation,Quaternion.identity)>.001f||floor.bounds.size.x<=0||floor.bounds.size.z<=0) reason="MVP требует прямоугольный BoxCollider пола без поворота.";
            return reason.Length==0;
        }
        public static bool Finite(float value) => !float.IsNaN(value)&&!float.IsInfinity(value);
    }
}
