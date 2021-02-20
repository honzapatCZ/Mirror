// vis2k: GUILayout instead of spacey += ...; removed Update hotkeys to avoid
// confusion if someone accidentally presses one.
using FlaxEngine;
using FlaxEngine.GUI;

namespace Mirror
{
    /// <summary>
    /// An extension for the NetworkManager that displays a default HUD for controlling the network state of the game.
    /// <para>This component also shows useful internal state for the networking system in the inspector window of the editor. It allows users to view connections, networked objects, message handlers, and packet statistics. This information can be helpful when debugging networked games.</para>
    /// </summary>
    //[DisallowMultipleComponent]
    //[AddComponentMenu("Network/NetworkManagerHUD")]
    //[RequireComponent(typeof(NetworkManager))]
    //[HelpURL("https://mirror-networking.com/docs/Articles/Components/NetworkManagerHUD.html")]
    public class NetworkManagerHUD : Script
    {
        NetworkManager manager => NetworkManager.singleton;

        public Vector2 offset;

        public UICanvas uiRoot;
        public FontReference font;

        private VerticalPanel mainVertPanel;
        
        private Button hostBtn;
        private HorizontalPanel clientPanel;             
        private Button clientBtn;
        private TextBox clientAddress;
        private Button serverBtn;
        private Label connecting;
        private Button cancelConnect;

        private Label transport;
        private Label address;

        private Button cancelHostBtn;
        private Button cancelClientBtn;
        private Button cancelServerBtn;

        private Button clientReadyBtn;
        public override void OnEnable()
        {
            mainVertPanel = new VerticalPanel {
                AnchorPreset = AnchorPresets.VerticalStretchLeft,
                Width = 250,
                TopMargin = offset.Y,
                LeftMargin = offset.X,
                BottomMargin = 0,
                Parent = uiRoot.GUI,
                
            };

            hostBtn = new Button
            {
                Parent = mainVertPanel,
                Text = "Host a server",
                Visible = false,
                Font = font,
            };
            clientPanel = new HorizontalPanel {
                Parent = mainVertPanel,
                Height = 30,
                Visible = false,
            };
            clientBtn = new Button
            {
                Parent = clientPanel,
                Text = "Connect as client",
                Font = font,
            };
            clientAddress = new TextBox
            {
                Parent = clientPanel,
                Font = font,
            };
            serverBtn = new Button
            {
                Parent = mainVertPanel,
                Text = "Start Server",
                Visible = false,
                Font = font,
            };
            connecting = new Label
            {
                Parent = mainVertPanel,
                Text = "Connecting to server",
                Visible = false,
                Font = font,
            };
            cancelConnect = new Button
            {
                Parent = mainVertPanel,
                Text = "Cancel connection attempt",
                Visible = false,
                Font = font,
            };

            transport = new Label
            {
                Parent = mainVertPanel,
                Text = "Transport is",
                Visible = false,
                Font = font,
                AutoHeight = true,
                Wrapping = TextWrapping.WrapWords,
            };
            address = new Label
            {
                Parent = mainVertPanel,
                Text = "address is",
                Visible = false,
                Font = font,
            };

            clientReadyBtn = new Button
            {
                Parent = mainVertPanel,
                Text = "Client Ready",
                Visible = false,
                Font = font,
            };

            cancelHostBtn = new Button
            {
                Parent = mainVertPanel,
                Text = "Stop hosting",
                Visible = false,
                Font = font,
            };
            cancelClientBtn = new Button
            {
                Parent = mainVertPanel,
                Text = "Disconnect",
                Visible = false,
            };
            cancelServerBtn = new Button
            {
                Parent = mainVertPanel,
                Text = "Stop server",
                Visible = false,
                Font = font,
            };

            hostBtn.Clicked += () =>
            {
                manager.StartHost();
            };
            clientBtn.Clicked += () =>
            {
                manager.StartClient();
            };
            clientAddress.TextChanged += () =>
            {
                manager.networkAddress = clientAddress.Text;
            };
            serverBtn.Clicked += () =>
            {
                manager.StartServer();
            };
            cancelConnect.Clicked += () =>
            {
                manager.StopClient();
            };

            clientReadyBtn.Clicked += () =>
            {
                ClientScene.Ready(NetworkClient.connection);

                if (ClientScene.localPlayer == null)
                {
                    ClientScene.AddPlayer(NetworkClient.connection);
                }
            };

            cancelHostBtn.Clicked += () =>
            {
                manager.StopHost();
            };
            cancelClientBtn.Clicked += () =>
            {
                manager.StopClient();
            };
            cancelServerBtn.Clicked += () =>
            {
                manager.StopServer();
            };
        }

