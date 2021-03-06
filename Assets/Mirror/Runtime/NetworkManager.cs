using System;
using System.Collections.Generic;
using System.Linq;
using kcp2k;
using FlaxEngine;

namespace Mirror
{
    /// <summary>
    /// Enumeration of methods of where to spawn player objects in multiplayer games.
    /// </summary>
    public enum PlayerSpawnMethod { Random, RoundRobin }

    /// <summary>
    /// Enumeration of methods of current Network Manager state at runtime.
    /// </summary>
    public enum NetworkManagerMode { Offline, ServerOnly, ClientOnly, Host }

    //[DisallowMultipleComponent]
    //[AddComponentMenu("Network/NetworkManager")]
    //[HelpURL("https://mirror-networking.com/docs/Articles/Components/NetworkManager.html")]
    public class NetworkManager : Script
    {
        static readonly ILogger logger = LogFactory.GetLogger<NetworkManager>();

        /// <summary>
        /// A flag to control whether the NetworkManager object is destroyed when the scene changes.
        /// <para>This should be set if your game has a single NetworkManager that exists for the lifetime of the process. If there is a NetworkManager in each scene, then this should not be set.</para>
        /// </summary>
        [Header("Configuration")]
        ////[FormerlySerializedAs("m_DontDestroyOnLoad")]
        [Tooltip("Should the Network Manager object be persisted through scene changes?")]
        [EditorOrder(0)]
        public bool dontDestroyOnLoad = true;

        /// <summary>
        /// Controls whether the program runs when it is in the background.
        /// <para>This is required when multiple instances of a program using networking are running on the same machine, such as when testing using localhost. But this is not recommended when deploying to mobile platforms.</para>
        /// </summary>
        //[FormerlySerializedAs("m_RunInBackground")]
        //[Tooltip("Should the server or client keep running in the background?")]
        //public bool runInBackground = true;

        /// <summary>
        /// Automatically invoke StartServer()
        /// <para>If the application is a Server Build, StartServer is automatically invoked.</para>
        /// <para>Server build is true when "Server build" is checked in build menu, or BuildOptions.EnableHeadlessMode flag is in BuildOptions</para>
        /// </summary>
        [Tooltip("Should the server auto-start when 'Server Build' is checked in build settings")]
        ////[FormerlySerializedAs("startOnHeadless")]
        [EditorOrder(1)]
        public bool autoStartServerBuild = true;

        /// <summary>
        /// Enables verbose debug messages in the console
        /// </summary>
        ////[FormerlySerializedAs("m_ShowDebugMessages")]
        [Tooltip("This will enable verbose debug messages in the Unity Editor console")]
        [EditorOrder(2)]
        public bool showDebugMessages;

        /// <summary>
        /// Server Update frequency, per second. Use around 60Hz for fast paced games like Counter-Strike to minimize latency. Use around 30Hz for games like WoW to minimize computations. Use around 1-10Hz for slow paced games like EVE.
        /// </summary>
        [Tooltip("Server Update frequency, per second. Use around 60Hz for fast paced games like Counter-Strike to minimize latency. Use around 30Hz for games like WoW to minimize computations. Use around 1-10Hz for slow paced games like EVE.")]
        [EditorOrder(3)]
        public int serverTickRate = 30;

        /// <summary>
        /// batching is still optional until we improve mirror's Update order.
        /// right now it increases latency because:
        ///   enabling batching flushes all state Updates in same frame, but
        ///   transport processes incoming messages afterwards so server would
        ///   batch them until next frame's flush
        /// => disable it for sUper fast paced games
        /// => enable it for high scale / cpu heavy games
        /// </summary>
        [Tooltip("Batching greatly reduces CPU & Transport load, but increases latency by one frame time. Use for high scale games / CPU intensive games. Don't use for fast paced games.")]
        [EditorOrder(4)]
        public bool serverBatching;

        /// <summary>
        /// batching from server to client.
        /// fewer transport calls give us significantly better performance/scale.
        /// if batch interval is 0, then we only batch until the Update() call
        /// </summary>
        [Tooltip("Server can batch messages Up to Transport.GetMaxPacketSize to significantly reduce transport calls and improve performance/scale.\nIf batch interval is 0, then we only batch until the Update() call. Otherwise we batch until interval elapsed (note that this increases latency).")]
        [EditorOrder(5)]
        public float serverBatchInterval = 0;

        /// <summary>
        /// The scene to switch to when offline.
        /// <para>Setting this makes the NetworkManager do scene management. This scene will be switched to when a network session is completed - such as a client disconnect, or a server shutdown.</para>
        /// </summary>
        [Header("Scene Management")]
        ////[FormerlySerializedAs("m_OfflineScene")]
        //[Scene]
        [Tooltip("Scene that Mirror will switch to when the client or server is stopped")]
        [EditorOrder(6)]
        public SceneReference offlineScene;

        /// <summary>
        /// The scene to switch to when online.
        /// <para>Setting this makes the NetworkManager do scene management. This scene will be switched to when a network session is started - such as a client connect, or a server listen.</para>
        /// </summary>
        //[Scene]
        ////[FormerlySerializedAs("m_OnlineScene")]
        [Tooltip("Scene that Mirror will switch to when the server is started. Clients will recieve a Scene Message to load the server's current scene when they connect.")]
        [EditorOrder(7)]
        public SceneReference onlineScene;

        // transport layer
        [Header("Network Info")]
        [Tooltip("Transport component attached to this object that server and client will use to connect")]
        [Serialize][ShowInEditor]
        [EditorOrder(8)]
        protected Transport transport;

        /// <summary>
        /// The network address currently in use.
        /// <para>For clients, this is the address of the server that is connected to. For servers, this is the local address.</para>
        /// </summary>
        ////[FormerlySerializedAs("m_NetworkAddress")]
        [Tooltip("Network Address where the client should connect to the server. Server does not use this for anything.")]
        [EditorOrder(9)]
        public string networkAddress = "localhost";

        /// <summary>
        /// The maximum number of concurrent network connections to sUpport.
        /// <para>This effects the memory usage of the network layer.</para>
        /// </summary>
        ////[FormerlySerializedAs("m_MaxConnections")]
        [Tooltip("Maximum number of concurrent connections.")]
        [EditorOrder(10)]
        public int maxConnections = 100;

        // This value is passed to NetworkServer in SetUpServer
        /// <summary>
        /// Should the server disconnect remote connections that have gone silent for more than Server Idle Timeout?
        /// </summary>
        [Tooltip("Server Only - Disconnects remote connections that have been silent for more than Server Idle Timeout")]
        [EditorOrder(11)]
        public bool disconnectInactiveConnections;

