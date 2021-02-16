using System.Collections;
using FlaxEngine;

namespace Mirror.Cloud.ListServerService
{
    public sealed class ListServerClientApi : ListServerBaseApi, IListServerClientApi
    {
        private System.Action<ServerCollectionJson> _onServerListUpdated;

        Coroutine getServerListRepeatCoroutine;

        public event System.Action<ServerCollectionJson> onServerListUpdated
        {
            add => _onServerListUpdated += (value);
            remove => _onServerListUpdated -= (value);
        }

        public ListServerClientApi(ICoroutineRunner runner, IRequestCreator requestCreator, System.Action<ServerCollectionJson> onServerListUpdated) : base(runner, requestCreator)
        {
            _onServerListUpdated = onServerListUpdated;
        }

        public void Shutdown()
        {
            StopGetServerListRepeat();
        }

        public void GetServerList()
        {
            runner.StartCoroutine(getServerList());
        }

        public void StartGetServerListRepeat(int interval)
        {
            getServerListRepeatCoroutine = runner.StartCoroutine(GetServerListRepeat(interval));
        }

        public void StopGetServerListRepeat()
        {
            // if runner is null it has been destroyed and will already be null
            if (runner.IsNotNull() && getServerListRepeatCoroutine != null)
            {
                runner.StopCoroutine(getServerListRepeatCoroutine);
            }
        }

        IEnumerator GetServerListRepeat(int interval)
        {
            while (true)
            {
                yield return getServerList();

                yield return new WaitForSeconds(interval);
            }
        }
        IEnumerator getServerList()
        {
            UnityWebRequest request = requestCreator.Get("servers");
            yield return requestCreator.SendRequestEnumerator(request, onSuccess);

            void onSuccess(string responseBody)
            {
                ServerCollectionJson serverlist = JsonUtility.FromJson<ServerCollectionJson>(responseBody);
                _onServerListUpdated?.Invoke(serverlist);
            }
        }
    }
}
