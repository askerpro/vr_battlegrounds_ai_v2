using System;

namespace UltimateXR.Mechanics.Weapons
{
    public partial class UxrActor
    {
        /// <summary>
        ///     Event triggered when the actor died.
        /// </summary>
        public event Action<UxrActor> Died;

        /// <summary>
        ///     Gets or sets whether to automatically destroy the GameObject when the actor dies.
        /// </summary>
        public bool AutoDestroyOnDie
        {
            get => _autoDestroyOnDie;
            set => _autoDestroyOnDie = value;
        }
    }
}
