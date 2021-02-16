using FlaxEngine;
using UnityEngine.UI;

namespace Mirror.Examples.MultipleMatch
{
    public class PlayerGUI : Script
    {
        public Text playerName;

        public void SetPlayerInfo(PlayerInfo info)
        {
            playerName.text = "Player " + info.playerIndex;
            playerName.color = info.ready ? Color.green : Color.red;
        }
    }
}