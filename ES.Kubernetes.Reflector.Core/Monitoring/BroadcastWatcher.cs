using System;
using System.Threading;
using System.Threading.Tasks;
using ES.Kubernetes.Reflector.Core.Events;
using ES.Kubernetes.Reflector.Core.Queuing;
using k8s;
using MediatR;
using Microsoft.Rest;

namespace ES.Kubernetes.Reflector.Core.Monitoring
{
    public class BroadcastWatcher<TResource> : BroadcastWatcher<TResource, WatcherEvent<TResource>>
        where TResource : IKubernetesObject
    {
        public BroadcastWatcher(IMediator mediator, IKubernetes apiClient) : base(mediator, apiClient)
        {
        }
    }


    public class BroadcastWatcher<TResource, TNotification>
        where TResource : IKubernetesObject
        where TNotification : WatcherEvent<TResource>, new()
    {
        private readonly IKubernetes _apiClient;
        private readonly FeederQueue<TNotification> _queue;
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        private bool _isMonitoring;
        private Func<IKubernetes, Task<HttpOperationResponse>> _requestFactory;
        private Watcher<TResource> _watcher;
        public Action<TNotification> OnBeforePublish;

        public Action<BroadcastWatcher<TResource, TNotification>, BroadcastWatcherStateUpdate> OnStateChanged;

        public BroadcastWatcher(IMediator mediator, IKubernetes apiClient)
        {
            _apiClient = apiClient;
            _queue = new FeederQueue<TNotification>(item => mediator.Publish(item));
        }

        public bool IsFaulted { get; set; }

        public Func<IKubernetes, Task<HttpOperationResponse>> RequestFactory
        {
            get => _requestFactory;
            set
            {
                try
                {
                    _semaphore.Wait();
                    if (_isMonitoring)
                        throw new InvalidOperationException(
                            $"{nameof(RequestFactory)} cannot be set while monitor is running.");
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

            OnStateChanged?.Invoke(this, new BroadcastWatcherStateUpdate {State = BroadcastWatcherState.Starting});

            try
            {
                _semaphore.Wait();
                var request = await _requestFactory(_apiClient);

                _watcher = request.Watch<TResource>((eventType, item) =>
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
                OnStateChanged?.Invoke(this, new BroadcastWatcherStateUpdate
                    {State = BroadcastWatcherState.Faulted, Exception = e});
                throw;
            }
            finally
            {
                _semaphore.Release();
            }


            OnStateChanged?.Invoke(this, new BroadcastWatcherStateUpdate {State = BroadcastWatcherState.Started});
        }

        private void OnWatcherClosed()
        {
            OnStateChanged?.Invoke(this, new BroadcastWatcherStateUpdate {State = BroadcastWatcherState.Closed});
        }

        private void OnWatcherError(Exception e)
        {
            IsFaulted = true;
            OnStateChanged?.Invoke(
                this, new BroadcastWatcherStateUpdate {State = BroadcastWatcherState.Faulted, Exception = e});
        }

        public Task Stop()
        {
            IsFaulted = false;
            try
            {
                _semaphore.Wait();
                if (!_isMonitoring) return Task.CompletedTask;
                OnStateChanged?.Invoke(this, new BroadcastWatcherStateUpdate {State = BroadcastWatcherState.Stopping});
                _isMonitoring = false;

                _watcher.OnError -= OnWatcherError;
                _watcher.OnClosed -= OnWatcherClosed;
                _watcher?.Dispose();

                _queue.Clear();
            }
            finally
            {
                _semaphore.Release();
            }


            OnStateChanged?.Invoke(this, new BroadcastWatcherStateUpdate {State = BroadcastWatcherState.Stopped});
            return Task.CompletedTask;
        }
    }
}