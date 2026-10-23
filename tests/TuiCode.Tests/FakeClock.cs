namespace TuiCode.Tests;

// Timers fire synchronously inside Advance.
internal sealed class FakeClock : TimeProvider
{
    private readonly List<Timer> _timers = [];
    private DateTimeOffset _now = new(2026, 10, 23, 10, 42, 7, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

    public void Advance(TimeSpan by)
    {
        _now += by;
        foreach (var timer in _timers.Where(timer => timer.Due <= _now).ToList())
        {
            _timers.Remove(timer);
            timer.Fire();
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new Timer(this, () => callback(state), _now + dueTime);
        _timers.Add(timer);
        return timer;
    }

    private sealed class Timer(FakeClock clock, Action fire, DateTimeOffset due) : ITimer
    {
        public DateTimeOffset Due { get; private set; } = due;

        public void Fire() => fire();

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            Due = clock._now + dueTime;
            return true;
        }

        public void Dispose() => clock._timers.Remove(this);

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
