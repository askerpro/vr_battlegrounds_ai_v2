using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VrBattlegrounds.Weapons.Sights;

namespace VrBattlegrounds.Editor.Gameplay.SightSplit
{
    /// <summary>
    /// Game-owned спецификация разделения вшитых прицелов. Перенесено из разборов iron-split 2026-10-12 (islands.json —
    /// AK105/Mk14/Viper/R08, меши Kinemation .asset; islands-2.json — стволы пака Hands и Herrington).
    /// Точки и окна селекторов — в единицах меша узла; ход — метры мира (по умолчанию 3 мм: оценок хода в разборах нет).
    /// Не входят: ShotgunReal (FABARM не калибруется), M16 (мушки нет — будет комплект),
    /// SRM12 / SniperRifle / MKR9 (донорские половины пересаживаются комплектом; у островов нет отдельных GO).
    /// Таблица сгенерирована из разборов (tasks/weapon-system/reports/sight-split/gen_table.py); правка — через разбор.
    /// </summary>
    public static class SightSplitTable
    {
        public static readonly IReadOnlyList<SightSplitSpec> All = new[]
        {
            new SightSplitSpec("AK105", "Assets/Prefabs/Weapons/AK105/AK105.prefab",
                "AK-105: высота сводится мушкой (столбик, остров 49); целик-планка — неподвижная опорная точка",
                null,
                P(SightPartRole.Rear, "MeshContainer/Base", "Assets/Art/Weapons/Kinemation/AK105/Meshes/AK105_Body.asset", 4300000L, 24991, 22756, false,
                  new Vector3(0.001442f, 0.204878f, -0.038206f), null, 0f, 0f, null,
                    S("целик: пластина с прорезью на конце планки", 100, 72, new Vector3(-0.010572f, 0.204378f, -0.038706f), new Vector3(0.013456f, 0.209906f, -0.030964f)),
                    S("прицельная планка", 86, 52, new Vector3(-0.005942f, 0.206378f, -0.035136f), new Vector3(0.008826f, 0.261277f, -0.031324f)),
                    S("хомутик, правая половина с кнопкой", 213, 190, new Vector3(0.003279f, 0.209456f, -0.037652f), new Vector3(0.018687f, 0.219125f, -0.027983f)),
                    S("хомутик, левая половина с кнопкой", 212, 188, new Vector3(-0.015802f, 0.209456f, -0.037652f), new Vector3(-0.000395f, 0.219125f, -0.027983f))),
                P(SightPartRole.Front, "MeshContainer/Base", "Assets/Art/Weapons/Kinemation/AK105/Meshes/AK105_Body.asset", 4300000L, 24991, 22756, true,
                  new Vector3(0.001442f, 0.477916f, -0.036423f), null, 0.003f, 0.003f, null,
                    S("мушка (столбик с квадратным основанием) в намушнике", 60, 26, new Vector3(-0.00045f, 0.476557f, -0.036923f), new Vector3(0.003334f, 0.480796f, -0.027871f)))),
            new SightSplitSpec("Mk14", "Assets/Prefabs/Weapons/Mk14/Mk14.prefab",
                "Mk14: высота — диоптр целика (острова 13+11); мушка M14 по высоте неподвижна",
                null,
                P(SightPartRole.Rear, "MeshContainer/Base", "Assets/Art/Weapons/Kinemation/Mk14/Meshes/Mk14_Body.asset", 4300000L, 28785, 30555, true,
                  // Ход вверх 8 мм: диоптр M14 регулируется по высоте в большом диапазоне; на стенде нужно +6,67 мм (решение 2026-10-12).
                  new Vector3(0.0f, 0.042082f, -0.043092f), null, 0.003f, 0.008f, null,
                    S("диоптр (диск с отверстием)", 286, 282, new Vector3(-0.005077f, 0.041582f, -0.048169f), new Vector3(0.005077f, 0.045554f, -0.034975f)),
                    S("полудиск-основание перед диоптром (едет вместе с ним)", 24, 16, new Vector3(-0.006324f, 0.044591f, -0.039471f), new Vector3(0.006324f, 0.05145f, -0.032862f))),
                P(SightPartRole.Front, "MeshContainer/Base", "Assets/Art/Weapons/Kinemation/Mk14/Meshes/Mk14_Body.asset", 4300000L, 28785, 30555, false,
                  new Vector3(0.0f, 0.667011f, -0.048577f), null, 0f, 0f, null,
                    S("столбик мушки", 62, 50, new Vector3(-0.003661f, 0.664077f, -0.049076f), new Vector3(0.003661f, 0.671398f, -0.037534f)))),
            new SightSplitSpec("Viper", "Assets/Prefabs/Weapons/Viper/Viper.prefab",
                "Viper: регулируемый целик на затворе (острова 3+4+2, едет с Slide/Bolt); мушка в пазу неподвижна по высоте",
                null,
                P(SightPartRole.Rear, "Slide/Bolt", "Assets/Art/Weapons/Kinemation/Viper/Meshes/Viper_Bolt.asset", 4300000L, 3776, 2946, true,
                  // Ход вниз 4 мм: на стенде нужно −3,07 мм (решение 2026-10-12).
                  new Vector3(0.015404f, -0.037009f, 0.0f), null, 0.004f, 0.003f, null,
                    S("регулируемый целик (корпус + лопатка с прорезью — один остров)", 350, 284, new Vector3(0.004167f, -0.037509f, -0.008987f), new Vector3(0.015904f, -0.002036f, 0.008987f)),
                    S("винт высоты (сверху)", 72, 54, new Vector3(0.010029f, -0.023304f, -0.003311f), new Vector3(0.013768f, -0.016683f, 0.003311f)),
                    S("винт горизонтали (сбоку)", 84, 66, new Vector3(0.005536f, -0.035507f, 0.007303f), new Vector3(0.013575f, -0.027468f, 0.010299f))),
                P(SightPartRole.Front, "MeshContainer/Base", "Assets/Art/Weapons/Kinemation/Viper/Meshes/Viper_Body.asset", 4300000L, 5831, 5892, false,
                  new Vector3(0.047275f, 0.128955f, 0.0f), null, 0f, 0f, null,
                    S("мушка (лопатка в пазу)", 72, 52, new Vector3(0.042381f, 0.127789f, -0.001536f), new Vector3(0.047775f, 0.145007f, 0.001536f)),
                    S("световод мушки", 44, 34, new Vector3(0.044094f, 0.128005f, -0.001078f), new Vector3(0.047243f, 0.143958f, 0.001078f)))),
            new SightSplitSpec("R08", "Assets/Prefabs/Weapons/R08/R08.prefab",
                "R08: регулируемый целик (острова 1+21); мушка неподвижна",
                null,
                P(SightPartRole.Rear, "MeshContainer/Base", "Assets/Art/Weapons/Kinemation/R08/Meshes/R08_Body.asset", 4300000L, 9018, 10587, true,
                  new Vector3(0.0f, -0.034553f, 0.023967f), null, 0.003f, 0.003f, null,
                    S("регулируемая планка целика с лопаткой и прорезью", 672, 644, new Vector3(-0.008744f, -0.038681f, 0.010086f), new Vector3(0.008726f, 0.02278f, 0.024467f)),
                    S("винт высоты сверху", 200, 306, new Vector3(-0.003021f, -0.030464f, 0.017111f), new Vector3(0.003865f, -0.023578f, 0.020559f))),
                P(SightPartRole.Front, "MeshContainer/Base", "Assets/Art/Weapons/Kinemation/R08/Meshes/R08_Body.asset", 4300000L, 9018, 10587, false,
                  new Vector3(0.0f, 0.124452f, 0.024285f), null, 0f, 0f, null,
                    S("лопатка мушки", 51, 42, new Vector3(-0.003026f, 0.123952f, 0.016542f), new Vector3(0.003026f, 0.142469f, 0.024785f)),
                    S("вставка-точка на заднем торце мушки", 64, 60, new Vector3(-0.001519f, 0.12233f, 0.020394f), new Vector3(0.001519f, 0.124952f, 0.023432f)))),
            new SightSplitSpec("MP5K", "Assets/Prefabs/Weapons/MP5K/MP5K.prefab",
                "MP5K: высота — столбик мушки (остров 16); барабан целика неподвижен",
                null,
                P(SightPartRole.Rear, "MeshContainer/Base", "Assets/ThirdParty/Hands_Weapons_Animations_Pack_Update/Modelas/Hands_Automatic_Rifle04/Mesh/Hands_Automatic_Rifle04_Mesh.FBX", 4300004L, 8075, 9716, false,
                  new Vector3(6.232000000e-06f, -0.025039529f, 0.02058094f), new Vector3(-2.800000000e-05f, 0.126708f, -0.055523f), 0f, 0f, null,
                    S("поворотный барабан (кольцо с 4 прорезями, рабочая — к глазу)", 558, 624, new Vector3(-0.001559109f, -0.025661443f, 0.017174641f), new Vector3(0.001573429f, -0.022152296f, 0.020692018f))),
                P(SightPartRole.Front, "MeshContainer/Base", "Assets/ThirdParty/Hands_Weapons_Animations_Pack_Update/Modelas/Hands_Automatic_Rifle04/Mesh/Hands_Automatic_Rifle04_Mesh.FBX", 4300004L, 8075, 9716, true,
                  new Vector3(-3.860000000e-07f, 0.033158388f, 0.020557236f), new Vector3(2.000000000e-06f, 0.126601f, 0.208308f), 0.003f, 0.003f, -0.904f,
                    S("столбик мушки с поперечной полкой-основанием (внутри кольца)", 72, 54, new Vector3(-0.001558719f, 0.033042856f, 0.018393771f), new Vector3(0.001560159f, 0.033751073f, 0.020669949f)))),
            new SightSplitSpec("Uzi", "Assets/Prefabs/Weapons/Uzi/Uzi.prefab",
                "Uzi: мушка двигается целым островом 10 вместе с защитными ушками (решение пользователя)",
                null,
                P(SightPartRole.Rear, "MeshContainer/Base", "Assets/ThirdParty/Hands_Weapons_Animations_Pack_Update/Modelas/Hands_Tommy_gun/Mesh/Tommy-Gun_Mesh.FBX", 4300010L, 9710, 9891, false,
                  new Vector3(-0.00480789f, 0.154510036f, -0.671060264f), new Vector3(-0.001003f, 0.074828f, -0.135934f), 0f, 0f, null,
                    S("задняя крышка ствольной коробки с прорезью целика на верхней кромке (два выступа)", 395, 372, new Vector3(-0.103678479f, -0.134214236f, -0.686015158f), new Vector3(0.092700108f, 0.156906112f, -0.612496705f))),
                P(SightPartRole.Front, "MeshContainer/Base", "Assets/ThirdParty/Hands_Weapons_Animations_Pack_Update/Modelas/Hands_Tommy_gun/Mesh/Tommy-Gun_Mesh.FBX", 4300010L, 9710, 9891, true,
                  new Vector3(-0.002843108f, 0.156582415f, 0.255283713f), new Vector3(-0.000593f, 0.075261f, 0.05737f), 0.003f, 0.003f, -0.904f,
                    S("мушка: столбик на трапециевидном основании между защитными ушками (один остров)", 342, 362, new Vector3(-0.069915852f, 0.073998654f, 0.195326091f), new Vector3(0.064264617f, 0.169659181f, 0.284653066f)))),
            new SightSplitSpec("Herrington", "Assets/Prefabs/Weapons/Herrington/Herrington.prefab",
                "Remington 11-87: подвижная передняя лопатка (острова 1+2), V-целик неподвижен (решение пользователя)",
                null,
                P(SightPartRole.Rear, "MeshContainer/Herrington_1187_Skeleton", "Assets/Art/Weapons/Kinemation/Herrington/Meshes/Herrington_Herrington_1187_Skeleton.asset", 4300000L, 1199, 1140, false,
                  new Vector3(-4.800000000e-08f, -0.125680938f, 0.052147239f), new Vector3(-0.0f, 0.059549f, 0.288004f), 0f, 0f, null,
                    S("лопатка целика с пологой V-прорезью", 262, 248, new Vector3(-0.007742338f, -0.136100269f, 0.044860912f), new Vector3(0.007742338f, -0.122092442f, 0.052647292f)),
                    S("основание целика", 98, 92, new Vector3(-0.005742907f, -0.13681919f, 0.038956636f), new Vector3(0.005742907f, -0.116141358f, 0.04638424f)),
                    S("поперечный штифт", 98, 92, new Vector3(-0.006332509f, -0.122287623f, 0.040073254f), new Vector3(0.006332509f, -0.117819638f, 0.044541246f)),
                    S("вставка-точка на верхней грани лопатки", 96, 118, new Vector3(-0.002401224f, -0.134984679f, 0.046544106f), new Vector3(0.002401224f, -0.130182275f, 0.048692181f))),
                P(SightPartRole.Front, "MeshContainer/Herrington_1187_Skeleton", "Assets/Art/Weapons/Kinemation/Herrington/Meshes/Herrington_Herrington_1187_Skeleton.asset", 4300000L, 1199, 1140, true,
                  new Vector3(-4.800000000e-08f, -0.426644057f, 0.04995976f), new Vector3(-0.0f, 0.057361f, 0.588968f), 0.003f, 0.003f, 1.65f,
                    S("мушка-лопатка (рампа) — «передняя бусина» из разбора", 130, 108, new Vector3(-0.003508605f, -0.442084349f, 0.041762014f), new Vector3(0.003508605f, -0.424669379f, 0.050459768f)),
                    S("фрагмент основания (ласточкин хвост), едет с лопаткой", 16, 6, new Vector3(-0.003587399f, -0.441465533f, 0.035770577f), new Vector3(0.003587319f, -0.429712349f, 0.044878642f)))),
            new SightSplitSpec("Revolver", "Assets/Prefabs/Weapons/Revolver/Revolver.prefab",
                "Revolver: подвижный целик (острова 2+4); мушка — гребень ствола, только точка",
                null,
                P(SightPartRole.Rear, "MeshContainer/Base", "Assets/ThirdParty/Hands_Weapons_Animations_Pack_Update/Modelas/Hands_Gun_03/Mesh/Hands_Gun_03.FBX", 4300024L, 3403, 4410, true,
                  new Vector3(0.000271947f, 0.130936965f, 0.220386015f), new Vector3(0.000192f, 0.155556f, -0.09242f), 0.003f, 0.003f, 0.392f,
                    S("лопатка целика с прорезью", 96, 94, new Vector3(-0.011597165f, 0.127521474f, 0.205425564f), new Vector3(0.012130254f, 0.13171288f, 0.221123602f)),
                    S("планка целика в канале рамки (едет вместе)", 107, 108, new Vector3(-0.008724814f, 0.083141562f, 0.205353726f), new Vector3(0.009065358f, 0.128923889f, 0.214233708f))),
                P(SightPartRole.Front, "MeshContainer/Base", "Assets/ThirdParty/Hands_Weapons_Animations_Pack_Update/Modelas/Hands_Gun_03/Mesh/Hands_Gun_03.FBX", 4300024L, 3403, 4410, false,
                  new Vector3(0.000300283f, -0.214232281f, 0.220408052f), new Vector3(0.000212f, 0.155571f, 0.151212f), 0f, 0f, null)),
            new SightSplitSpec("Gun", "Assets/Prefabs/Weapons/BrowningHiPower/BrowningHiPower.prefab",
                "Browning Hi-Power: подвижная мушка (остров 2), обе половины в затворе Slide/Gate",
                null,
                P(SightPartRole.Rear, "Slide/Gate", "Assets/ThirdParty/Hands_Weapons_Animations_Pack_Update/Modelas/Hands_Gun/Mesh/Gun_Mesh.FBX", 4300006L, 1118, 1165, false,
                  new Vector3(-0.000240838f, 0.117417925f, 0.027741641f), new Vector3(-0.0f, 0.048813f, -0.057319f), 0f, 0f, null,
                    S("целик в пазу затвора (прямоугольная прорезь)", 68, 62, new Vector3(-0.013447561f, 0.105505969f, 0.016617859f), new Vector3(0.012966065f, 0.121172573f, 0.028476935f))),
                P(SightPartRole.Front, "Slide/Gate", "Assets/ThirdParty/Hands_Weapons_Animations_Pack_Update/Modelas/Hands_Gun/Mesh/Gun_Mesh.FBX", 4300006L, 1118, 1165, true,
                  new Vector3(-0.000240788f, -0.121954351f, 0.02780981f), new Vector3(-0.0f, 0.048859f, 0.105454f), 0.003f, 0.003f, -0.19f,
                    S("мушка (столбик) на затворе", 74, 62, new Vector3(-0.010594136f, -0.135565635f, 0.018774975f), new Vector3(0.010112639f, -0.120109398f, 0.028545104f)))),
            new SightSplitSpec("PPK", "Assets/Prefabs/Weapons/PPK/PPK.prefab",
                "PPK: прицел — одна оболочка с затвором; без подвижной детали, только точки (решение пользователя)",
                null,
                P(SightPartRole.Rear, "Slide/Gun02_Detail_01", "Assets/ThirdParty/Hands_Weapons_Animations_Pack_Update/Modelas/Hands_Gun02/Mesh/Hands_Gun_02_Mesh.FBX", 4300006L, 2328, 3084, false,
                  new Vector3(1.522100000e-05f, 0.091928937f, 0.107895893f), new Vector3(1.000000000e-05f, 0.066796f, -0.049033f), 0f, 0f, null),
                P(SightPartRole.Front, "Slide/Gun02_Detail_01", "Assets/ThirdParty/Hands_Weapons_Animations_Pack_Update/Modelas/Hands_Gun02/Mesh/Hands_Gun_02_Mesh.FBX", 4300006L, 2328, 3084, false,
                  new Vector3(-5.200000000e-08f, -0.136892572f, 0.10786551f), new Vector3(0.0f, 0.066776f, 0.100746f), 0f, 0f, null)),
            new SightSplitSpec("Scar", "Assets/Prefabs/Weapons/Scar/Scar.prefab",
                "SCAR-L: сначала подъём рамок целика и мушки, затем подвижный столбик мушки (остров 2)",
                new[]
                {
                    R("MeshContainer/Scar_Detail_02", new Vector3(-0.006117362f, 0.14671227f, -0.08676169f), new Quaternion(1.0f, -3.139165300e-07f, 2.264936800e-07f, -9.777372000e-08f), new Vector3(-0.006117362f, 0.14665573f, -0.086706059f), new Quaternion(-0.707106712f, 3.820000000e-07f, 6.200000000e-08f, 0.70710685f), new Vector3(-0.004381f, 0.105036f, -0.062139f), new Vector3(1.0f, 0.0f, 0.0f), 90.0f),
                    R("MeshContainer/Scar_Detail_03", new Vector3(-0.0065060635f, 0.13690236f, 0.38337642f), new Quaternion(3.780752800e-08f, -2.264936800e-07f, 3.139166000e-07f, 1.0f), new Vector3(-0.006506064f, 0.136880006f, 0.38336706f), new Quaternion(-0.707106754f, 6.200000000e-08f, 3.820000000e-07f, 0.707106808f), new Vector3(-0.00466f, 0.098039f, 0.274581f), new Vector3(1.0f, 0.0f, 0.0f), -90.0f)
                },
                P(SightPartRole.Rear, "MeshContainer/Scar_Detail_02", "Assets/ThirdParty/Hands_Weapons_Animations_Pack_Update/Modelas/Hands_Automatic_Rifle01/Mesh/Hands_Automatic_Rifle01.FBX", 1854231415305027722L, 1569, 1574, false,
                  new Vector3(9.886000000e-06f, 0.001774637f, 0.041673966f), new Vector3(-0.004374f, 0.134041f, -0.063334f), 0f, 0f, null,
                    S("кольцо диоптра на конце рамки", 224, 220, new Vector3(-0.004661093f, -0.009432602f, 0.034503553f), new Vector3(0.004723865f, 0.00249302f, 0.046603068f)),
                    S("рамка целика", 320, 384, new Vector3(-0.008057905f, -0.008275977f, -0.01034649f), new Vector3(0.008058935f, 0.007840872f, 0.030105762f)),
                    S("рамка, задняя часть", 143, 130, new Vector3(-0.006996979f, -0.006949568f, 0.028736895f), new Vector3(0.006996981f, 0.007044392f, 0.046461068f)),
                    S("втулка/барабан на оси", 342, 336, new Vector3(-0.010926825f, -0.006632615f, -0.006253053f), new Vector3(0.010926827f, 0.005522481f, 0.005902062f)),
                    S("маховик правый (на оси шарнира)", 270, 252, new Vector3(0.013337583f, -0.011526662f, -0.011529885f), new Vector3(0.024908188f, 0.011642085f, 0.01153082f)),
                    S("маховик левый (на оси шарнира)", 270, 252, new Vector3(-0.024908188f, -0.011526662f, -0.011529885f), new Vector3(-0.01333758f, 0.011642085f, 0.01153082f))),
                P(SightPartRole.Front, "MeshContainer/Scar_Detail_03", "Assets/ThirdParty/Hands_Weapons_Animations_Pack_Update/Modelas/Hands_Automatic_Rifle01/Mesh/Hands_Automatic_Rifle01.FBX", -8593926231563432924L, 744, 704, true,
                  new Vector3(0.000316833f, 0.000377283f, 0.052226502f), new Vector3(-0.004439f, 0.134384f, 0.274307f), 0.003f, 0.003f, -2.025f,
                    S("столбик мушки (внутри кольца-намушника)", 113, 100, new Vector3(-0.004469367f, -0.005692025f, 0.03611593f), new Vector3(0.004906299f, 0.00417862f, 0.052944887f))))
        };