        // This value is passed to NetworkServer in SetUpServer
        /// <summary>
        /// Timeout in seconds since last message from a client after which server will auto-disconnect.
        /// <para>By default, clients send at least a Ping message every 2 seconds.</para>
        /// <para>The Host client is immune from idle timeout disconnection.</para>
        /// <para>Default value is 60 seconds.</para>
        /// </summary>
        [Tooltip("Timeout in seconds since last message from a client after which server will auto-disconnect if Disconnect Inactive Connections is enabled.")]
        [EditorOrder(12)]
        public float disconnectInactiveTimeout = 60f;

        [Header("Authentication")]
        [Tooltip("Authentication component attached to this object")]
        [EditorOrder(13)]
        public NetworkAuthenticator authenticator;

        /// <summary>
        /// The default prefab to be used to create player objects on the server.
        /// <para>Player objects are created in the default handler for AddPlayer() on the server. Implementing OnServerAddPlayer overrides this behaviour.</para>
        /// </summary>
        [Header("Player Object")]
        ////[FormerlySerializedAs("m_PlayerPrefab")]
        [Tooltip("Prefab of the player object. Prefab must have a Network Identity component. May be an empty game object or a full avatar.")]
        [EditorOrder(14)]
        public Prefab playerPrefab;

        /// <summary>
        /// A flag to control whether or not player objects are automatically created on connect, and on scene change.
        /// </summary>
        ////[FormerlySerializedAs("m_AutoCreatePlayer")]
        [Tooltip("Should Mirror automatically spawn the player after scene change?")]
        [EditorOrder(15)]
        public bool autoCreatePlayer = true;

        /// <summary>
        /// The current method of spawning players used by the NetworkManager.
        /// </summary>
        ////[FormerlySerializedAs("m_PlayerSpawnMethod")]
        [Tooltip("Round Robin or Random order of Start Position selection")]
        [EditorOrder(16)]
        public PlayerSpawnMethod playerSpawnMethod;

        /// <summary>
        /// List of prefabs that will be registered with the spawning system.
        /// <para>For each of these prefabs, ClientScene.RegisterPrefab() will be automatically invoked.</para>
        /// </summary>
        ////[FormerlySerializedAs("m_SpawnPrefabs"), HideInEditor]
        [EditorOrder(17)]
        public List<Prefab> spawnPrefabs = new List<Prefab>();

        /// <summary>
        /// NetworkManager singleton
        /// </summary>
        public static NetworkManager singleton { get; private set; }

        /// <summary>
        /// Number of active player objects across all connections on the server.
        /// <para>This is only valid on the host / server.</para>
        /// </summary>
        public int numPlayers => NetworkServer.connections.Count(kv => kv.Value.identity != null);

        /// <summary>
        /// True if the server or client is started and running
        /// <para>This is set True in StartServer / StartClient, and set False in StopServer / StopClient</para>
        /// </summary>
        [NonSerialized][HideInEditor]
        public bool isNetworkActive;

        static NetworkConnection clientReadyConnection;

        /// <summary>
        /// This is true if the client loaded a new scene when connecting to the server.
        /// <para>This is set before OnClientConnect is called, so it can be checked there to perform different logic if a scene load occurred.</para>
        /// </summary>
        [NonSerialized][HideInEditor]
        public bool clientLoadedScene;

        // helper enum to know if we started the networkmanager as server/client/host.
        // -> this is necessary because when StartHost changes server scene to
        //    online scene, FinishLoadScene is called and the host client isn't
        //    connected yet (no need to connect it before server was fully set Up).
        //    in other words, we need this to know which mode we are running in
        //    during FinishLoadScene.
        public NetworkManagerMode mode { get; private set; }

        #region Unity Callbacks

        /// <summary>
        /// virtual so that inheriting classes' OnValidate() can call base.OnValidate() too
        /// </summary>
        public virtual void OnValidate()
        {
            // add transport if there is none yet. makes Upgrading easier.
            if (transport == null)
            {
                // was a transport added yet? if not, add one
                /*
                transport = Actor.GetScript<Transport>();
                if (transport == null)
                {
                    transport = Actor.AddScript<KcpTransport>();
                    logger.Log("NetworkManager: added default Transport because there was none yet.");
                }
                */
                // For some insane reason, this line fails when building unless wrapped in this define. StUpid but true.
                // error CS0234: The type or namespace name 'Undo' does not exist in the namespace 'UnityEditor' (are you missing an assembly reference?)
                //UnityEditor.Undo.RecordObject(gameObject, "Added default Transport");
#if FLAX_EDITOR
                using (new FlaxEditor.UndoBlock(FlaxEditor.Editor.Instance.Undo, this, "Change Log Settings"))
                {
                    transport = Actor.GetScript<Transport>();
                    if (transport == null)
                    {
                        transport = Actor.AddScript<KcpTransport>();
                        logger.Log("NetworkManager: added default Transport because there was none yet.");
                    }
                }
#endif
            }
            // always >= 0
            maxConnections = Mathf.Max(maxConnections, 0);

            if (playerPrefab != null && playerPrefab.GetDefaultInstance().GetScript<NetworkIdentity>() == null)
            {
                logger.LogError("NetworkManager - playerPrefab must have a NetworkIdentity.");
                playerPrefab = null;
            }
        }

        /// <summary>
        /// virtual so that inheriting classes' Awake() can call base.Awake() too
        /// </summary>
        public override void OnAwake()
        {
            base.OnAwake();

            OnValidate();

            // Don't allow collision-destroyed second instance to continue.
            if (!InitializeSingleton()) return;

            logger.Log("Thank you for using Mirror! https://mirror-networking.com");

            // Set the networkSceneName to prevent a scene reload
            // if client connection to server fails.
            networkSceneName = offlineScene;

            // setUp OnSceneLoaded callback
            Level.SceneLoaded += OnSceneLoaded;
            //SceneManager.sceneLoaded += OnSceneLoaded;
        }

        /// <summary>
        /// virtual so that inheriting classes' Start() can call base.Start() too
        /// </summary>
        public virtual void Start()
        {
            // headless mode? then start the server
            // can't do this in Awake because Awake is for initialization.
            // some transports might not be ready until Start.
            //
            // (tick rate is applied in StartServer!)

            if (autoStartServerBuild)
            {
                StartServer();
            }

        }

