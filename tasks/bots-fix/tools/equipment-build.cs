var folder = "Assets/Resources/Bots/EquipmentProbe";
System.IO.Directory.CreateDirectory(folder);
UnityEditor.AssetDatabase.Refresh();
var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
var results = new System.Collections.Generic.List<object>();
try {
    foreach (var id in new[]{"PPK","AK105"}) {
        var data = UnityEditor.AssetDatabase.LoadAssetAtPath<VrBattlegrounds.Arsenal.WeaponInfo>("Assets/Data/Weapons/"+id+"_Weapon.asset");
        var instance = (UnityEngine.GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(data.WeaponPrefab, scene);
        try {
            if(instance.GetComponent<VrBattlegrounds.Weapons.WeaponEquipmentBinding>()==null) instance.AddComponent<VrBattlegrounds.Weapons.WeaponEquipmentBinding>();
            var transport=instance.GetComponent<Mirror.NetworkTransformBase>();
            if(transport==null)transport=instance.AddComponent<Mirror.NetworkTransformUnreliable>();
            transport.target=instance.transform;
            transport.syncDirection=Mirror.SyncDirection.ClientToServer;
            var saved=UnityEditor.PrefabUtility.SaveAsPrefabAsset(instance,folder+"/"+id+".prefab");
            if(saved==null) throw new System.InvalidOperationException("Не сохранён probe prefab: "+id);
            results.Add(new{id=id,behaviours=saved.GetComponents<Mirror.NetworkBehaviour>().Length});
        }finally{UnityEngine.Object.DestroyImmediate(instance);}
    }
}finally{UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);}
return new{prepared=true,models=results};
