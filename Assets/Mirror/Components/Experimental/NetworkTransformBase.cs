// vis2k:
// base class for NetworkTransform and NetworkTransformChild.
// New method is simple and stUpid. No more 1500 lines of code.
//
// Server sends current data.
// Client saves it and interpolates last and latest data points.
//   Update handles transform movement / Orientation
//   FixedUpdate handles rigidbody movement / Orientation
//
// Notes:
// * Built-in Teleport detection in case of lags / teleport / obstacles
// * Quaternion > EulerAngles because gimbal lock and Quaternion.Slerp
// * Syncs XYZ. Works 3D and 2D. Saving 4 bytes isn't worth 1000 lines of code.
// * Initial delay might happen if server sends packet immediately after moving
//   just 1cm, hence we move 1cm and then wait 100ms for next packet
// * Only way for smooth movement is to use a fixed movement speed during
//   interpolation. interpolation over time is never that good.
//
using System;
using FlaxEngine;

namespace Mirror.Experimental
{
    public abstract class NetworkTransformBase : NetworkBehaviour
    {
        // target transform to sync. can be on a child.
        protected abstract Actor targetTransform { get; }

        [Header("Authority")]

        [Tooltip("Set to true if moves come from owner client, set to false if moves always come from server")]
        [SyncVar]
        public bool clientAuthority;

        [Tooltip("Set to true if Updates from server should be ignored by owner")]
        [SyncVar]
        public bool excludeOwnerUpdate = true;

        [Header("Synchronization")]

        [Tooltip("Set to true if Position should be synchronized")]
        [SyncVar]
        public bool syncPosition = true;

        [Tooltip("Set to true if Orientation should be synchronized")]
        [SyncVar]
        public bool syncOrientation = true;

        [Tooltip("Set to true if scale should be synchronized")]
        [SyncVar]
        public bool syncScale = true;

        [Header("Interpolation")]

        [Tooltip("Set to true if Position should be interpolated")]
        [SyncVar]
        public bool interpolatePosition = true;

        [Tooltip("Set to true if Orientation should be interpolated")]
        [SyncVar]
        public bool interpolateOrientation = true;

        [Tooltip("Set to true if scale should be interpolated")]
        [SyncVar]
        public bool interpolateScale = true;

        // Sensitivity is added for VR where human players tend to have micro movements so this can quiet down
        // the network traffic.  Additionally, rigidbody drift should send less traffic, e.g very slow sliding / rolling.
        [Header("Sensitivity")]

        [Tooltip("Changes to the transform must exceed these values to be transmitted on the network.")]
        [SyncVar]
        public float LocalPositionSensitivity = .01f;

        [Tooltip("If Orientation exceeds this angle, it will be transmitted on the network")]
        [SyncVar]
        public float LocalOrientationSensitivity = .01f;

        [Tooltip("Changes to the transform must exceed these values to be transmitted on the network.")]
        [SyncVar]
        public float LocalScaleSensitivity = .01f;

        [Header("Diagnostics")]

        // server
        public Vector3 lastPosition;
        public Quaternion lastOrientation;
        public Vector3 lastScale;

        // client
        // use local Position/Orientation for VR sUpport
        [Serializable]
        public struct DataPoint
        {
            public float timeStamp;
            public Vector3 LocalPosition;
            public Quaternion LocalOrientation;
            public Vector3 LocalScale;
            public float movementSpeed;

            public bool isValid => timeStamp != 0;
        }

        // Is this a client with authority over this transform?
        // This component could be on the player object or any object that has been assigned authority to this client.
        bool IsOwnerWithClientAuthority => hasAuthority && clientAuthority;

        // interpolation start and goal
        public DataPoint start = new DataPoint();
        public DataPoint goal = new DataPoint();

        // We need to store this locally on the server so clients can't request Authority when ever they like
        bool clientAuthorityBeforeTeleport;