        // NetworkIdentity.UNetStaticUpdate is called from UnityEngine while LLAPI network is active.
        // If we want TCP then we need to call it manually. Probably best from NetworkManager, although this means that we can't use NetworkServer/NetworkClient without a NetworkManager invoking Update anymore.
        /// <summary>
        /// virtual so that inheriting classes' LateUpdate() can call base.LateUpdate() too
        /// </summary>
        public override void OnLateUpdate()
        {
            base.OnLateUpdate();
            // call it while the NetworkManager exists.
            // -> we don't only call while Client/Server.Connected, because then we would stop if disconnected and the
            //    NetworkClient wouldn't receive the last Disconnect event, result in all kinds of issues
            NetworkServer.Update();
            NetworkClient.Update();
            //UpdateScene();
        }

        #endregion

        #region Start & Stop

        // keep the online scene change check in a separate function
        bool IsServerOnlineSceneChangeNeeded()
        {
            // Only change scene if the requested online scene is not blank, and is not already loaded
            return onlineScene.ID != Guid.Empty && !IsSceneActive(onlineScene) && onlineScene.ID != offlineScene.ID;
        }

        public static bool IsSceneActive(SceneReference newSceneName)
        {
            IEnumerable<Scene> scenes = Level.Scenes.Where(scene => scene.ID == newSceneName.ID);
            return scenes.Count() > 0;
        }

        // full server setUp code, without spawning objects yet
        void SetUpServer()
        {
            if (logger.LogEnabled()) logger.Log("NetworkManager SetUpServer");
            InitializeSingleton();

              //  Application.runInBackground = true;

            if (authenticator != null)
            {
                authenticator.OnStartServer();
                authenticator.OnServerAuthenticated += (OnServerAuthenticated);
            }

            ConfigureServerFrameRate();

            // batching
            NetworkServer.batching = serverBatching;
            NetworkServer.batchInterval = serverBatchInterval;

            // Copy auto-disconnect settings to NetworkServer
            NetworkServer.disconnectInactiveTimeout = disconnectInactiveTimeout;
            NetworkServer.disconnectInactiveConnections = disconnectInactiveConnections;

            // start listening to network connections
            NetworkServer.Listen(maxConnections);

            // call OnStartServer AFTER Listen, so that NetworkServer.active is
            // true and we can call NetworkServer.Spawn in OnStartServer
            // overrides.
            // (useful for loading & spawning stuff from database etc.)
            //
            // note: there is no risk of someone connecting after Listen() and
            //       before OnStartServer() because this all runs in one thread
            //       and we don't start processing connects until Update.
            OnStartServer();

            // this must be after Listen(), since that registers the default message handlers
            RegisterServerMessages();

            isNetworkActive = true;
        }

        /// <summary>
        /// This starts a new server.
        /// </summary>
        public void StartServer()
        {
            if (NetworkServer.active)
            {
                logger.LogWarning("Server already started.");
                return;
            }

            mode = NetworkManagerMode.ServerOnly;

            // StartServer is inherently ASYNCHRONOUS (=doesn't finish immediately)
            //
            // Here is what it does:
            //   Listen
            //   if onlineScene:
            //       LoadSceneAsync
            //       ...
            //       FinishLoadSceneServerOnly
            //           SpawnObjects
            //   else:
            //       SpawnObjects
            //
            // there is NO WAY to make it synchronous because both LoadSceneAsync
            // and LoadScene do not finish loading immediately. as long as we
            // have the onlineScene feature, it will be asynchronous!

            SetUpServer();

            // scene change needed? then change scene and spawn afterwards.
            if (IsServerOnlineSceneChangeNeeded())
            {
                ServerChangeScene(onlineScene);
            }
            // otherwise spawn directly
            else
            {
                NetworkServer.SpawnObjects();
            }
        }

        /// <summary>
        /// This starts a network client. It uses the networkAddress property as the address to connect to.
        /// <para>This makes the newly created client connect to the server immediately.</para>
        /// </summary>
        public void StartClient()
        {
            if (NetworkClient.active)
            {
                logger.LogWarning("Client already started.");
                return;
            }

            mode = NetworkManagerMode.ClientOnly;

            InitializeSingleton();

            if (authenticator != null)
            {
                authenticator.OnStartClient();
                authenticator.OnClientAuthenticated += (OnClientAuthenticated);
            }
            //if (runInBackground)
              //  Application.runInBackground = true;

            isNetworkActive = true;

            RegisterClientMessages();

            if (string.IsNullOrEmpty(networkAddress))
            {
                logger.LogError("Must set the Network Address field in the manager");
                return;
            }
            if (logger.LogEnabled()) logger.Log("NetworkManager StartClient address:" + networkAddress);

            NetworkClient.Connect(networkAddress);

            OnStartClient();
        }

        /// <summary>
        /// This starts a network client. It uses the Uri parameter as the address to connect to.
        /// <para>This makes the newly created client connect to the server immediately.</para>
        /// </summary>
        /// <param name="uri">location of the server to connect to</param>
        public void StartClient(Uri uri)
        {
            if (NetworkClient.active)
            {
                logger.LogWarning("Client already started.");
                return;
            }

            mode = NetworkManagerMode.ClientOnly;

            InitializeSingleton();

            if (authenticator != null)
            {
                authenticator.OnStartClient();
                authenticator.OnClientAuthenticated += (OnClientAuthenticated);
            }

            //if (runInBackground)
            //    Application.runInBackground = true;


            isNetworkActive = true;

            RegisterClientMessages();

            if (logger.LogEnabled()) logger.Log("NetworkManager StartClient address:" + uri);
            networkAddress = uri.Host;

            NetworkClient.Connect(uri);

            OnStartClient();
        }

        /// <summary>
        /// This starts a network "host" - a server and client in the same application.
        /// <para>The client returned from StartHost() is a special "local" client that communicates to the in-process server using a message queue instead of the real network. But in almost all other cases, it can be treated as a normal client.</para>
        /// </summary>
        public void StartHost()
        {
            if (NetworkServer.active || NetworkClient.active)
            {
                logger.LogWarning("Server or Client already started.");
                return;
            }

            mode = NetworkManagerMode.Host;

            // StartHost is inherently ASYNCHRONOUS (=doesn't finish immediately)
            //
            // Here is what it does:
            //   Listen
            //   ConnectHost
            //   if onlineScene:
            //       LoadSceneAsync
            //       ...
            //       FinishLoadSceneHost
            //           FinishStartHost
            //               SpawnObjects
            //               StartHostClient      <= not guaranteed to happen after SpawnObjects if onlineScene is set!
            //                   ClientAuth
            //                       success: server sends changescene msg to client
            //   else:
            //       FinishStartHost
            //
            // there is NO WAY to make it synchronous because both LoadSceneAsync
            // and LoadScene do not finish loading immediately. as long as we
            // have the onlineScene feature, it will be asynchronous!

            // setUp server first
            SetUpServer();

            // call OnStartHost AFTER SetUpServer. this way we can use
            // NetworkServer.Spawn etc. in there too. just like OnStartServer
            // is called after the server is actually properly started.
            OnStartHost();

            // scene change needed? then change scene and spawn afterwards.
            // => BEFORE host client connects. if client auth succeeds then the
            //    server tells it to load 'onlineScene'. we can't do that if
            //    server is still in 'offlineScene'. so load on server first.
            if (IsServerOnlineSceneChangeNeeded())
            {
                // call FinishStartHost after changing scene.
                finishStartHostPending = true;
                ServerChangeScene(onlineScene);
            }
            // otherwise call FinishStartHost directly
            else
            {
                FinishStartHost();
            }
        }

