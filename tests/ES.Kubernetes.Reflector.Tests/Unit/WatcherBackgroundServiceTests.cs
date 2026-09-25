using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using ES.Kubernetes.Reflector.Configuration;
using ES.Kubernetes.Reflector.Watchers.Core;
using ES.Kubernetes.Reflector.Watchers.Core.Events;
using k8s;
using k8s.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace ES.Kubernetes.Reflector.Tests.Unit;

public class WatcherBackgroundServiceTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task HandlerThrows_SubsequentEventsAreStillProcessed()
    {
        var handler = new RecordingHandler(name =>
            name == "bad" ? throw new InvalidOperationException("boom") : Task.CompletedTask);
        await using var watcher = new FakeWatcher(handler, new WatcherOptions());

        await watcher.StartAsync(CancellationToken.None);
        await watcher.Emit("bad");
        await watcher.Emit("good");

        await handler.WaitForAsync("good");
        Assert.Equal(["bad", "good"], handler.Handled);
    }

    [Fact]
    public async Task HandlerHangs_IsCanceledAfterEventHandlerTimeout_AndSubsequentEventsAreProcessed()
    {
        var handler = new RecordingHandler(async (name, ct) =>
        {
            if (name == "hang") await Task.Delay(Timeout.Infinite, ct);
        });
        await using var watcher = new FakeWatcher(handler, new WatcherOptions { EventHandlerTimeout = 1 });

        await watcher.StartAsync(CancellationToken.None);
        await watcher.Emit("hang");
        await watcher.Emit("next");

        await handler.WaitForAsync("next");
        Assert.Equal(["hang", "next"], handler.Handled);
    }

    [Fact]
    public async Task HandlerThrowsNonSessionCancellation_SubsequentEventsAreStillProcessed()
    {
        // e.g. an HttpClient timeout surfaces as TaskCanceledException
        var handler = new RecordingHandler(name =>
            name == "http-timeout" ? throw new TaskCanceledException() : Task.CompletedTask);
        await using var watcher = new FakeWatcher(handler, new WatcherOptions());

        await watcher.StartAsync(CancellationToken.None);
        await watcher.Emit("http-timeout");
        await watcher.Emit("next");

        await handler.WaitForAsync("next");
        Assert.Equal(["http-timeout", "next"], handler.Handled);
    }

    private sealed class RecordingHandler(Func<string, CancellationToken, Task> onHandle) : IWatcherEventHandler
    {
        private readonly ConcurrentDictionary<string, TaskCompletionSource> _completed = new();

        public RecordingHandler(Func<string, Task> onHandle) : this((name, _) => onHandle(name))
        {
        }

        public ConcurrentQueue<string> Handled { get; } = new();

        public async Task Handle(WatcherEvent e, CancellationToken cancellationToken)
        {
            var name = e.Item!.Metadata.Name;
            Handled.Enqueue(name);
            try
            {
                await onHandle(name, cancellationToken);
            }
            finally
            {
                Completion(name).TrySetResult();
            }
        }

        public Task WaitForAsync(string name) => Completion(name).Task.WaitAsync(TestTimeout);

        private TaskCompletionSource Completion(string name) =>
            _completed.GetOrAdd(name, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
    }

    private sealed class FakeWatcher(IWatcherEventHandler handler, WatcherOptions watcherOptions)
        : WatcherBackgroundService<V1Secret, V1SecretList>(
            NullLogger.Instance,
            Mock.Of<IOptionsMonitor<ReflectorOptions>>(o =>
                o.CurrentValue == new ReflectorOptions { Watcher = watcherOptions }),
            [handler],
            []), IAsyncDisposable
    {
        private readonly Channel<V1Secret> _stream = Channel.CreateUnbounded<V1Secret>();

        public ValueTask Emit(string name) => _stream.Writer.WriteAsync(new V1Secret
        {
            Metadata = new V1ObjectMeta { Name = name, NamespaceProperty = "default" }
        });

        protected override async IAsyncEnumerable<(WatchEventType, V1Secret)> OnGetWatcher(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (var item in _stream.Reader.ReadAllAsync(cancellationToken))
                yield return (WatchEventType.Added, item);
        }

        public async ValueTask DisposeAsync()
        {
            await StopAsync(CancellationToken.None);
            Dispose();
        }
    }
}