        public override void OnDisable()
        {
            mainVertPanel.Dispose();
            mainVertPanel = null;
            clientReadyBtn = null;
        }

        public override void OnUpdate()
        {
            OnGUI();
        }

        void OnGUI()
        {
            //GUILayout.BeginArea(new Rect(10 + offsetX, 40 + offsetY, 215, 9999));
            /*
            if (!NetworkClient.isConnected && !NetworkServer.active)
            {
                StartButtons(!NetworkClient.isConnected && !NetworkServer.active);
            }
            else
            {
                StatusLabels(!(!NetworkClient.isConnected && !NetworkServer.active));
            }
            */
            StartButtons(!NetworkClient.isConnected && !NetworkServer.active);
            StatusLabels(!(!NetworkClient.isConnected && !NetworkServer.active));
            clientReadyBtn.Visible = NetworkClient.isConnected && !ClientScene.ready;
            // client ready
            /*
            if (NetworkClient.isConnected && !ClientScene.ready)
            {
                if (GUILayout.Button("Client Ready"))
                {
                    ClientScene.Ready(NetworkClient.connection);

                    if (ClientScene.localPlayer == null)
                    {
                        ClientScene.AddPlayer(NetworkClient.connection);
                    }
                }
            }
            */

            StopButtons();

            //GUILayout.EndArea();
        }

        void StartButtons(bool parent)
        {
            hostBtn.Visible = parent && !NetworkClient.active;
            clientPanel.Visible = parent && !NetworkClient.active;
            serverBtn.Visible = parent && !NetworkClient.active;
            /*
            if (!NetworkClient.active)
            {
                // Server + Client
                if (GUILayout.Button("Host (Server + Client)"))
                {
                    manager.StartHost();
                }

                // Client + IP
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Client"))
                {
                    manager.StartClient();
                }
                manager.networkAddress = GUILayout.TextField(manager.networkAddress);
                GUILayout.EndHorizontal();

                // Server Only
                if (GUILayout.Button("Server Only")) manager.StartServer();
            }
            else
            {
                // Connecting
                GUILayout.Label("Connecting to " + manager.networkAddress + "..");
                if (GUILayout.Button("Cancel Connection Attempt"))
                {
                    manager.StopClient();
                }
            }
            */
            connecting.Visible = parent && NetworkClient.active;
            connecting.Text = "Connecting to " + manager.networkAddress + "..";
            cancelConnect.Visible = parent && NetworkClient.active;
        }

        void StatusLabels(bool parent)
        {
            // server / client status message
            transport.Visible = parent && NetworkServer.active;
            transport.Text = "Server: active. Transport: " + Transport.activeTransport;

            address.Visible = parent && NetworkClient.isConnected;
            address.Text = "Client: address=" + manager.networkAddress;
            /*
            if (NetworkServer.active)
            {
                GUILayout.Label("Server: active. Transport: " + Transport.activeTransport);
            }
            if (NetworkClient.isConnected)
            {
                GUILayout.Label("Client: address=" + manager.networkAddress);
            }*/
        }

        void StopButtons()
        {
            cancelHostBtn.Visible = NetworkServer.active && NetworkClient.isConnected;
            cancelClientBtn.Visible = !NetworkServer.active && NetworkClient.isConnected;
            cancelServerBtn.Visible = NetworkServer.active && !NetworkClient.isConnected;
            // stop host if host mode
            /*
            if (NetworkServer.active && NetworkClient.isConnected)
            {
                if (GUILayout.Button("Stop Host"))
                {
                    manager.StopHost();
                }
            }
            // stop client if client-only
            else if (NetworkClient.isConnected)
            {
                if (GUILayout.Button("Stop Client"))
                {
                    manager.StopClient();
                }
            }
            // stop server if server-only
            else if (NetworkServer.active)
            {
                if (GUILayout.Button("Stop Server"))
                {
                    manager.StopServer();
                }
            }*/
        }
    }
}