        // This may be set true in StartHost and is evaluated in FinishStartHost
        bool finishStartHostPending;

        // FinishStartHost is guaranteed to be called after the host server was
        // fully started and all the asynchronous StartHost magic is finished
        // (= scene loading), or immediately if there was no asynchronous magic.
        //
        // note: we don't really need FinishStartClient/FinishStartServer. the
        //       host version is enough.
        void FinishStartHost()
        {
            // ConnectHost needs to be called BEFORE SpawnObjects:
            // https://github.com/vis2k/Mirror/pull/1249/
            // -> this sets NetworkServer.localConnection.
            // -> localConnection needs to be set before SpawnObjects because:
            //    -> SpawnObjects calls OnStartServer in all NetworkBehaviours
            //       -> OnStartServer might spawn an object and set [SyncVar(hook="OnColorChanged")] object.color = Green;
            //          -> this calls SyncVar.set (generated by Weaver), which has
            //             a custom case for host mode (because host mode doesn't
            //             get OnDeserialize calls, where SyncVar hooks are usually
            //             called):
            //
            //               if (!SyncVarEqual(value, ref color))
            //               {
            //                   if (NetworkServer.localClientActive && !getSyncVarHookGuard(1uL))
            //                   {
            //                       setSyncVarHookGuard(1uL, value: true);
            //                       OnColorChangedHook(value);
            //                       setSyncVarHookGuard(1uL, value: false);
            //                   }
            //                   SetSyncVar(value, ref color, 1uL);
            //               }
            //
            //          -> localClientActive needs to be true, otherwise the hook
            //             isn't called in host mode!
            //
            // TODO call this after spawnobjects and worry about the syncvar hook fix later?
            NetworkClient.ConnectHost();

            // server scene was loaded. now spawn all the objects
            NetworkServer.SpawnObjects();

            // connect client and call OnStartClient AFTER server scene was
            // loaded and all objects were spawned.
            // DO NOT do this earlier. it would cause race conditions where a
            // client will do things before the server is even fully started.
            logger.Log("StartHostClient called");
            StartHostClient();
        }

        void StartHostClient()
        {
            logger.Log("NetworkManager ConnectLocalClient");

            if (authenticator != null)
            {
                authenticator.OnStartClient();
                authenticator.OnClientAuthenticated+=(OnClientAuthenticated);
            }

            networkAddress = "localhost";
            NetworkServer.ActivateHostScene();
            RegisterClientMessages();

            // ConnectLocalServer needs to be called AFTER RegisterClientMessages
            // (https://github.com/vis2k/Mirror/pull/1249/)
            NetworkClient.ConnectLocalServer();

            OnStartClient();
        }

        /// <summary>
        /// This stops both the client and the server that the manager is using.
        /// </summary>
        public void StopHost()
        {
            OnStopHost();

            // TODO try to move DisconnectLocalServer into StopClient(), and
            // then call StopClient() before StopServer(). needs testing!.

            // DisconnectLocalServer needs to be called so that the host client
            // receives a DisconnectMessage too.
            // fixes: https://github.com/vis2k/Mirror/issues/1515
            NetworkClient.DisconnectLocalServer();

            StopClient();
            StopServer();
        }

        /// <summary>
        /// Stops the server that the manager is using.
        /// </summary>
        public void StopServer()
        {
            if (!NetworkServer.active)
                return;

            if (authenticator != null)
            {
                authenticator.OnServerAuthenticated -= (OnServerAuthenticated);
                authenticator.OnStopServer();
            }

            OnStopServer();

            logger.Log("NetworkManager StopServer");
            isNetworkActive = false;
            NetworkServer.Shutdown();

            // set offline mode BEFORE changing scene so that FinishStartScene
            // doesn't think we need initialize anything.
            mode = NetworkManagerMode.Offline;

            if (offlineScene.ID != Guid.Empty)
            {
                ServerChangeScene(offlineScene);
            }

            startPositionIndex = 0;

            networkSceneName = new SceneReference();
        }

        /// <summary>
        /// Stops the client that the manager is using.
        /// </summary>
        public void StopClient()
        {
            if (authenticator != null)
            {
                authenticator.OnClientAuthenticated -= (OnClientAuthenticated);
                authenticator.OnStopClient();
            }

            OnStopClient();

            logger.Log("NetworkManager StopClient");
            isNetworkActive = false;

            // shutdown client
            NetworkClient.Disconnect();
            NetworkClient.Shutdown();

            // set offline mode BEFORE changing scene so that FinishStartScene
            // doesn't think we need initialize anything.
            mode = NetworkManagerMode.Offline;

            // If this is the host player, StopServer will already be changing scenes.
            // Check loadingSceneAsync to ensure we don't double-invoke the scene change.
            // Check if NetworkServer.active because we can get here via Disconnect before server has started to change scenes.
            if (offlineScene.ID != Guid.Empty && !IsSceneActive(offlineScene) && !NetworkServer.active)
            {
                ClientChangeScene(offlineScene, SceneOperation.Normal);
            }

            networkSceneName = new SceneReference();
        }

        /// <summary>
        /// called when quitting the application by closing the window / pressing stop in the editor
        /// <para>virtual so that inheriting classes' OnApplicationQuit() can call base.OnApplicationQuit() too</para>
        /// </summary>
        public virtual void OnApplicationQuit()
        {
            // stop client first
            // (we want to send the quit packet to the server instead of waiting
            //  for a timeout)
            if (NetworkClient.isConnected)
            {
                StopClient();
                logger.Log("OnApplicationQuit: stopped client");
            }

            // stop server after stopping client (for proper host mode stopping)
            if (NetworkServer.active)
            {
                StopServer();
                logger.Log("OnApplicationQuit: stopped server");
            }
        }