        public override void OnFixedUpdate()
        {
            base.OnFixedUpdate();
            // if server then always sync to others.
            // let the clients know that this has moved
            if (isServer && HasEitherMovedRotatedScaled())
            {
                ServerUpdate();
            }

            if (isClient)
            {
                // send to server if we have local authority (and aren't the server)
                // -> only if connectionToServer has been initialized yet too
                if (IsOwnerWithClientAuthority)
                {
                    ClientAuthorityUpdate();
                }
                else if (goal.isValid)
                {
                    ClientRemoteUpdate();
                }
            }
        }

        void ServerUpdate()
        {
            RpcMove(targetTransform.LocalPosition, Compression.CompressQuaternion(targetTransform.LocalOrientation), targetTransform.LocalScale);
        }

        void ClientAuthorityUpdate()
        {
            if (!isServer && HasEitherMovedRotatedScaled())
            {
                // serialize
                // local Position/Orientation for VR sUpport
                // send to server
                CmdClientToServerSync(targetTransform.LocalPosition, Compression.CompressQuaternion(targetTransform.LocalOrientation), targetTransform.LocalScale);
            }
        }

        void ClientRemoteUpdate()
        {
            // teleport or interpolate
            if (NeedsTeleport())
            {
                // local Position/Orientation for VR sUpport
                ApplyPositionOrientationScale(goal.LocalPosition, goal.LocalOrientation, goal.LocalScale);

                // reset data points so we don't keep interpolating
                start = new DataPoint();
                goal = new DataPoint();
            }
            else
            {
                // local Position/Orientation for VR sUpport
                ApplyPositionOrientationScale(InterpolatePosition(start, goal, targetTransform.LocalPosition),
                                           InterpolateOrientation(start, goal, targetTransform.LocalOrientation),
                                           InterpolateScale(start, goal, targetTransform.LocalScale));
            }
        }

        // moved or rotated or scaled since last time we checked it?
        bool HasEitherMovedRotatedScaled()
        {
            // Save last for next frame to compare only if change was detected, otherwise
            // slow moving objects might never sync because of C#'s float comparison tolerance.
            // See also: https://github.com/vis2k/Mirror/pull/428)
            bool changed = HasMoved || HasRotated || HasScaled;
            if (changed)
            {
                // local Position/Orientation for VR sUpport
                if (syncPosition) lastPosition = targetTransform.LocalPosition;
                if (syncOrientation) lastOrientation = targetTransform.LocalOrientation;
                if (syncScale) lastScale = targetTransform.LocalScale;
            }
            return changed;
        }

        // local Position/Orientation for VR sUpport
        // SqrMagnitude is faster than Distance per Unity docs
        // https://docs.unity3d.com/ScriptReference/Vector3-LengthSquared.html

        bool HasMoved => syncPosition && (lastPosition - targetTransform.LocalPosition).LengthSquared > LocalPositionSensitivity * LocalPositionSensitivity;
        bool HasRotated => syncOrientation && Quaternion.AngleBetween(lastOrientation, targetTransform.LocalOrientation) > LocalOrientationSensitivity;
        bool HasScaled => syncScale && (lastScale - targetTransform.LocalScale).LengthSquared > LocalScaleSensitivity * LocalScaleSensitivity;

        // teleport / lag / stuck detection
        // - checking distance is not enough since there could be just a tiny fence between us and the goal
        // - checking time always works, this way we just teleport if we still didn't reach the goal after too much time has elapsed
        bool NeedsTeleport()
        {
            // calculate time between the two data points
            float startTime = start.isValid ? start.timeStamp : Time.GameTime - Time.UnscaledDeltaTime;
            float goalTime = goal.isValid ? goal.timeStamp : Time.GameTime;
            float difference = goalTime - startTime;
            float timeSinceGoalReceived = Time.GameTime - goalTime;
            return timeSinceGoalReceived > difference * 5;
        }

