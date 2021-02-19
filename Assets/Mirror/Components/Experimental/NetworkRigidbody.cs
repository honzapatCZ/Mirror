using FlaxEngine;

namespace Mirror.Experimental
{
    //[AddComponentMenu("Network/Experimental/NetworkRigidbody")]
    //[HelpURL("https://mirror-networking.com/docs/Articles/Components/NetworkRigidbody.html")]
    public class NetworkRigidbody : NetworkBehaviour
    {
        static readonly ILogger logger = LogFactory.GetLogger(typeof(NetworkRigidbody));

        [Header("Settings")]
        [Serialize] internal RigidBody target = null;

        [Tooltip("Set to true if moves come from owner client, set to false if moves always come from server")]
        [Serialize] bool clientAuthority = false;

        [Header("Velocity")]

        [Tooltip("Syncs Velocity every SyncInterval")]
        [Serialize] bool syncVelocity = true;

        [Tooltip("Set velocity to 0 each frame (only works if syncVelocity is false")]
        [Serialize] bool clearVelocity = false;

        [Tooltip("Only Syncs Value if distance between previous and current is great than sensitivity")]
        [Serialize] float velocitySensitivity = 0.1f;


        [Header("Angular Velocity")]

        [Tooltip("Syncs AngularVelocity every SyncInterval")]
        [Serialize] bool syncAngularVelocity = true;

        [Tooltip("Set AngularVelocity to 0 each frame (only works if syncAngularVelocity is false")]
        [Serialize] bool clearAngularVelocity = false;

        [Tooltip("Only Syncs Value if distance between previous and current is great than sensitivity")]
        [Serialize] float AngularVelocitySensitivity = 0.1f;

        /// <summary>
        /// Values sent on client with authority after they are sent to the server
        /// </summary>
        readonly ClientSyncState previousValue = new ClientSyncState();

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


        #region Sync vars
        [SyncVar(hook = nameof(OnVelocityChanged))]
        Vector3 LinearVelocity;

        [SyncVar(hook = nameof(OnAngularVelocityChanged))]
        Vector3 AngularVelocity;

        [SyncVar(hook = nameof(OnIsKinematicChanged))]
        bool IsKinematic;

        [SyncVar(hook = nameof(OnUseGravityChanged))]
        bool EnableGravity;

        [SyncVar(hook = nameof(OnuDragChanged))]
        float drag;

        [SyncVar(hook = nameof(OnAngularDragChanged))]
        float angularDrag;

        /// <summary>
        /// Ignore value if is host or client with Authority
        /// </summary>
        /// <returns></returns>
        bool IgnoreSync => isServer || ClientWithAuthority;

        bool ClientWithAuthority => clientAuthority && hasAuthority;

        void OnVelocityChanged(Vector3 _, Vector3 newValue)
        {
            if (IgnoreSync)
                return;

            target.LinearVelocity = newValue;
        }


        void OnAngularVelocityChanged(Vector3 _, Vector3 newValue)
        {
            if (IgnoreSync)
                return;

            target.AngularVelocity = newValue;
        }

        void OnIsKinematicChanged(bool _, bool newValue)
        {
            if (IgnoreSync)
                return;

            target.IsKinematic = newValue;
        }

        void OnUseGravityChanged(bool _, bool newValue)
        {
            if (IgnoreSync)
                return;

            target.EnableGravity = newValue;
        }

        void OnuDragChanged(float _, float newValue)
        {
            if (IgnoreSync)
                return;

            target.LinearDamping = newValue;
        }

        void OnAngularDragChanged(float _, float newValue)
        {
            if (IgnoreSync)
                return;

            target.AngularDamping = newValue;
        }
        #endregion


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

        public override void OnFixedUpdate()
        {
            base.OnFixedUpdate();
            if (clearAngularVelocity && !syncAngularVelocity)
            {
                target.AngularVelocity = Vector3.Zero;
            }

            if (clearVelocity && !syncVelocity)
            {
                target.LinearVelocity = Vector3.Zero;
            }
        }

        /// <summary>
        /// Updates sync var values on server so that they sync to the client
        /// </summary>
        [Server]
        void SyncToClients()
        {
            // only Update if they have changed more than Sensitivity

            Vector3 currentVelocity = syncVelocity ? target.LinearVelocity : default;
            Vector3 currentAngularVelocity = syncAngularVelocity ? target.AngularVelocity : default;

            bool velocityChanged = syncVelocity && ((previousValue.LinearVelocity - currentVelocity).LengthSquared > velocitySensitivity * velocitySensitivity);
            bool AngularVelocityChanged = syncAngularVelocity && ((previousValue.AngularVelocity - currentAngularVelocity).LengthSquared > AngularVelocitySensitivity * AngularVelocitySensitivity);

            if (velocityChanged)
            {
                LinearVelocity = currentVelocity;
                previousValue.LinearVelocity = currentVelocity;
            }

            if (AngularVelocityChanged)
            {
                AngularVelocity = currentAngularVelocity;
                previousValue.AngularVelocity = currentAngularVelocity;
            }

            // other rigidbody settings
            IsKinematic = target.IsKinematic;
            EnableGravity = target.EnableGravity;
            drag = target.LinearDamping;
            angularDrag = target.AngularDamping;
        }