        /// <summary>
        /// Set the frame rate for a headless server.
        /// <para>Override if you wish to disable the behavior or set your own tick rate.</para>
        /// </summary>
        public virtual void ConfigureServerFrameRate()
        {
            // only set framerate for server build
#if UNITY_SERVER
            Application.targetFrameRate = serverTickRate;
            if (logger.logEnabled) logger.Log("Server Tick Rate set to: " + Application.targetFrameRate + " Hz.");
#endif
        }

        bool InitializeSingleton()
        {
            if (singleton != null && singleton == this) return true;

            // do this early
            LogFilter.Debug = showDebugMessages;
            if (LogFilter.Debug)
            {
                LogFactory.EnableDebugMode();
            }
            Debug.Log(singleton);

            if (dontDestroyOnLoad)
            {
                if (singleton != null)
                {
                    logger.LogWarning("Multiple NetworkManagers detected in the scene. Only one NetworkManager can exist at a time. The dUplicate NetworkManager will be destroyed.");
                    Destroy(Actor);

                    // Return false to not allow collision-destroyed second instance to continue.
                    return false;
                }    
                //if (Application.isPlaying)
                {
                    //DontDestroyOnLoad(Actor);
                    Debug.LogWarning("Making dontdestroyonload by using parent = null, which in turn completely removes it from any levels");
                    Actor.SetParent(Actor.Scene.Parent, true);
                    
                }
            }
            logger.Log("NetworkManager created singleton (DontDestroyOnLoad)");
            singleton = this;
            // set active transport AFTER setting singleton.
            // so only if we didn't destroy ourselves.
            logger.Log("Settign active transport to " + transport?.TypeName);
            Transport.activeTransport = transport;

            return true;
        }

        void RegisterServerMessages()
        {
            NetworkServer.OnConnectedEvent = OnServerConnectInternal;
            NetworkServer.OnDisconnectedEvent = OnServerDisconnectInternal;
            NetworkServer.RegisterHandler<AddPlayerMessage>(OnServerAddPlayerInternal);

            // Network Server initially registers its own handler for this, so we replace it here.
            NetworkServer.ReplaceHandler<ReadyMessage>(OnServerReadyMessageInternal);
        }

        void RegisterClientMessages()
        {
            NetworkClient.OnConnectedEvent = OnClientConnectInternal;
            NetworkClient.OnDisconnectedEvent = OnClientDisconnectInternal;
            NetworkClient.RegisterHandler<NotReadyMessage>(OnClientNotReadyMessageInternal);
            NetworkClient.RegisterHandler<SceneMessage>(OnClientSceneInternal, false);

            if (playerPrefab != null)
                ClientScene.RegisterPrefab(playerPrefab);

            foreach (Prefab prefab in spawnPrefabs.Where(t => t != null))
                ClientScene.RegisterPrefab(prefab);
        }

        /// <summary>
        /// This is the only way to clear the singleton, so another instance can be created.
        /// </summary>
        public static void Shutdown()
        {
            if (singleton == null)
                return;

            startPositions.Clear();
            startPositionIndex = 0;
            clientReadyConnection = null;

            singleton.StopHost();
            singleton = null;

            Transport.activeTransport = null;
        }

        /// <summary>
        /// virtual so that inheriting classes' OnDestroy() can call base.OnDestroy() too
        /// </summary>
        public override void OnDestroy()
        {
            base.OnDestroy();
            Level.SceneLoaded -= OnSceneLoaded;
            if (this == singleton)
                Shutdown();
            logger.Log("NetworkManager destroyed");
        }

        #endregion

        #region Scene Management

        /// <summary>
        /// The name of the current network scene.
        /// </summary>
        /// <remarks>
        /// <para>This is populated if the NetworkManager is doing scene management. Calls to ServerChangeScene() cause this to change. New clients that connect to a server will automatically load this scene.</para>
        /// <para>This is used to make sure that all scene changes are initialized by Mirror.</para>
        /// <para>Loading a scene manually wont set networkSceneName, so Mirror would still load it again on start.</para>
        /// </remarks>
        public static SceneReference networkSceneName { get; protected set; }

        //public static UnityEngine.AsyncOperation loadingSceneAsync;

        /// <summary>
        /// This causes the server to switch scenes and sets the networkSceneName.
        /// <para>Clients that connect to this server will automatically switch to this scene. This is called automatically if onlineScene or offlineScene are set, but it can be called from user code to switch scenes again while the game is in progress. This automatically sets clients to be not-ready during the change and ready again to participate in the new scene.</para>
        /// </summary>
        /// <param name="newSceneName"></param>
        public virtual void ServerChangeScene(SceneReference newSceneName)
        {
            if (newSceneName.ID == null)
            {
                logger.LogError("ServerChangeScene empty scene name");
                return;
            }

            if (logger.LogEnabled) logger.Log("ServerChangeScene " + newSceneName);
            NetworkServer.SetAllClientsNotReady();
            networkSceneName = newSceneName;

            // Let server prepare for scene change
            OnServerChangeScene(newSceneName);

            // Suspend the server's transport while changing scenes
            // It will be re-enabled in FinishScene.
            Transport.activeTransport.Enabled = false;

            //Level.LoadSceneAsync(Content.Load<SceneAsset>().);
            Level.ChangeSceneAsync(newSceneName);
            // ServerChangeScene can be called when stopping the server
            // when this happens the server is not active so does not need to tell clients about the change
            if (NetworkServer.active)
            {
                // notify all clients about the new scene
                NetworkServer.SendToAll(new SceneMessage { sceneName = newSceneName.ID });
            }

            startPositionIndex = 0;
            startPositions.Clear();
        }

        // This is only set in ClientChangeScene below...never on server.
        // We need to check this in OnClientSceneChanged called from FinishLoadSceneClientOnly
        // to prevent AddPlayer message after loading/unloading additive scenes
        SceneOperation clientSceneOperation = SceneOperation.Normal;

