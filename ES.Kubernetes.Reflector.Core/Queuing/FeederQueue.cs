using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace ES.Kubernetes.Reflector.Core.Queuing
{
    public class FeederQueue<T>
    {
        private readonly Func<T, Task> _handler;
        private readonly ConcurrentQueue<T> _queue = new ConcurrentQueue<T>();
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        private Task _queueProcessingTask;

        public FeederQueue(Func<T, Task> handler)
        {
            _handler = handler;
        }

        public void Feed(T item)
        {
            _queue.Enqueue(item);
            _semaphore.Wait();
            if (_queueProcessingTask == null || _queueProcessingTask.IsCompleted)
                _queueProcessingTask = ProcessEventsQueue();
            _semaphore.Release();
        }

        protected async Task ProcessEventsQueue()
        {
            while (_queue.TryDequeue(out var queuedEvent))
                await _handler(queuedEvent);
        }

        public void Clear()
        {
            while (!_queue.IsEmpty) _queue.TryDequeue(out _);
        }
    }
}