using FlaxEngine;

namespace Mirror.Logging
{
    /// <summary>
    /// Used to replace log handler with Console Color LogHandler
    /// </summary>
    //[DisallowMultipleComponent]
    //[AddComponentMenu("Network/NetworkHeadlessLogger")]
    //[HelpURL("https://mirror-networking.com/docs/Articles/Components/NetworkHeadlessLogger.html")]
    public class NetworkHeadlessLogger : Script
    {
#pragma warning disable CS0414 // unused private members
        [Serialize] bool showExceptionStackTrace = false;
#pragma warning restore CS0414 // unused private members

        void Awake()
        {
#if UNITY_SERVER
            LogFactory.ReplaceLogHandler(new ConsoleColorLogHandler(showExceptionStackTrace));
#endif
        }
    }
}
