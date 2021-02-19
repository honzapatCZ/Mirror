using System.Collections;
using System.Threading.Tasks;
using FlaxEngine;

namespace Mirror.Authenticators
{
    /// <summary>
    /// An authenticator that disconnects connections if they don't
    /// authenticate within a specified time limit.
    /// </summary>
    //[AddComponentMenu("Network/Authenticators/TimeoutAuthenticator")]
    public class TimeoutAuthenticator : NetworkAuthenticator
    {
        static readonly ILogger logger = LogFactory.GetLogger(typeof(TimeoutAuthenticator));

        public NetworkAuthenticator authenticator;

        [Range(0, 600), Tooltip("Timeout to auto-disconnect in seconds. Set to 0 for no timeout.")]
        public float timeout = 60;

        public override void OnAwake()
        {
            base.OnAwake();
            authenticator.OnServerAuthenticated += (connection => OnServerAuthenticated.Invoke(connection));
            authenticator.OnClientAuthenticated += (connection => OnClientAuthenticated.Invoke(connection));
        }

        public override void OnStartServer()
        {
            authenticator.OnStartServer();
        }

        public override void OnStopServer()
        {
            authenticator.OnStopServer();
        }

        public override void OnStartClient()
        {
            authenticator.OnStartClient();
        }

        public override void OnStopClient()
        {
            authenticator.OnStopClient();
        }

        public override void OnServerAuthenticate(NetworkConnection conn)
        {
            authenticator.OnServerAuthenticate(conn);
            if (timeout > 0)
                Task.Run(() => BeginAuthentication(conn));
        }

        public override void OnClientAuthenticate(NetworkConnection conn)
        {
            authenticator.OnClientAuthenticate(conn);
            if (timeout > 0)
                Task.Run(() => BeginAuthentication(conn));
        }

        async Task BeginAuthentication(NetworkConnection conn)
        {
            if (logger.LogEnabled()) logger.Log($"Authentication countdown started {conn} {timeout}");

            await Task.Delay(Mathf.RoundToInt(timeout * 1000));
            //yield return new WaitForSecondsRealtime(timeout);

            if (!conn.isAuthenticated)
            {
                if (logger.LogEnabled()) logger.Log($"Authentication Timeout {conn}");

                conn.Disconnect();
            }
        }
    }
}
