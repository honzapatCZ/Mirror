using FlaxEngine;

namespace Mirror.Experimental
{
    //[AddComponentMenu("Network/Experimental/NetworkLerpRigidbody")]
    //[HelpURL("https://mirror-networking.com/docs/Articles/Components/NetworkLerpRigidbody.html")]
    public class NetworkLerpRigidbody : NetworkBehaviour
    {
        [Header("Settings")]
        [Serialize] internal RigidBody target = null;
        [Tooltip("How quickly current LinearVelocity approaches target LinearVelocity")]
        [Serialize] float lerpLinearVelocityAmount = 0.5f;
        [Tooltip("How quickly current Position approaches target Position")]
        [Serialize] float lerpPositionAmount = 0.5f;

        [Tooltip("Set to true if moves come from owner client, set to false if moves always come from server")]
        [Serialize] bool clientAuthority = false;

        float nextSyncTime;


        [SyncVar()]
        Vector3 targetLinearVelocity;

        [SyncVar()]
        Vector3 targetPosition;

        /// <summary>
        /// Ignore value if is host or client with Authority
        /// </summary>
        /// <returns></returns>
        bool IgnoreSync => isServer || ClientWithAuthority;

        bool ClientWithAuthority => clientAuthority && hasAuthority;

        void OnValidate()
        {
            if (target == null)
            {
                target = Actor as RigidBody;
            }
        }

        public override void OnAwake()
        {
            base.OnAwake();
            OnValidate();
        }

        public override void OnUpdate()
        {
            base.OnUpdate();
            if (isServer)
            {
                SyncToClients();
            }
            else if (ClientWithAuthority)
            {
                SendToServer();
            }
        }

        private void SyncToClients()
        {
            targetLinearVelocity = target.LinearVelocity;
            targetPosition = target.Position;
        }

        private void SendToServer()
        {
            float now = Time.GameTime;
            if (now > nextSyncTime)
            {
                nextSyncTime = now + syncInterval;
                CmdSendState(target.LinearVelocity, target.Position);
            }
        }

        [Command]
        private void CmdSendState(Vector3 LinearVelocity, Vector3 Position)
        {
            target.LinearVelocity = LinearVelocity;
            target.Position = Position;
            targetLinearVelocity = LinearVelocity;
            targetPosition = Position;
        }

        public override void OnFixedUpdate()
        {
            base.OnFixedUpdate();
            if (IgnoreSync) { return; }

            target.LinearVelocity = Vector3.Lerp(target.LinearVelocity, targetLinearVelocity, lerpLinearVelocityAmount);
            target.Position = Vector3.Lerp(target.Position, targetPosition, lerpPositionAmount);
            // add LinearVelocity to Position as Position would have moved on server at that LinearVelocity
            targetPosition += target.LinearVelocity * Time.UnscaledDeltaTime;

            // TODO does this also need to sync acceleration so and Update LinearVelocity?
        }
    }
}
