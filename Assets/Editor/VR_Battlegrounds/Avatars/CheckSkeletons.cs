using UnityEngine;
using UnityEditor;

public static class CheckSkeletons
{
    [MenuItem("Tools/VR Battlegrounds/Avatars/Check Skeletons")]
    public static void Check()
    {
        Debug.Log("--- SKELETON CHECK START ---");
        
        string folder = "Assets/ThirdParty/Military Soldier Mega Bundle/Heavy Soldier/Prefabs";
        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { folder });
        
        foreach(string guid in guids) {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) continue;
            
            GameObject go = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            Animator anim = go.GetComponentInChildren<Animator>();
            
            if (anim != null && anim.avatar != null && anim.isHuman) {
                Transform lHand = anim.GetBoneTransform(HumanBodyBones.LeftHand);
                Transform rHand = anim.GetBoneTransform(HumanBodyBones.RightHand);
                Transform head = anim.GetBoneTransform(HumanBodyBones.Head);
                
                if (lHand != null && rHand != null && head != null) {
                    Vector3 lPos = go.transform.InverseTransformPoint(lHand.position);
                    Vector3 rPos = go.transform.InverseTransformPoint(rHand.position);
                    Vector3 hPos = go.transform.InverseTransformPoint(head.position);
                    Debug.Log($"[MATCH_DATA] {prefab.name} => Head: {hPos:F4} | LHand: {lPos:F4} | RHand: {rPos:F4}");
                }
            }
            GameObject.DestroyImmediate(go);
        }
        Debug.Log("--- SKELETON CHECK END ---");
    }
}
