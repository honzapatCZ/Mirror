using FlaxEditor;
using FlaxEngine;

namespace Mirror.Logging
{
#if FLAX_EDITOR
    public static class EditorLogSettingsLoader
    {
        [InitializeOnLoadMethod]
        static void Init()
        {
            // load settings first time LogFactory is used in the editor
            LoadLogSettingsIntoDictionary();
        }

        public static void LoadLogSettingsIntoDictionary()
        {
            LogSettings settings = FindLogSettings();
            if (settings != null)
            {
                settings.LoadIntoDictionary(LogFactory.loggers);
            }
        }

        static LogSettings cache;
        public static LogSettings FindLogSettings()
        {
            if (cache != null)
                return cache;

            System.Guid[] assetGuids = Content.GetAllAssetsByType(typeof(LogSettings));
            if (assetGuids.Length == 0)
                return null;

            System.Guid firstGuid = assetGuids[0];
                        
            cache = Content.Load<LogSettings>(firstGuid);

            if (assetGuids.Length > 2)
            {
                Debug.LogWarning("Found more than one LogSettings, Delete extra settings. Using first asset found: " + path);
            }
            Debug.Assert(cache != null, "Failed to load asset at: " + path);

            return cache;
        }
    }
#endif
}
