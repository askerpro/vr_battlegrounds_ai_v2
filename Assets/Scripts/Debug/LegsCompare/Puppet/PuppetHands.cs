using UnityEngine;

namespace VrBattlegrounds.DevTools.LegsCompare
{
    /// <summary>Как держит руки «игрок» стенда.</summary>
    public enum PuppetHandPose
    {
        /// <summary>Опущены вдоль тела.</summary>
        Down,
        /// <summary>Согнуты, кисти перед корпусом (готовность).</summary>
        Ready,
        /// <summary>Винтовка: правая у плеча на рукояти, левая на цевье.</summary>
        Rifle,
        /// <summary>Пистолет двумя руками перед собой.</summary>
        Pistol,
        /// <summary>Только сценарий из клипа: кисти — как в клипе.</summary>
        FromClip,
    }

    /// <summary>
    /// Позы кистей стенда в осях корпуса (x — вправо, y — вверх, z — вперёд; начало — глаза). Поворот — в «универсальных»
    /// осях кисти UltimateXR (<c>HandUniversalLocalAxes</c>): forward — к пальцам, up — тыльная сторона, единичный поворот —
    /// ладонь вниз, пальцы вперёд. На кость кисти аватара переводит <see cref="AvatarPuppetStand"/>.
    /// </summary>
    public static class PuppetHands
    {
        public static Pose Get(PuppetHandPose pose, bool left)
        {
            float s = left ? -1f : 1f;
            Vector3 outward = Vector3.right * s;
            switch (pose)
            {
                case PuppetHandPose.Ready:
                    // Локти согнуты ~90°, кисти перед поясом, ладони друг к другу.
                    return new Pose(new Vector3(0.17f * s, -0.62f, 0.32f), Quaternion.LookRotation(Vector3.forward, outward));
                case PuppetHandPose.Rifle:
                    return left
                        // Цевьё: впереди и чуть левее середины, ладонь снизу-сбоку (тыл — вниз-влево), пальцы вперёд-вправо.
                        ? new Pose(new Vector3(-0.04f, -0.36f, 0.58f), Quaternion.LookRotation(new Vector3(0.35f, 0f, 1f), new Vector3(-0.7f, -0.7f, 0f)))
                        // Рукоять у плеча: ладонь влево, пальцы вниз-вперёд.
                        : new Pose(new Vector3(0.13f, -0.33f, 0.26f), Quaternion.LookRotation(new Vector3(0f, -0.55f, 1f), Vector3.right));
                case PuppetHandPose.Pistol:
                    return left
                        ? new Pose(new Vector3(-0.03f, -0.28f, 0.43f), Quaternion.LookRotation(new Vector3(0.6f, -0.2f, 1f), new Vector3(-0.8f, -0.4f, 0f)))
                        : new Pose(new Vector3(0.04f, -0.25f, 0.46f), Quaternion.LookRotation(new Vector3(0f, -0.3f, 1f), Vector3.right));
                default:
                    // Вдоль тела: пальцы вниз, тыл наружу.
                    return new Pose(new Vector3(0.22f * s, -0.78f, 0.04f), Quaternion.LookRotation(Vector3.down, outward));
            }
        }
    }
}
