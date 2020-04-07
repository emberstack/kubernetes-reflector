using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace ES.Kubernetes.Reflector.Core.Queuing
{
    public class FeederQueue<T>
    {
        private readonly Func<T, Task> _handler;
        private readonly Func<T, Exception, Task> _onError;
        private readonly ConcurrentQueue<T> _queue = new ConcurrentQueue<T>();
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        private Task _currentHandler;
        private Task _queueProcessingTask;

        public FeederQueue(Func<T, Task> handler, Func<T, Exception, Task> onError = null)
        {
            _handler = handler;
            _onError = onError;
        }

        public void Feed(T item)
        {
            FeedAsync(item).Wait();
        }

        public async Task FeedAsync(T item)
        {
            try
            {
                _queue.Enqueue(item);
                await _semaphore.WaitAsync();
                if (_queueProcessingTask == null || _queueProcessingTask.IsCompleted)
                    _queueProcessingTask = ProcessEventsQueue();
            }
            finally
            {
                _semaphore.Release();
            }
        }


        protected async Task ProcessEventsQueue()
        {
            try
            {
                while (_queue.TryDequeue(out var queuedEvent))
                    try
                    {
                        _currentHandler = _handler(queuedEvent);
                        await _currentHandler;
                    }
                    catch (Exception exception)
                    {
                        await (_onError?.Invoke(queuedEvent, exception) ?? Task.CompletedTask);
                    }
            }
            finally
            {
                _currentHandler = null;
            }
        }

        public void Clear()
        {
            while (!_queue.IsEmpty) _queue.TryDequeue(out _);
        }

        public async Task WaitAndClear()
        {
            while (!_queue.IsEmpty) _queue.TryDequeue(out _);
            await (_currentHandler ?? Task.CompletedTask);
        }
    }
}