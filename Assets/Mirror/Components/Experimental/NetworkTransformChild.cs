using FlaxEngine;

namespace Mirror.Experimental
{
    /// <summary>
    /// A component to synchronize the Position of child transforms of networked objects.
    /// <para>There must be a NetworkTransform on the root object of the hierarchy. There can be multiple NetworkTransformChild components on an object. This does not use physics for synchronization, it simply synchronizes the LocalPosition and LocalOrientation of the child transform and lerps towards the received values.</para>
    /// </summary>
    //[AddComponentMenu("Network/Experimental/NetworkTransformChildExperimentalExperimental")]
    //[HelpURL("https://mirror-networking.com/docs/Articles/Components/NetworkTransformChild.html")]
    public class NetworkTransformChild : NetworkTransformBase
    {
        [Header("Target")]
        public Actor target;

        protected override Actor targetTransform => target;
    }
}