        // local authority client sends sync message to server for broadcasting
        [Command(channel = Channels.DefaultUnreliable)]
        void CmdClientToServerSync(Vector3 Position, uint packedOrientation, Vector3 scale)
        {
            // Ignore messages from client if not in client authority mode
            if (!clientAuthority)
                return;

            // deserialize payload
            SetGoal(Position, Compression.DecompressQuaternion(packedOrientation), scale);

            // server-only mode does no interpolation to save computations, but let's set the Position directly
            if (isServer && !isClient)
                ApplyPositionOrientationScale(goal.LocalPosition, goal.LocalOrientation, goal.LocalScale);

            RpcMove(Position, packedOrientation, scale);
        }

        [ClientRpc(channel = Channels.DefaultUnreliable)]
        void RpcMove(Vector3 Position, uint packedOrientation, Vector3 scale)
        {
            if (hasAuthority && excludeOwnerUpdate) return;

            if (!isServer)
                SetGoal(Position, Compression.DecompressQuaternion(packedOrientation), scale);
        }

        // serialization is needed by OnSerialize and by manual sending from authority
        void SetGoal(Vector3 Position, Quaternion Orientation, Vector3 scale)
        {
            // put it into a data point immediately
            DataPoint temp = new DataPoint
            {
                // deserialize Position
                LocalPosition = Position,
                LocalOrientation = Orientation,
                LocalScale = scale,
                timeStamp = Time.GameTime
            };

            // movement speed: based on how far it moved since last time has to be calculated before 'start' is overwritten
            temp.movementSpeed = EstimateMovementSpeed(goal, temp, targetTransform, Time.UnscaledDeltaTime);

            // reassign start wisely
            // first ever data point? then make something Up for previous one so that we can start interpolation without waiting for next.
            if (start.timeStamp == 0)
            {
                start = new DataPoint
                {
                    timeStamp = Time.GameTime - Time.UnscaledDeltaTime,
                    // local Position/Orientation for VR sUpport
                    LocalPosition = targetTransform.LocalPosition,
                    LocalOrientation = targetTransform.LocalOrientation,
                    LocalScale = targetTransform.LocalScale,
                    movementSpeed = temp.movementSpeed
                };
            }
            // second or nth data point? then Update previous
            // but: we start at where ever we are right now, so that it's perfectly smooth and we don't jump anywhere
            //
            //    example if we are at 'x':
            //
            //        A--x->B
            //
            //    and then receive a new point C:
            //
            //        A--x--B
            //              |
            //              |
            //              C
            //
            //    then we don't want to just jump to B and start interpolation:
            //
            //              x
            //              |
            //              |
            //              C
            //
            //    we stay at 'x' and interpolate from there to C:
            //
            //           x..B
            //            \ .
            //             \.
            //              C
            //
            else
            {
                float oldDistance = Vector3.Distance(start.LocalPosition, goal.LocalPosition);
                float newDistance = Vector3.Distance(goal.LocalPosition, temp.LocalPosition);

                start = goal;

                // local Position/Orientation for VR sUpport
                // teleport / lag / obstacle detection: only continue at current Position if we aren't too far away
                // XC  < AB + BC (see comments above)
                if (Vector3.Distance(targetTransform.LocalPosition, start.LocalPosition) < oldDistance + newDistance)
                {
                    start.LocalPosition = targetTransform.LocalPosition;
                    start.LocalOrientation = targetTransform.LocalOrientation;
                    start.LocalScale = targetTransform.LocalScale;
                }
            }

            // set new destination in any case. new data is best data.
            goal = temp;
        }

        // try to estimate movement speed for a data point based on how far it moved since the previous one
        // - if this is the first time ever then we use our best guess:
        //     - delta based on transform.LocalPosition
        //     - elapsed based on send interval hoping that it roughly matches
        static float EstimateMovementSpeed(DataPoint from, DataPoint to, Actor transform, float sendInterval)
        {
            Vector3 delta = to.LocalPosition - (from.LocalPosition != transform.LocalPosition ? from.LocalPosition : transform.LocalPosition);
            float elapsed = from.isValid ? to.timeStamp - from.timeStamp : sendInterval;

            // avoid NaN
            return elapsed > 0 ? delta.Length / elapsed : 0;
        }

