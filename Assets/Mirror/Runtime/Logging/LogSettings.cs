using System;
using System.Collections.Generic;
using FlaxEngine;

namespace Mirror.Logging
{
    public class LogSettings //: ScriptableObject
    {
        public List<LoggerSettings> loglevels = new List<LoggerSettings>();

        [Serializable]
        public struct LoggerSettings
        {
            public string name;
            public LogType logLevel;
        }
    }

    public static class LogSettingsExt
    {
        public static void SaveFromDictionary(this LogSettings settings, SortedDictionary<string, ILogger> dictionary)
        {
            if (settings == null)
            {
                Debug.LogWarning("Could not SaveFromDictionary because LogSettings were null");
                return;
            }

#if FLAX_EDITOR
            using (new FlaxEditor.UndoBlock(FlaxEditor.Editor.Instance.Undo, settings, "Change Log Settings"))
#endif
            {
                settings.loglevels.Clear();

                foreach (KeyValuePair<string, ILogger> kvp in dictionary)
                {
                    settings.loglevels.Add(new LogSettings.LoggerSettings { name = kvp.Key, logLevel = kvp.Value.FilterLogType });
                }
            }
            //UnityEditor.EditorUtility.SetDirty(settings);

        }

        public static void LoadIntoDictionary(this LogSettings settings, SortedDictionary<string, ILogger> dictionary)
        {
            if (settings == null)
            {
                Debug.LogWarning("Could not LoadIntoDictionary because LogSettings were null");
                return;
            }

            foreach (LogSettings.LoggerSettings logLevel in settings.loglevels)
            {
                if (dictionary.TryGetValue(logLevel.name, out ILogger logger))
                {
                    logger.FilterLogType = logLevel.logLevel;
                }
                else
                {
                    logger = new Logger(Debug.Logger.LogHandler)
                    {
                        FilterLogType = logLevel.logLevel
                    };

                    dictionary[logLevel.name] = logger;
                }
            }
        }
    }
}
