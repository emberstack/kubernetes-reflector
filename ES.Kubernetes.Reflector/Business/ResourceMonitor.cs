using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using k8s;
using Microsoft.Extensions.Logging;
using Microsoft.Rest;

namespace ES.Kubernetes.Reflector.Business
{
    public abstract class ResourceMonitor<T> where T : class, IKubernetesObject
    {
        private readonly ConcurrentQueue<(WatchEventType EventType, T Item)> _eventsQueue =
            new ConcurrentQueue<(WatchEventType EventType, T Item)>();

        private readonly ConcurrentDictionary<string, Func<WatchEventType, T, Task>> _eventSubscribers =
            new ConcurrentDictionary<string, Func<WatchEventType, T, Task>>();

        private readonly SemaphoreSlim _processingSemaphore = new SemaphoreSlim(1, 1);
        private Task _queueProcessingTask;
        private Watcher<T> _watcher;

        protected ResourceMonitor(ILogger logger, IKubernetes apiClient)
        {
            Logger = logger;
            ApiClient = apiClient;
        }

        protected ILogger Logger { get; }
        protected IKubernetes ApiClient { get; }

        public bool IsMonitoring { get; private set; }


        public async Task Start()
        {
            Logger.LogDebug("Starting monitor");

            if (_watcher != null)
            {
                _watcher.OnClosed -= _watcher_OnClosed;
                _watcher.OnError -= _watcher_OnError;
                _watcher?.Dispose();
            }

            _eventsQueue.Clear();

            var listRequest = await ListRequest(ApiClient);
            IsMonitoring = true;
            _watcher = listRequest.Watch<T>((eventType, item) =>
            {
                _eventsQueue.Enqueue((eventType, item));
                _processingSemaphore.Wait();
                if (_queueProcessingTask == null || _queueProcessingTask.IsCompleted)
                    _queueProcessingTask = ProcessEventsQueue();
                _processingSemaphore.Release();
            });
            _watcher.OnClosed += _watcher_OnClosed;
            _watcher.OnError += _watcher_OnError;
        }

        private async void _watcher_OnError(Exception ex)
        {
            Logger.LogError(ex, "An error occured on the watcher. Restarting.");
            await Start();
        }

        private async void _watcher_OnClosed()
        {
            Logger.LogDebug("Watcher closed. Restarting");
            await Start();
        }

        protected async Task ProcessEventsQueue()
        {
            while (_eventsQueue.TryDequeue(out var queuedEvent))
                try
                {
                    var subscribers = _eventSubscribers.Values.ToList();
                    foreach (var subscriber in subscribers)
                        try
                        {
                            await subscriber(queuedEvent.EventType, queuedEvent.Item);
                        }
                        catch (Exception exception)
                        {
                            Logger.LogError(exception, "Exception occurred in event subscriber.");
                        }
                }
                catch (Exception exception)
                {
                    Logger.LogError(exception, "Failed to process event due to exception.");
                }
        }

        public Task Stop()
        {
            Logger.LogDebug("Stopping monitor");
            if (_watcher != null)
            {
                _watcher.OnClosed -= _watcher_OnClosed;
                _watcher.OnError -= _watcher_OnError;
                _watcher?.Dispose();
            }

            _eventsQueue.Clear();
            IsMonitoring = false;
            return Task.CompletedTask;
        }

        protected abstract Task<HttpOperationResponse> ListRequest(IKubernetes apiClient);


        public string Subscribe(Func<WatchEventType, T, Task> eventHandler)
        {
            var key = Guid.NewGuid().ToString();
            _eventSubscribers[key] = eventHandler;
            return key;
        }

        public void Unsubscribe(string key)
        {
            if (key == null) return;
            _eventSubscribers.TryRemove(key, out _);
        }
    }
}