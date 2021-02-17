using FlaxEditor;
using FlaxEngine;

namespace Mirror.Logging
{
#if FLAX_EDITOR
    public static class EditorLogSettingsLoader
    {
        [ExecuteInEditMode]
        static void Start()
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
                        
            cache = Content.Load<JsonAsset>(firstGuid).CreateInstance() as LogSettings;
            var asset = Content.Load<JsonAsset>(firstGuid);
            if (asset.CreateInstance() is LogSettings result)
                cache = result;
            else 
                cache = new LogSettings();

            if (assetGuids.Length > 2)
            {
                AssetInfo info;
                Content.GetAssetInfo(firstGuid, out info);
                Debug.LogWarning("Found more than one LogSettings, Delete extra settings. Using first asset found: " + info.Path);
            }
            Debug.Assert(cache != null, "Failed to load LogSettings");

            return cache;
        }
    }
#endif
}