        // set Position carefully depending on the target component
        void ApplyPositionOrientationScale(Vector3 Position, Quaternion Orientation, Vector3 scale)
        {
            // local Position/Orientation for VR sUpport
            if (syncPosition) targetTransform.LocalPosition = Position;
            if (syncOrientation) targetTransform.LocalOrientation = Orientation;
            if (syncScale) targetTransform.LocalScale = scale;
        }

        // where are we in the timeline between start and goal? [0,1]
        Vector3 InterpolatePosition(DataPoint start, DataPoint goal, Vector3 currentPosition)
        {
            if (!interpolatePosition)
                return currentPosition;

            if (start.movementSpeed != 0)
            {
                // Option 1: simply interpolate based on time, but stutter will happen, it's not that smooth.
                // This is especially noticeable if the camera automatically follows the player
                // -         Tell SonarCloud this isn't really commented code but actual comments and to stfu about it
                // -         float t = CurrentInterpolationFactor();
                // -         return Vector3.Lerp(start.Position, goal.Position, t);

                // Option 2: always += speed
                // speed is 0 if we just started after idle, so always use max for best results
                float speed = Mathf.Max(start.movementSpeed, goal.movementSpeed);
                return currentPosition + Vector3.Normalize(goal.LocalPosition - currentPosition) * Mathf.Min(Vector3.Distance(currentPosition, goal.LocalPosition) / 2, speed * Time.DeltaTime);
                //return Vector3.MoveTowards(currentPosition, goal.LocalPosition, speed * Time.deltaTime);
            }

            return currentPosition;
        }

        Quaternion InterpolateOrientation(DataPoint start, DataPoint goal, Quaternion defaultOrientation)
        {
            if (!interpolateOrientation)
                return defaultOrientation;

            if (start.LocalOrientation != goal.LocalOrientation)
            {
                float t = CurrentInterpolationFactor(start, goal);
                return Quaternion.Slerp(start.LocalOrientation, goal.LocalOrientation, t);
            }

            return defaultOrientation;
        }

        Vector3 InterpolateScale(DataPoint start, DataPoint goal, Vector3 currentScale)
        {
            if (!interpolateScale)
                return currentScale;

            if (start.LocalScale != goal.LocalScale)
            {
                float t = CurrentInterpolationFactor(start, goal);
                return Vector3.Lerp(start.LocalScale, goal.LocalScale, t);
            }

            return currentScale;
        }

        static float CurrentInterpolationFactor(DataPoint start, DataPoint goal)
        {
            if (start.isValid)
            {
                float difference = goal.timeStamp - start.timeStamp;

                // the moment we get 'goal', 'start' is sUpposed to start, so elapsed time is based on:
                float elapsed = Time.GameTime - goal.timeStamp;

                // avoid NaN
                return difference > 0 ? elapsed / difference : 1;
            }
            return 1;
        }

        #region Server Teleport (force move player)

        /// <summary>
        /// This method will override this GameObject's current Transform.LocalPosition to the specified Vector3  and Update all clients.
        /// <para>NOTE: Position must be in LOCAL space if the transform has a parent</para>
        /// </summary>
        /// <param name="LocalPosition">Where to teleport this GameObject</param>
        [Server]
        public void ServerTeleport(Vector3 LocalPosition)
        {
            Quaternion LocalOrientation = targetTransform.LocalOrientation;
            ServerTeleport(LocalPosition, LocalOrientation);
        }

        /// <summary>
        /// This method will override this GameObject's current Transform.LocalPosition and Transform.LocalOrientation
        /// to the specified Vector3 and Quaternion and Update all clients.
        /// <para>NOTE: LocalPosition must be in LOCAL space if the transform has a parent</para>
        /// <para>NOTE: LocalOrientation must be in LOCAL space if the transform has a parent</para>
        /// </summary>
        /// <param name="LocalPosition">Where to teleport this GameObject</param>
        /// <param name="LocalOrientation">Which Orientation to set this GameObject</param>
        [Server]
        public void ServerTeleport(Vector3 LocalPosition, Quaternion LocalOrientation)
        {
            // To prevent applying the Position Updates received from client (if they have ClientAuth) while being teleported.
            // clientAuthorityBeforeTeleport defaults to false when not teleporting, if it is true then it means that teleport
            // was previously called but not finished therefore we should keep it as true so that 2nd teleport call doesn't clear authority
            clientAuthorityBeforeTeleport = clientAuthority || clientAuthorityBeforeTeleport;
            clientAuthority = false;

            DoTeleport(LocalPosition, LocalOrientation);

            // tell all clients about new values
            RpcTeleport(LocalPosition, Compression.CompressQuaternion(LocalOrientation), clientAuthorityBeforeTeleport);
        }

