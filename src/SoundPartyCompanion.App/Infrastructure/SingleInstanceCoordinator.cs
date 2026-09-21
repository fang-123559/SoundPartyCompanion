using System.Threading;

namespace SoundPartyCompanion.App.Infrastructure;

public sealed class SingleInstanceCoordinator : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activationEvent;
    private readonly Action _activationRequested;
    private readonly RegisteredWaitHandle _activationRegistration;
    private int _disposed;

    private SingleInstanceCoordinator(
        Mutex mutex,
        EventWaitHandle activationEvent,
        Action activationRequested)
    {
        _mutex = mutex;
        _activationEvent = activationEvent;
        _activationRequested = activationRequested;
        _activationRegistration = ThreadPool.RegisterWaitForSingleObject(
            _activationEvent,
            static (state, _) => ((SingleInstanceCoordinator)state!).HandleActivationRequested(),
            this,
            Timeout.Infinite,
            executeOnlyOnce: false);
    }

    public static SingleInstanceCoordinator? TryAcquire(
        string instanceId,
        Action activationRequested)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        ArgumentNullException.ThrowIfNull(activationRequested);

        var activationEvent = new EventWaitHandle(
            initialState: false,
            EventResetMode.AutoReset,
            $"Local\\{instanceId}.Activate");
        var mutex = new Mutex(
            initiallyOwned: true,
            $"Local\\{instanceId}.Mutex",
            out var createdNew);

        if (createdNew)
        {
            return new SingleInstanceCoordinator(mutex, activationEvent, activationRequested);
        }

        activationEvent.Set();
        activationEvent.Dispose();
        mutex.Dispose();
        return null;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        _activationRegistration.Unregister(null);
        _activationEvent.Dispose();
        try
        {
            _mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
        }
        _mutex.Dispose();
    }

    private void HandleActivationRequested()
    {
        if (Volatile.Read(ref _disposed) == 0)
        {
            _activationRequested();
        }
    }
}
