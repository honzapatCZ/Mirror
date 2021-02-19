using FlaxEngine;

namespace Mirror
{
    /// <summary>
    /// This component is used to make a gameObject a starting Position for spawning player objects in multiplayer games.
    /// <para>This object's transform will be automatically registered and unregistered with the NetworkManager as a starting Position.</para>
    /// </summary>
    //[DisallowMultipleComponent]
    //[AddComponentMenu("Network/NetworkStartPosition")]
    //[HelpURL("https://mirror-networking.com/docs/Articles/Components/NetworkStartPosition.html")]
    public class NetworkStartPosition : Script
    {
        public override void OnAwake()
        {
            base.OnAwake();
            NetworkManager.RegisterStartPosition(Transform);
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            NetworkManager.UnRegisterStartPosition(Transform);
        }
    }
}
