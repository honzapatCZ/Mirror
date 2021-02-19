using FlaxEngine;

namespace Mirror.Logging
{
    /// <summary>
    /// Used to load LogSettings in build
    /// </summary>
    //[DisallowMultipleComponent]
    //[AddComponentMenu("Network/NetworkLogSettings")]
    //[HelpURL("https://mirror-networking.com/docs/Articles/Components/NetworkLogSettings.html")]
    public class NetworkLogSettings : Script
    {
        [Header("Log Settings Asset")]
        [Serialize] internal LogSettings settings;

#if FLAX_EDITOR
        // called when component is added to GameObject
        void Reset()
        {
            LogSettings existingSettings = EditorLogSettingsLoader.FindLogSettings();
            if (existingSettings != null)
            {
                using (new FlaxEditor.UndoBlock(FlaxEditor.Editor.Instance.Undo, this, "Change Log Settings"))
                {
                    settings = existingSettings;
                }
                //UnityEditor.EditorUtility.SetDirty(this);
            }
        }
#endif

        public override void OnAwake()
        {
            base.OnAwake();
            RefreshDictionary();
        }

        void OnValidate()
        {
            // if settings field is changed
            RefreshDictionary();
        }

        void RefreshDictionary()
        {
            settings.LoadIntoDictionary(LogFactory.loggers);
        }
    }
}