        /// <summary>
        /// Uses Command to send values to server
        /// </summary>
        [Client]
        void SendToServer()
        {
            if (!hasAuthority)
            {
                logger.LogWarning("SendToServer called without authority");
                return;
            }

            SendVelocity();
            SendRigidBodySettings();
        }

        [Client]
        void SendVelocity()
        {
            float now = Time.GameTime;
            if (now < previousValue.nextSyncTime)
                return;

            Vector3 currentVelocity = syncVelocity ? target.LinearVelocity : default;
            Vector3 currentAngularVelocity = syncAngularVelocity ? target.AngularVelocity : default;

            bool velocityChanged = syncVelocity && ((previousValue.LinearVelocity - currentVelocity).LengthSquared > velocitySensitivity * velocitySensitivity);
            bool AngularVelocityChanged = syncAngularVelocity && ((previousValue.AngularVelocity - currentAngularVelocity).LengthSquared > AngularVelocitySensitivity * AngularVelocitySensitivity);

            // if AngularVelocity has changed it is likely that velocity has also changed so just sync both values
            // however if only velocity has changed just send velocity
            if (AngularVelocityChanged)
            {
                CmdSendVelocityAndAngular(currentVelocity, currentAngularVelocity);
                previousValue.LinearVelocity = currentVelocity;
                previousValue.AngularVelocity = currentAngularVelocity;
            }
            else if (velocityChanged)
            {
                CmdSendVelocity(currentVelocity);
                previousValue.LinearVelocity = currentVelocity;
            }


            // only Update syncTime if either has changed
            if (AngularVelocityChanged || velocityChanged)
            {
                previousValue.nextSyncTime = now + syncInterval;
            }
        }

        [Client]
        void SendRigidBodySettings()
        {
            // These shouldn't change often so it is ok to send in their own Command
            if (previousValue.IsKinematic != target.IsKinematic)
            {
                CmdSendIsKinematic(target.IsKinematic);
                previousValue.IsKinematic = target.IsKinematic;
            }
            if (previousValue.EnableGravity != target.EnableGravity)
            {
                CmdSendUseGravity(target.EnableGravity);
                previousValue.EnableGravity = target.EnableGravity;
            }
            if (previousValue.drag != target.LinearDamping)
            {
                CmdSendDrag(target.LinearDamping);
                previousValue.drag = target.LinearDamping;
            }
            if (previousValue.angularDrag != target.AngularDamping)
            {
                CmdSendAngularDrag(target.AngularDamping);
                previousValue.angularDrag = target.AngularDamping;
            }
        }

        /// <summary>
        /// Called when only Velocity has changed on the client
        /// </summary>
        [Command]
        void CmdSendVelocity(Vector3 velocity)
        {
            // Ignore messages from client if not in client authority mode
            if (!clientAuthority)
                return;

            this.LinearVelocity = velocity;
            target.LinearVelocity = velocity;
        }

        /// <summary>
        /// Called when AngularVelocity has changed on the client
        /// </summary>
        [Command]
        void CmdSendVelocityAndAngular(Vector3 velocity, Vector3 AngularVelocity)
        {
            // Ignore messages from client if not in client authority mode
            if (!clientAuthority)
                return;

            if (syncVelocity)
            {
                this.LinearVelocity = velocity;

                target.LinearVelocity = velocity;

            }
            this.AngularVelocity = AngularVelocity;
            target.AngularVelocity = AngularVelocity;
        }

        [Command]
        void CmdSendIsKinematic(bool IsKinematic)
        {
            // Ignore messages from client if not in client authority mode
            if (!clientAuthority)
                return;

            this.IsKinematic = IsKinematic;
            target.IsKinematic = IsKinematic;
        }

        [Command]
        void CmdSendUseGravity(bool EnableGravity)
        {
            // Ignore messages from client if not in client authority mode
            if (!clientAuthority)
                return;

            this.EnableGravity = EnableGravity;
            target.EnableGravity = EnableGravity;
        }

        [Command]
        void CmdSendDrag(float drag)
        {
            // Ignore messages from client if not in client authority mode
            if (!clientAuthority)
                return;

            this.drag = drag;
            target.LinearDamping = drag;
        }

        [Command]
        void CmdSendAngularDrag(float angularDrag)
        {
            // Ignore messages from client if not in client authority mode
            if (!clientAuthority)
                return;

            this.angularDrag = angularDrag;
            target.AngularDamping = angularDrag;
        }

        /// <summary>
        /// holds previously synced values
        /// </summary>
        public class ClientSyncState
        {
            /// <summary>
            /// Next sync time that velocity will be synced, based on syncInterval.
            /// </summary>
            public float nextSyncTime;
            public Vector3 LinearVelocity;
            public Vector3 AngularVelocity;
            public bool IsKinematic;
            public bool EnableGravity;
            public float drag;
            public float angularDrag;
        }
    }
}