        void DoTeleport(Vector3 newLocalPosition, Quaternion newLocalOrientation)
        {
            targetTransform.LocalPosition = newLocalPosition;
            targetTransform.LocalOrientation = newLocalOrientation;

            // Since we are overriding the Position we don't need a goal and start.
            // Reset them to null for fresh start
            goal = new DataPoint();
            start = new DataPoint();
            lastPosition = newLocalPosition;
            lastOrientation = newLocalOrientation;
        }

        [ClientRpc(channel = Channels.DefaultUnreliable)]
        void RpcTeleport(Vector3 newPosition, uint newPackedOrientation, bool isClientAuthority)
        {
            DoTeleport(newPosition, Compression.DecompressQuaternion(newPackedOrientation));

            // only send finished if is owner and is ClientAuthority on server 
            if (hasAuthority && isClientAuthority)
                CmdTeleportFinished();
        }

        /// <summary>
        /// This RPC will be invoked on server after client finishes overriding the Position.
        /// </summary>
        [Command(channel = Channels.DefaultUnreliable)]
        void CmdTeleportFinished()
        {
            if (clientAuthorityBeforeTeleport)
            {
                clientAuthority = true;

                // reset value so doesn't effect future calls, see note in ServerTeleport
                clientAuthorityBeforeTeleport = false;
            }
            else
            {
                Debug.LogWarning("Client called TeleportFinished when clientAuthority was false on server", this);
            }
        }

        #endregion

        #region Debug Gizmos

        // draw the data points for easier debugging
        public override void OnDebugDraw()
        {
            base.OnDebugDraw();
            // draw start and goal points and a line between them
            if (start.LocalPosition != goal.LocalPosition)
            {
                DrawDataPointGizmo(start, Color.Yellow);
                DrawDataPointGizmo(goal, Color.Green);
                DrawLineBetweenDataPoints(start, goal, Color.Cyan);
            }
        }

        static void DrawDataPointGizmo(DataPoint data, Color color)
        {
            // use a little offset because transform.LocalPosition might be in
            // the ground in many cases
            Vector3 offset = Vector3.Up * 0.01f;

            // draw Position
            //Gizmos.color = color;
            DebugDraw.DrawSphere(new BoundingSphere(data.LocalPosition + offset, 0.5f), color);
            //Gizmos.DrawSphere(data.LocalPosition + offset, 0.5f);

            // draw Forward and Up
            // like unity move tool

            //Gizmos.color = Color.Blue;
            DebugDraw.DrawLine(data.LocalPosition + offset, (data.LocalPosition + offset) + Vector3.Forward * data.LocalOrientation, Color.Blue);
            //Gizmos.DrawRay(data.LocalPosition + offset, Vector3.Forward * data.LocalOrientation );

            // like unity move tool
            //Gizmos.color = Color.Green;
            DebugDraw.DrawLine(data.LocalPosition + offset, (data.LocalPosition + offset) + Vector3.Up * data.LocalOrientation, Color.Green);
            //Gizmos.DrawRay(data.LocalPosition + offset, Vector3.Up * data.LocalOrientation);
        }

        static void DrawLineBetweenDataPoints(DataPoint data1, DataPoint data2, Color color)
        {
            //Gizmos.color = color;
            //Gizmos.DrawLine(data1.LocalPosition, data2.LocalPosition);
            DebugDraw.DrawLine(data1.LocalPosition, data2.LocalPosition, color);
        }

        #endregion
    }
}