        internal void ClientChangeScene(SceneReference newSceneName, SceneOperation sceneOperation = SceneOperation.Normal, bool customHandling = false)
        {
            if (networkSceneName.ID == Guid.Empty)
            {
                logger.LogError("ClientChangeScene empty scene name");
                return;
            }

            if (logger.LogEnabled()) logger.Log("ClientChangeScene newSceneName:" + newSceneName + " networkSceneName:" + networkSceneName);

            // vis2k: pause message handling while loading scene. otherwise we will process messages and then lose all
            // the state as soon as the load is finishing, causing all kinds of bugs because of missing state.
            // (client may be null after StopClient etc.)
            if (logger.LogEnabled()) logger.Log("ClientChangeScene: pausing handlers while scene is loading to avoid data loss after scene was loaded.");
            Transport.activeTransport.Enabled = false;

            // Let client prepare for scene change
            OnClientChangeScene(newSceneName, sceneOperation, customHandling);

            // scene handling will happen in overrides of OnClientChangeScene and/or OnClientSceneChanged
            if (customHandling)
            {
                FinishLoadScene();
                return;
            }

            // cache sceneOperation so we know what was done in OnClientSceneChanged called from FinishLoadSceneClientOnly
            clientSceneOperation = sceneOperation;

            IEnumerable<Scene> scenes = Level.Scenes.Where(scene => scene.ID == newSceneName.ID);
            switch (sceneOperation)
            {
                case SceneOperation.Normal:
                    Level.ChangeSceneAsync(newSceneName);
                    break;
                case SceneOperation.LoadAdditive:
                    // Ensure additive scene is not already loaded on client by name or path
                    // since we don't know which was passed in the Scene message
                    if (scenes.Count() <= 0)
                        Level.LoadSceneAsync(newSceneName);
                    else
                    {
                        logger.LogWarning($"Scene {newSceneName} is already loaded");

                        // Re-enable the transport that we disabled before entering this switch
                        Transport.activeTransport.Enabled = true;
                    }
                    break;
                case SceneOperation.UnloadAdditive:
                    // Ensure additive scene is actually loaded on client by name or path
                    // since we don't know which was passed in the Scene message
                    if (scenes.Count() > 0)
                        Level.UnloadSceneAsync(scenes.FirstOrDefault());
                    else
                    {
                        logger.LogWarning($"Cannot unload {newSceneName} with UnloadAdditive operation");

                        // Re-enable the transport that we disabled before entering this switch
                        Transport.activeTransport.Enabled = true;
                    }
                    break;
            }

            // don't change the client's current networkSceneName when loading additive scene content
            if (sceneOperation == SceneOperation.Normal)
                networkSceneName = newSceneName;
        }

        // sUpport additive scene loads:
        //   NetworkScenePostProcess disables all scene objects on load, and
        //   * NetworkServer.SpawnObjects enables them again on the server when
        //     calling OnStartServer
        //   * ClientScene.PrepareToSpawnSceneObjects enables them again on the
        //     client after the server sends ObjectSpawnStartedMessage to client
        //     in SpawnObserversForConnection. this is only called when the
        //     client joins, so we need to rebuild scene objects manually again
        // TODO merge this with FinishLoadScene()?
        void OnSceneLoaded(Scene scene, Guid id)
        {
            if (NetworkServer.active)
            {
                // TODO only respawn the server objects from that scene later!
                NetworkServer.SpawnObjects();
                if (logger.LogEnabled()) logger.Log("Respawned Server objects after additive scene load: " + scene.Name);
            }
            if (NetworkClient.active)
            {
                ClientScene.PrepareToSpawnSceneObjects();
                if (logger.LogEnabled()) logger.Log("Rebuild Client spawnableObjects after additive scene load: " + scene.Name);
            }
            if (singleton != null)
            {
                if (logger.LogEnabled()) logger.Log("ClientChangeScene done readyCon:" + clientReadyConnection);
                singleton.FinishLoadScene();
            }
        }

        void FinishLoadScene()
        {
            // NOTE: this cannot use NetworkClient.allClients[0] - that client may be for a completely different purpose.

            // process queued messages that we received while loading the scene
            logger.Log("FinishLoadScene: resuming handlers after scene was loading.");
            Debug.Log("pretest" + Transport.activeTransport);
            Transport.activeTransport.Enabled = true;
            Debug.Log("test" + Transport.activeTransport);

            // host mode?
            if (mode == NetworkManagerMode.Host)
            {
                FinishLoadSceneHost();
            }
            // server-only mode?
            else if (mode == NetworkManagerMode.ServerOnly)
            {
                FinishLoadSceneServerOnly();
            }
            // client-only mode?
            else if (mode == NetworkManagerMode.ClientOnly)
            {
                FinishLoadSceneClientOnly();
            }
            // otherwise we called it after stopping when loading offline scene.
            // do nothing then.
        }

        // finish load scene part for host mode. makes code easier and is
        // necessary for FinishStartHost later.
        // (the 3 things have to happen in that exact order)
        void FinishLoadSceneHost()
        {
            // debug message is very important. if we ever break anything then
            // it's very obvious to notice.
            logger.Log("Finished loading scene in host mode.");

            if (clientReadyConnection != null)
            {
                OnClientConnect(clientReadyConnection);
                clientLoadedScene = true;
                clientReadyConnection = null;
            }

            // do we need to finish a StartHost() call?
            // then call FinishStartHost and let it take care of spawning etc.
            if (finishStartHostPending)
            {
                finishStartHostPending = false;
                FinishStartHost();

                // call OnServerSceneChanged
                OnServerSceneChanged(networkSceneName);

                // DO NOT call OnClientSceneChanged here.
                // the scene change happened because StartHost loaded the
                // server's online scene. it has nothing to do with the client.
                // this was not meant as a client scene load, so don't call it.
                //
                // otherwise AddPlayer would be called twice:
                // -> once for client OnConnected
                // -> once in OnClientSceneChanged
            }
            // otherwise we just changed a scene in host mode
            else
            {
                // spawn server objects
                NetworkServer.SpawnObjects();

                // call OnServerSceneChanged
                OnServerSceneChanged(networkSceneName);

                if (NetworkClient.isConnected)
                {
                    // let client know that we changed scene
                    OnClientSceneChanged(NetworkClient.connection);
                }
            }
        }

        // finish load scene part for server-only. . makes code easier and is
        // necessary for FinishStartServer later.
        void FinishLoadSceneServerOnly()
        {
            // debug message is very important. if we ever break anything then
            // it's very obvious to notice.
            logger.Log("Finished loading scene in server-only mode.");

            NetworkServer.SpawnObjects();
            OnServerSceneChanged(networkSceneName);
        }

        // finish load scene part for client-only. makes code easier and is
        // necessary for FinishStartClient later.
        void FinishLoadSceneClientOnly()
        {
            // debug message is very important. if we ever break anything then
            // it's very obvious to notice.
            logger.Log("Finished loading scene in client-only mode.");

            if (clientReadyConnection != null)
            {
                OnClientConnect(clientReadyConnection);
                clientLoadedScene = true;
                clientReadyConnection = null;
            }

            if (NetworkClient.isConnected)
            {
                OnClientSceneChanged(NetworkClient.connection);
            }
        }

        #endregion

        #region Start Positions

        public static int startPositionIndex;

