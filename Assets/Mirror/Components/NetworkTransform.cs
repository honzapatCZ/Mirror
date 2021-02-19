using FlaxEngine;

namespace Mirror
{
    //[DisallowMultipleComponent]
    //[AddComponentMenu("Network/NetworkTransform")]
    //[HelpURL("https://mirror-networking.com/docs/Articles/Components/NetworkTransform.html")]
    public class NetworkTransform : NetworkTransformBase
    {        
        protected override Actor targetComponent => Actor;
    }
}
