namespace Battery.Simulation.Meters;

public sealed class MeterReadingObservable : IObservable<MeterReading>, IDisposable
{
    private readonly object _sync = new();
    private readonly List<IObserver<MeterReading>> _observers = [];
    private bool _isDisposed;

    public IDisposable Subscribe(IObserver<MeterReading> observer)
    {
        ArgumentNullException.ThrowIfNull(observer);

        lock (_sync)
        {
            if (_isDisposed)
            {
                observer.OnCompleted();
                return EmptySubscription.Instance;
            }

            _observers.Add(observer);
            return new Subscription(this, observer);
        }
    }

    public void Publish(MeterReading reading)
    {
        IObserver<MeterReading>[] observers;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            observers = _observers.ToArray();
        }

        foreach (var observer in observers)
        {
            try
            {
                observer.OnNext(reading);
            }
            catch (Exception exception)
            {
                observer.OnError(exception);
                Unsubscribe(observer);
            }
        }
    }

    public void Dispose()
    {
        IObserver<MeterReading>[] observers;
        lock (_sync)
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            observers = _observers.ToArray();
            _observers.Clear();
        }

        foreach (var observer in observers)
            observer.OnCompleted();
    }

    private void Unsubscribe(IObserver<MeterReading> observer)
    {
        lock (_sync)
            _observers.Remove(observer);
    }

    private sealed class Subscription(MeterReadingObservable publisher, IObserver<MeterReading> observer) : IDisposable
    {
        private MeterReadingObservable? _publisher = publisher;

        public void Dispose()
        {
            Interlocked.Exchange(ref _publisher, null)?.Unsubscribe(observer);
        }
    }

    private sealed class EmptySubscription : IDisposable
    {
        public static EmptySubscription Instance { get; } = new();
        public void Dispose() { }
    }
}
