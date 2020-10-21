using System;
using System.Threading;
using System.Threading.Tasks;
using ES.Kubernetes.Reflector.Core.Events;
using ES.Kubernetes.Reflector.Core.Queuing;
using k8s;
using Microsoft.Rest;

namespace ES.Kubernetes.Reflector.Core.Monitoring
{
    public class
        ManagedWatcher<TResource, TResourceList> : ManagedWatcher<TResource, TResourceList, WatcherEvent<TResource>>
        where TResource : class, IKubernetesObject
    {
        public ManagedWatcher(IKubernetes apiClient) : base(apiClient)
        {
        }
    }


    public class ManagedWatcher<TResource, TResourceList, TNotification>
        where TResource : class, IKubernetesObject
        where TNotification : WatcherEvent<TResource>, new()
    {
        private readonly IKubernetes _client;
        private readonly FeederQueue<TNotification> _queue;
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        private Func<TNotification, Task> _eventHandlerFactory;
        private bool _isMonitoring;
        private Func<IKubernetes, Task<HttpOperationResponse<TResourceList>>> _requestFactory;
        private Watcher<TResource> _watcher;
        public Action<TNotification> OnBeforePublish;

        public Func<ManagedWatcher<TResource, TResourceList, TNotification>, ManagedWatcherStateUpdate, Task>
            OnStateChanged;

        public ManagedWatcher(IKubernetes client)
        {
            _client = client;
            _queue = new FeederQueue<TNotification>(item => EventHandlerFactory?.Invoke(item) ?? Task.CompletedTask);
        }

        public string Tag { get; set; }

        public bool IsFaulted { get; set; }


        public Func<TNotification, Task> EventHandlerFactory
        {
            get => _eventHandlerFactory;
            set
            {
                try
                {
                    _semaphore.Wait();
                    if (_isMonitoring)
                        throw new InvalidOperationException(
                            $"{nameof(EventHandlerFactory)} cannot be set while watcher is started.");
                    _eventHandlerFactory = value;
                }
                finally
                {
                    _semaphore.Release();
                }
            }
        }


        public Func<IKubernetes, Task<HttpOperationResponse<TResourceList>>> RequestFactory
        {
            get => _requestFactory;
            set
            {
                try
                {
                    _semaphore.Wait();
                    if (_isMonitoring)
                        throw new InvalidOperationException(
                            $"{nameof(RequestFactory)} cannot be set while watcher is started.");
                }
                finally
                {
                    _semaphore.Release();
                }

                _requestFactory = value;
            }
        }


        public async Task Start()
        {
            await Stop();

            OnStateChanged?.Invoke(this, new ManagedWatcherStateUpdate {State = ManagedWatcherState.Starting});

            try
            {
                await _semaphore.WaitAsync();
                var request = await _requestFactory(_client);

                _watcher = request.Watch<TResource, TResourceList>((eventType, item) =>
                {
                    var notification = new TNotification {Item = item, Type = eventType};
                    OnBeforePublish?.Invoke(notification);
                    _queue.Feed(notification);
                });

                _watcher.OnError += OnWatcherError;
                _watcher.OnClosed += OnWatcherClosed;
                _isMonitoring = true;
            }
            catch (Exception e)
            {
                IsFaulted = true;
                OnStateChanged?.Invoke(this, new ManagedWatcherStateUpdate
                    {State = ManagedWatcherState.Faulted, Exception = e});
                throw;
            }
            finally
            {
                _semaphore.Release();
            }


            OnStateChanged?.Invoke(this, new ManagedWatcherStateUpdate {State = ManagedWatcherState.Started});
        }

        private void OnWatcherClosed()
        {
            OnStateChanged?.Invoke(this, new ManagedWatcherStateUpdate {State = ManagedWatcherState.Closed});
        }

        private void OnWatcherError(Exception e)
        {
            IsFaulted = true;
            OnStateChanged?.Invoke(
                this, new ManagedWatcherStateUpdate {State = ManagedWatcherState.Faulted, Exception = e});
        }

        public async Task Stop()
        {
            IsFaulted = false;
            try
            {
                await _semaphore.WaitAsync();
                if (!_isMonitoring) return;
                OnStateChanged?.Invoke(this, new ManagedWatcherStateUpdate {State = ManagedWatcherState.Stopping});
                _isMonitoring = false;

                _watcher.OnError -= OnWatcherError;
                _watcher.OnClosed -= OnWatcherClosed;
                _watcher?.Dispose();

                await _queue.WaitAndClear();
            }
            finally
            {
                _semaphore.Release();
            }


            OnStateChanged?.Invoke(this, new ManagedWatcherStateUpdate {State = ManagedWatcherState.Stopped});
        }
    }
}