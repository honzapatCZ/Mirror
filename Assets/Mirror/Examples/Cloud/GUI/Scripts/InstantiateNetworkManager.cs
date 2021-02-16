using FlaxEngine;

namespace Mirror.Cloud.Examples
{
    /// <summary>
    /// Instantiate a new NetworkManager if one does not already exist
    /// </summary>
    public class InstantiateNetworkManager : Script
    {
        public GameObject prefab;

        private void Awake()
        {
            if (NetworkManager.singleton == null)
            {
                Instantiate(prefab);
            }
        }
    }
}
