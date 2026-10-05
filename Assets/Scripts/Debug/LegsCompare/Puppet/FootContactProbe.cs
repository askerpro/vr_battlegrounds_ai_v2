using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace VrBattlegrounds.DevTools.LegsCompare
{
    /// <summary>
    /// Скольжение стопы в опоре — по подошве, а не по кости стопы (замер стенда <see cref="AvatarPuppetStand"/>).
    ///
    /// <list type="bullet">
    /// <item><b>Опора</b>: нижняя точка подошвы (<see cref="FootProbe.Sole"/>, меш ботинка) ниже <see cref="ContactHeight"/>
    /// и вертикальная скорость подошвы меньше <see cref="ContactVerticalSpeed"/>.</item>
    /// <item><b>Скольжение</b> — горизонтальный путь середины подошвы (<see cref="FootProbe.SoleCenter"/>) за касание без первых и
    /// последних <see cref="EdgeTrim"/> с (удар пяткой и отрыв носка — перекат, а не скольжение).</item>
    /// </list>
    /// Старая метрика <c>LegsComparisonRig</c> (путь кости стопы, пока она ниже покоя + 3 см) завышала: в неё шли постановка и
    /// отрыв. Для выводов — только эта.
    /// </summary>
    public sealed class FootContactProbe
    {
        public const float ContactHeight = 0.02f;
        public const float ContactVerticalSpeed = 0.15f;
        public const float EdgeTrim = 0.06f;

        private readonly FootProbe _foot;
        private readonly List<(float t, Vector3 p)> _contact = new List<(float, Vector3)>();
        private float _prevSole = float.NaN;
        private float _time;

        // Сегмент.
        public float Slide;          // м
        public float MaxSlipSpeed;   // м/с
        public int Contacts;
        public float MaxPerContact;  // м

        /// <summary>Для следов на полу: кадр в опоре и проскальзывает ли (быстрее 5 см/с).</summary>
        public bool InContact { get; private set; }
        public bool Slipping { get; private set; }
        public Vector3 Point => _foot.SoleCenter;

        public FootContactProbe(FootProbe foot) => _foot = foot;

        public void ResetSegment()
        {
            Slide = MaxSlipSpeed = MaxPerContact = 0f;
            Contacts = 0;
        }

        /// <summary>Кадр — после <see cref="FootProbe.Measure"/> этой же стопы.</summary>
        public void Measure(float dt)
        {
            _time += dt;
            float sole = _foot.Sole;
            bool has = !float.IsNaN(sole);
            float vz = has && !float.IsNaN(_prevSole) ? Mathf.Abs(sole - _prevSole) / Mathf.Max(dt, 1e-4f) : 0f;
            _prevSole = sole;

            bool contact = has && sole < ContactHeight && vz < ContactVerticalSpeed;
            Slipping = false;
            if (contact)
            {
                if (_contact.Count > 0)
                {
                    float v = Flat(_foot.SoleCenter - _contact[_contact.Count - 1].p) / Mathf.Max(dt, 1e-4f);
                    Slipping = v > 0.05f;
                }
                _contact.Add((_time, _foot.SoleCenter));
            }
            else if (_contact.Count > 0) CloseContact();
            InContact = contact;
        }

        /// <summary>Конец сегмента: незакрытое касание считается по тому, что есть.</summary>
        public void Flush()
        {
            if (_contact.Count > 0) CloseContact();
        }

        private void CloseContact()
        {
            float start = _contact[0].t + EdgeTrim, end = _contact[_contact.Count - 1].t - EdgeTrim;
            float path = 0f;
            for (int i = 1; i < _contact.Count; i++)
            {
                if (_contact[i - 1].t < start || _contact[i].t > end) continue;
                float d = Flat(_contact[i].p - _contact[i - 1].p);
                path += d;
                MaxSlipSpeed = Mathf.Max(MaxSlipSpeed, d / Mathf.Max(_contact[i].t - _contact[i - 1].t, 1e-4f));
            }
            if (end > start)
            {
                Slide += path;
                Contacts++;
                MaxPerContact = Mathf.Max(MaxPerContact, path);
            }
            _contact.Clear();
        }

        /// <summary>«см/м · макс см/с · касаний · за касание ср/макс, см» для таблицы.</summary>
        public static string Summary(FootContactProbe l, FootContactProbe r, float bodyPath)
        {
            float k = bodyPath > 1e-3f ? 100f / bodyPath : 0f;
            string PerContact(FootContactProbe f) => f.Contacts > 0 ? string.Format(CultureInfo.InvariantCulture, "{0:0.0}/{1:0.0}", f.Slide / f.Contacts * 100f, f.MaxPerContact * 100f) : "—";
            return string.Format(CultureInfo.InvariantCulture, "{0:0.0}/{1:0.0} | {2:0}/{3:0} | {4}/{5} | {6} · {7}",
                l.Slide * k, r.Slide * k, l.MaxSlipSpeed * 100f, r.MaxSlipSpeed * 100f, l.Contacts, r.Contacts, PerContact(l), PerContact(r));
        }

        private static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;
    }
}
