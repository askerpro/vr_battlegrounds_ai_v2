using UnityEngine;

namespace VrBattlegrounds.Interaction
{
    [RequireComponent(typeof(Collider))]
    public class ColliderVisualizer : MonoBehaviour
    {
        public Color VolumeColor = new Color(0f, 1f, 0f, 0.3f); // Green by default
        
        private GameObject _visualObj;

        private void Start()
        {
            Collider col = GetComponent<Collider>();
            if (col == null) return;

            // Определяем тип коллайдера и создаем соответствующий примитив
            if (col is BoxCollider boxCol)
            {
                _visualObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
                // Привязываем ДО назначения локальных координат, чтобы они не исказились
                _visualObj.transform.SetParent(transform, false);
                _visualObj.transform.localPosition = boxCol.center;
                _visualObj.transform.localRotation = Quaternion.identity;
                _visualObj.transform.localScale = boxCol.size;
            }
            else if (col is SphereCollider sphereCol)
            {
                _visualObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                _visualObj.transform.SetParent(transform, false);
                _visualObj.transform.localPosition = sphereCol.center;
                _visualObj.transform.localRotation = Quaternion.identity;
                _visualObj.transform.localScale = Vector3.one * (sphereCol.radius * 2f);
            }
            else if (col is CapsuleCollider capsuleCol)
            {
                _visualObj = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                _visualObj.transform.SetParent(transform, false);
                _visualObj.transform.localPosition = capsuleCol.center;
                
                // Направление капсулы (0 = X, 1 = Y, 2 = Z)
                Vector3 rot = Vector3.zero;
                if (capsuleCol.direction == 0) rot = new Vector3(0, 0, 90);
                else if (capsuleCol.direction == 2) rot = new Vector3(90, 0, 0);
                
                _visualObj.transform.localRotation = Quaternion.Euler(rot);
                _visualObj.transform.localScale = new Vector3(capsuleCol.radius * 2f, capsuleCol.height / 2f, capsuleCol.radius * 2f);
            }
            else
            {
                // Если коллайдер не поддерживается (например, MeshCollider)
                return;
            }

            _visualObj.name = "DebugVisualizer";
            
            // Удаляем у него коллайдер, чтобы он не мешал физике
            Destroy(_visualObj.GetComponent<Collider>());

            // Настраиваем полупрозрачный материал
            Renderer rend = _visualObj.GetComponent<Renderer>();
            Material transparentMat = new Material(Shader.Find("Standard"));
            transparentMat.SetFloat("_Mode", 3); // Transparent mode
            transparentMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            transparentMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            transparentMat.SetInt("_ZWrite", 0);
            transparentMat.DisableKeyword("_ALPHATEST_ON");
            transparentMat.EnableKeyword("_ALPHABLEND_ON");
            transparentMat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            transparentMat.renderQueue = 3000;
            
            transparentMat.color = VolumeColor;
            rend.material = transparentMat;
        }

        private void OnDestroy()
        {
            if (_visualObj != null)
            {
                Destroy(_visualObj);
            }
        }
        
        // Для визуализации в редакторе Unity без запуска игры
        private void OnDrawGizmos()
        {
            Collider col = GetComponent<Collider>();
            if (col == null) return;

            Gizmos.color = VolumeColor;
            Gizmos.matrix = transform.localToWorldMatrix;

            if (col is BoxCollider boxCol)
            {
                Gizmos.DrawCube(boxCol.center, boxCol.size);
                
                Gizmos.color = new Color(VolumeColor.r, VolumeColor.g, VolumeColor.b, 1f);
                Gizmos.DrawWireCube(boxCol.center, boxCol.size);
            }
            else if (col is SphereCollider sphereCol)
            {
                Gizmos.DrawSphere(sphereCol.center, sphereCol.radius);
                
                Gizmos.color = new Color(VolumeColor.r, VolumeColor.g, VolumeColor.b, 1f);
                Gizmos.DrawWireSphere(sphereCol.center, sphereCol.radius);
            }
        }
    }
}