        public static SightSplitSpec Get(string weaponId) =>
            All.FirstOrDefault(s => s.WeaponId == weaponId)
            ?? throw new ArgumentException($"Нет спецификации разделения прицела для '{weaponId}'. Есть: {string.Join(", ", All.Select(s => s.WeaponId))}");

        private static SightIslandSelector S(string role, int vertices, int triangles, Vector3 min, Vector3 max) =>
            new SightIslandSelector(role, vertices, triangles, min, max);

        private static SightSplitPartSpec P(SightPartRole role, string node, string meshPath, long meshLocalId, int vertices,
                                            int triangles, bool movable, Vector3 point, Vector3? pointPrefab, float travelDown,
                                            float travelUp, float? requiredShiftMm, params SightIslandSelector[] selectors) =>
            new SightSplitPartSpec(role, node, meshPath, meshLocalId, vertices, triangles, movable, point, pointPrefab,
                                   travelDown, travelUp, requiredShiftMm, selectors);

        private static SightNodeRaise R(string node, Vector3 foldedPosition, Quaternion foldedRotation, Vector3 raisedPosition,
                                        Quaternion raisedRotation, Vector3 axisPoint, Vector3 axisDir, float angle) =>
            new SightNodeRaise(node, foldedPosition, foldedRotation, raisedPosition, raisedRotation, axisPoint, axisDir, angle);
    }
}
