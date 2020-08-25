using System;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace ES.Kubernetes.Reflector.Core.Queuing
{
    public class FeederQueue<T>
    {
        private readonly Func<T, Task> _handler;
        private readonly Func<T, Exception, Task> _onError;
        private Task _currentHandler;
        private Channel<T> _channel;

        public FeederQueue(Func<T, Task> handler, Func<T, Exception, Task> onError = null)
        {
            _handler = handler;
            _onError = onError;
            InitializeAndStart();
        }

        public void Feed(T item)
        {
            FeedAsync(item).Wait();
        }

        public async Task FeedAsync(T item)
        {
            await _channel.Writer.WriteAsync(item);
        }

        public void Clear()
        {
            _channel?.Writer.Complete();
            InitializeAndStart();
        }

        private void InitializeAndStart()
        {
            var channel = Channel.CreateUnbounded<T>(new UnboundedChannelOptions
            { SingleReader = true, SingleWriter = false });

            async Task ReadChannel()
            {
                while (await channel.Reader.WaitToReadAsync())
                {
                    var item = await channel.Reader.ReadAsync();
                    try
                    {
                        _currentHandler = _handler(item);
                        await _currentHandler;
                    }
                    catch (Exception exception)
                    {
                        await (_onError?.Invoke(item, exception) ?? Task.CompletedTask);
                    }
                }
            }
            var _ = ReadChannel();
            _channel = channel;
        }

        public async Task WaitAndClear()
        {
            await (_currentHandler ?? Task.CompletedTask);
            Clear();
        }
    }
}