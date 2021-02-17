using Mirror.Cloud.ListServerService;
using FlaxEngine;

namespace Mirror.Cloud
{
    /// <summary>
    /// Used to requests and responses from the mirror api
    /// </summary>
    public interface IApiConnector
    {
        ListServer ListServer { get; }
    }

    /// <summary>
    /// Used to requests and responses from the mirror api
    /// </summary>
    //[DisallowMultipleComponent]
    ////[AddComponentMenu("Network/CloudServices/ApiConnector")]
    //[HelpURL("https://mirror-networking.com/docs/api/Mirror.Cloud.ApiConnector.html")]
    public class ApiConnector : Script, IApiConnector, ICoroutineRunner
    {
        #region Inspector
        [Header("Settings")]

        [Tooltip("Base URL of api, including https")]
        [Serialize] string ApiAddress = "";

        [Tooltip("Api key required to access api")]
        [Serialize] string ApiKey = "";

        [Header("Events")]

        [Tooltip("Triggered when server list Updates")]
        [Serialize] System.Action<ServerCollectionJson> _onServerListUpdated;
        #endregion

        IRequestCreator requestCreator;

        public ListServer ListServer { get; private set; }

        void Awake()
        {
            requestCreator = new RequestCreator(ApiAddress, ApiKey, this);

            InitListServer();
        }

        void InitListServer()
        {
            IListServerServerApi serverApi = new ListServerServerApi(this, requestCreator);
            IListServerClientApi clientApi = new ListServerClientApi(this, requestCreator, _onServerListUpdated);
            ListServer = new ListServer(serverApi, clientApi);
        }

        public void OnDestroy()
        {
            ListServer?.ServerApi.Shutdown();
            ListServer?.ClientApi.Shutdown();
        }
    }
}
