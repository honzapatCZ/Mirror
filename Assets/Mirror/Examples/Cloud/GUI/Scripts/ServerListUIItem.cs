using System;
using Mirror.Cloud.ListServerService;
using FlaxEngine;
using UnityEngine.UI;

namespace Mirror.Cloud.Example
{
    /// <summary>
    /// Displays a server created by ServerListUI
    /// </summary>
    public class ServerListUIItem : Script
    {
        [Serialize] Text nameText = null;
        [Serialize] Text namePlayers = null;
        [Serialize] string playersFormat = "{0} / {1}";
        [Serialize] Text addressText = null;

        [Serialize] Button joinButton = null;

        ServerJson server;

        public void Setup(ServerJson server)
        {
            this.server = server;
            nameText.text = server.displayName;
            namePlayers.text = string.Format(playersFormat, server.playerCount, server.maxPlayerCount);
            addressText.text = server.address;

            joinButton.onClick.AddListener(OnJoinClicked);
        }

        void OnJoinClicked()
        {
            NetworkManager.singleton.StartClient(new Uri(server.address));
        }
    }
}