        /// <summary>
        /// List of transforms populated by NetworkStartPosition components found in the scene.
        /// </summary>
        public static List<Transform> startPositions = new List<Transform>();

        /// <summary>
        /// Registers the transform of a game object as a player spawn location.
        /// <para>This is done automatically by NetworkStartPosition components, but can be done manually from user script code.</para>
        /// </summary>
        /// <param name="start">Transform to register.</param>
        public static void RegisterStartPosition(Transform start)
        {
            if (logger.LogEnabled()) logger.Log("RegisterStartPosition: (" + start + ") " + start.Translation);
            startPositions.Add(start);

            // reorder the list so that round-robin spawning uses the start Positions
            // in hierarchy order.  This assumes all objects with NetworkStartPosition
            // component are siblings, either in the scene root or together as children
            // under a single parent in the scene.
            //startPositions = startPositions.OrderBy(transform => transform.GetSiblingIndex()).ToList();
        }

        /// <summary>
        /// Unregisters the transform of a game object as a player spawn location.
        /// <para>This is done automatically by the <see cref="NetworkStartPosition">NetworkStartPosition</see> component, but can be done manually from user code.</para>
        /// </summary>
        /// <param name="start">Transform to unregister.</param>
        public static void UnRegisterStartPosition(Transform start)
        {
            if (logger.LogEnabled()) logger.Log("UnRegisterStartPosition: (" + start + ") " + start.Translation);
            startPositions.Remove(start);
        }

        /// <summary>
        /// This finds a spawn Position based on NetworkStartPosition objects in the scene.
        /// <para>This is used by the default implementation of OnServerAddPlayer.</para>
        /// </summary>
        /// <returns>Returns the transform to spawn a player at, or null.</returns>
        public Transform GetStartPosition()
        {
            // first remove any dead transforms
            startPositions.RemoveAll(t => t == null);

            if (startPositions.Count == 0)
            {
                throw new Exception("Missing start pos");                
            }

            if (playerSpawnMethod == PlayerSpawnMethod.Random)
            {
                Random random = new Random();

                return startPositions[random.Next(0, startPositions.Count)];
            }
            else
            {
                Transform startPosition = startPositions[startPositionIndex];
                startPositionIndex = (startPositionIndex + 1) % startPositions.Count;
                return startPosition;
            }
        }

        #endregion

        #region Server Internal Message Handlers

        void OnServerConnectInternal(NetworkConnection conn)
        {
            logger.Log("NetworkManager.OnServerConnectInternal");

            if (authenticator != null)
            {
                // we have an authenticator - let it handle authentication
                authenticator.OnServerAuthenticate(conn);
            }
            else
            {
                // authenticate immediately
                OnServerAuthenticated(conn);
            }
        }

        // called after successful authentication
        void OnServerAuthenticated(NetworkConnection conn)
        {
            logger.Log("NetworkManager.OnServerAuthenticated");

            // set connection to authenticated
            conn.isAuthenticated = true;

            // proceed with the login handshake by calling OnServerConnect
            if (networkSceneName.ID != Guid.Empty && networkSceneName.ID != offlineScene.ID)
            {
                Debug.Log("Sending change scene message to new scene " + networkSceneName.ID);
                SceneMessage msg = new SceneMessage() { sceneName = networkSceneName.ID };
                conn.Send(msg);
            }

            OnServerConnect(conn);
        }

        void OnServerDisconnectInternal(NetworkConnection conn)
        {
            logger.Log("NetworkManager.OnServerDisconnectInternal");
            OnServerDisconnect(conn);
        }

        void OnServerReadyMessageInternal(NetworkConnection conn, ReadyMessage msg)
        {
            logger.Log("NetworkManager.OnServerReadyMessageInternal");
            OnServerReady(conn);
        }

        void OnServerAddPlayerInternal(NetworkConnection conn, AddPlayerMessage msg)
        {
            logger.Log("NetworkManager.OnServerAddPlayer");

            if (autoCreatePlayer && playerPrefab == null)
            {
                logger.LogError("The PlayerPrefab is empty on the NetworkManager. Please setUp a PlayerPrefab object.");
                return;
            }

            if (autoCreatePlayer && playerPrefab.GetDefaultInstance().GetScript<NetworkIdentity>() == null)
            {
                logger.LogError("The PlayerPrefab does not have a NetworkIdentity. Please add a NetworkIdentity to the player prefab.");
                return;
            }

            if (conn.identity != null)
            {
                logger.LogError("There is already a player for this connection.");
                return;
            }

            OnServerAddPlayer(conn);
        }

        #endregion

        #region Client Internal Message Handlers

        void OnClientConnectInternal(NetworkConnection conn)
        {
            logger.Log("NetworkManager.OnClientConnectInternal");

            if (authenticator != null)
            {
                // we have an authenticator - let it handle authentication
                authenticator.OnClientAuthenticate(conn);
            }
            else
            {
                // authenticate immediately
                OnClientAuthenticated(conn);
            }
        }

        // called after successful authentication
        void OnClientAuthenticated(NetworkConnection conn)
        {
            logger.Log("NetworkManager.OnClientAuthenticated");

            // set connection to authenticated
            conn.isAuthenticated = true;

            // proceed with the login handshake by calling OnClientConnect
            if (onlineScene.ID == null || onlineScene.ID == offlineScene.ID || IsSceneActive(onlineScene))
            {
                clientLoadedScene = false;
                OnClientConnect(conn);
            }
            else
            {
                // will wait for scene id to come from the server.
                clientLoadedScene = true;
                clientReadyConnection = conn;
            }
        }

        void OnClientDisconnectInternal(NetworkConnection conn)
        {
            logger.Log("NetworkManager.OnClientDisconnectInternal");
            OnClientDisconnect(conn);
        }

        void OnClientNotReadyMessageInternal(NetworkConnection conn, NotReadyMessage msg)
        {
            logger.Log("NetworkManager.OnClientNotReadyMessageInternal");

            ClientScene.ready = false;
            OnClientNotReady(conn);

            // NOTE: clientReadyConnection is not set here! don't want OnClientConnect to be invoked again after scene changes.
        }

        void OnClientSceneInternal(NetworkConnection conn, SceneMessage msg)
        {
            logger.Log("NetworkManager.OnClientSceneInternal");

            if (NetworkClient.isConnected && !NetworkServer.active)
            {
                ClientChangeScene(new SceneReference(msg.sceneName), msg.sceneOperation, msg.customHandling);
            }
        }

        #endregion

        #region Server System Callbacks

