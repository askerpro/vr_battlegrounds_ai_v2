using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Maps;

namespace VrBattlegrounds.EditorTools
{
    /// <summary>Пять оформленных дорожек; мишенями и дистанцией попадания продолжает владеть игровой код.</summary>
    public static class LobbyRangeDressingBuilder
    {
        public static void Build(Transform parent, Bounds arena, Bounds tent)
        {
            var root = new GameObject("ShootingRangeDressing"); root.transform.SetParent(parent, false);
            var gravel = Material("RangeGravel", new Color(.42f,.39f,.32f));
            gravel.SetTexture("_BaseMap", LobbyMilitaryCampSources.Texture("T_GroundIntense_BC"));
            gravel.SetTextureScale("_BaseMap", new Vector2(1.3f,2.4f)); EditorUtility.SetDirty(gravel);
            var white = Material("LaneBoard", new Color(.68f,.65f,.55f));
            var black = Material("LaneNumber", new Color(.07f,.08f,.07f));
            var metal = Material("LanePost", new Color(.17f,.20f,.17f));
            var targets = parent.gameObject.scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<ShootingTarget>(true)).OrderBy(t=>t.name,StringComparer.Ordinal).ToArray();
            for (int i=0;i<targets.Length;i++)
            {
                float x=targets[i].transform.position.x, z=tent.max.z+15;
                var lane = new GameObject("RangeLane_"+(i+1)); lane.transform.SetParent(root.transform,false);
                Box(lane.transform,"Уплотнённая площадка",new Vector3(x,arena.max.y+.012f,z-.7f),new Vector3(2.6f,.012f,4.8f),gravel);
                // Табличка выше щита: низкие лучи к любому соседнему щиту не пересекают номер.
                Box(lane.transform,"Номер дорожки",new Vector3(x,arena.max.y+2.65f,z+.35f),new Vector3(.58f,.72f,.035f),white);
                Box(lane.transform,"Стойка номера",new Vector3(x+.42f,arena.max.y+1.5f,z+.42f),new Vector3(.035f,3,.035f),metal);
                Digit(lane.transform,i+1,new Vector3(x,arena.max.y+2.65f,z+.328f),black);
            }
        }
        private static void Digit(Transform parent,int number,Vector3 p,Material black)
        {
            string[] patterns={"","bc","abged","abgcd","fgbc","afgcd"};
            foreach(char s in patterns[number])
            {
                bool horizontal=s=='a'||s=='g'||s=='d';
                float x=s=='b'||s=='c'?.15f:s=='e'||s=='f'?-.15f:0;
                float y=s=='a'?.25f:s=='d'?-.25f:s=='b'||s=='f'?.125f:s=='c'||s=='e'?-.125f:0;
                Box(parent,"Цифра",p+new Vector3(x,y,0),horizontal?new Vector3(.32f,.055f,.007f):new Vector3(.055f,.25f,.007f),black);
            }
        }
        private static Material Material(string name,Color color)
        {
            string path="Assets/Materials/Lobby/"+name+".mat";
            var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!mat){mat=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(mat,path);}
            mat.SetColor("_BaseColor",color);mat.SetFloat("_Smoothness",.02f);EditorUtility.SetDirty(mat);return mat;
        }
        private static void Box(Transform parent,string name,Vector3 point,Vector3 size,Material material)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.SetParent(parent,false);
            go.transform.position=point;go.transform.localScale=size;UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            go.GetComponent<MeshRenderer>().sharedMaterial=material;
        }
    }
}