        /// <summary>
        /// Called on the server when a new client connects.
        /// <para>Unity calls this on the Server when a Client connects to the Server. Use an override to tell the NetworkManager what to do when a client connects to the server.</para>
        /// </summary>
        /// <param name="conn">Connection from client.</param>
        public virtual void OnServerConnect(NetworkConnection conn) { }

        /// <summary>
        /// Called on the server when a client disconnects.
        /// <para>This is called on the Server when a Client disconnects from the Server. Use an override to decide what should happen when a disconnection is detected.</para>
        /// </summary>
        /// <param name="conn">Connection from client.</param>
        public virtual void OnServerDisconnect(NetworkConnection conn)
        {
            NetworkServer.DestroyPlayerForConnection(conn);
            logger.Log("OnServerDisconnect: Client disconnected.");
        }

        /// <summary>
        /// Called on the server when a client is ready.
        /// <para>The default implementation of this function calls NetworkServer.SetClientReady() to continue the network setUp process.</para>
        /// </summary>
        /// <param name="conn">Connection from client.</param>
        public virtual void OnServerReady(NetworkConnection conn)
        {
            if (conn.identity == null)
            {
                // this is now allowed (was not for a while)
                logger.Log("Ready with no player object");
            }
            NetworkServer.SetClientReady(conn);
        }

        /// <summary>
        /// Called on the server when a client adds a new player with ClientScene.AddPlayer.
        /// <para>The default implementation for this function creates a new player object from the playerPrefab.</para>
        /// </summary>
        /// <param name="conn">Connection from client.</param>
        public virtual void OnServerAddPlayer(NetworkConnection conn)
        {
            Transform startPos = GetStartPosition();
            Actor player = startPos != null
                ? PrefabManager.SpawnPrefab(playerPrefab, startPos.Translation, startPos.Orientation)
                : PrefabManager.SpawnPrefab(playerPrefab);

            NetworkServer.AddPlayerForConnection(conn, player);
        }

        /// <summary>
        /// Called from ServerChangeScene immediately before SceneManager.LoadSceneAsync is executed
        /// <para>This allows server to do work / cleanUp / prep before the scene changes.</para>
        /// </summary>
        /// <param name="newSceneName">Name of the scene that's about to be loaded</param>
        public virtual void OnServerChangeScene(SceneReference newSceneName) { }

        /// <summary>
        /// Called on the server when a scene is completed loaded, when the scene load was initiated by the server with ServerChangeScene().
        /// </summary>
        /// <param name="sceneName">The name of the new scene.</param>
        public virtual void OnServerSceneChanged(SceneReference sceneName) { }

        #endregion

        #region Client System Callbacks

        /// <summary>
        /// Called on the client when connected to a server.
        /// <para>The default implementation of this function sets the client as ready and adds a player. Override the function to dictate what happens when the client connects.</para>
        /// </summary>
        /// <param name="conn">Connection to the server.</param>
        public virtual void OnClientConnect(NetworkConnection conn)
        {
            // OnClientConnect by default calls AddPlayer but it should not do
            // that when we have online/offline scenes. so we need the
            // clientLoadedScene flag to prevent it.
            if (!clientLoadedScene)
            {
                // Ready/AddPlayer is usually triggered by a scene load completing. if no scene was loaded, then Ready/AddPlayer it here instead.
                if (!ClientScene.ready) ClientScene.Ready(conn);
                if (autoCreatePlayer)
                {
                    ClientScene.AddPlayer(conn);
                }
            }
        }

        /// <summary>
        /// Called on clients when disconnected from a server.
        /// <para>This is called on the client when it disconnects from the server. Override this function to decide what happens when the client disconnects.</para>
        /// </summary>
        /// <param name="conn">Connection to the server.</param>
        public virtual void OnClientDisconnect(NetworkConnection conn)
        {
            StopClient();
        }

        /// <summary>
        /// Called on clients when a servers tells the client it is no longer ready.
        /// <para>This is commonly used when switching scenes.</para>
        /// </summary>
        /// <param name="conn">Connection to the server.</param>
        public virtual void OnClientNotReady(NetworkConnection conn) { }

        /// <summary>
        /// Called from ClientChangeScene immediately before SceneManager.LoadSceneAsync is executed
        /// <para>This allows client to do work / cleanUp / prep before the scene changes.</para>
        /// </summary>
        /// <param name="newSceneName">Name of the scene that's about to be loaded</param>
        /// <param name="sceneOperation">Scene operation that's about to happen</param>
        /// <param name="customHandling">true to indicate that scene loading will be handled through overrides</param>
        public virtual void OnClientChangeScene(SceneReference newSceneName, SceneOperation sceneOperation, bool customHandling) { }

        /// <summary>
        /// Called on clients when a scene has completed loaded, when the scene load was initiated by the server.
        /// <para>Scene changes can cause player objects to be destroyed. The default implementation of OnClientSceneChanged in the NetworkManager is to add a player object for the connection if no player object exists.</para>
        /// </summary>
        /// <param name="conn">The network connection that the scene change message arrived on.</param>
        public virtual void OnClientSceneChanged(NetworkConnection conn)
        {
            // always become ready.
            if (!ClientScene.ready) ClientScene.Ready(conn);

            // Only call AddPlayer for normal scene changes, not additive load/unload
            if (clientSceneOperation == SceneOperation.Normal && autoCreatePlayer && ClientScene.localPlayer == null)
            {
                // add player if existing one is null
                ClientScene.AddPlayer(conn);
            }
        }

        #endregion

        #region Start & Stop callbacks

        // Since there are multiple versions of StartServer, StartClient and StartHost, to reliably customize
        // their functionality, users would need override all the versions. Instead these callbacks are invoked
        // from all versions, so users only need to implement this one case.

        /// <summary>
        /// This is invoked when a host is started.
        /// <para>StartHost has multiple signatures, but they all cause this hook to be called.</para>
        /// </summary>
        public virtual void OnStartHost() { }

        /// <summary>
        /// This is invoked when a server is started - including when a host is started.
        /// <para>StartServer has multiple signatures, but they all cause this hook to be called.</para>
        /// </summary>
        public virtual void OnStartServer() { }

        /// <summary>
        /// This is invoked when the client is started.
        /// </summary>
        public virtual void OnStartClient() { }

        /// <summary>
        /// This is called when a server is stopped - including when a host is stopped.
        /// </summary>
        public virtual void OnStopServer() { }

        /// <summary>
        /// This is called when a client is stopped.
        /// </summary>
        public virtual void OnStopClient() { }

        /// <summary>
        /// This is called when a host is stopped.
        /// </summary>
        public virtual void OnStopHost() { }

        #endregion
    }
}
