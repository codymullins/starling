namespace Tessera.Loop;

/// <summary>
/// Deterministic event loop core for tests/headless hosts. Microtasks drain
/// before timers; timers run once their due time is reached.
/// </summary>
public sealed class WebEventLoop
{
    private readonly PriorityQueue<TimerTask, TimerKey> _timers = new();
    private readonly Queue<Action> _microtasks = new();
    private long _nowMs;
    private int _nextTimerId = 1;
    private long _sequence;

    public long NowMilliseconds => _nowMs;
    public int PendingTimerCount => _timers.Count;
    public int PendingMicrotaskCount => _microtasks.Count;

    public void QueueMicrotask(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        _microtasks.Enqueue(callback);
    }

    public int SetTimeout(Action callback, int delayMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(callback);
        if (delayMilliseconds < 0) delayMilliseconds = 0;

        var task = new TimerTask(_nextTimerId++, callback, Cancelled: false);
        _timers.Enqueue(task, new TimerKey(_nowMs + delayMilliseconds, _sequence++));
        return task.Id;
    }

    public bool ClearTimeout(int id)
    {
        var removed = false;
        var kept = new List<(TimerTask Task, TimerKey Key)>(_timers.Count);
        while (_timers.TryDequeue(out var task, out var key))
        {
            if (task.Id == id)
            {
                removed = true;
                continue;
            }
            kept.Add((task, key));
        }

        foreach (var (task, key) in kept)
            _timers.Enqueue(task, key);
        return removed;
    }

    public void AdvanceBy(int milliseconds)
    {
        if (milliseconds < 0)
            throw new ArgumentOutOfRangeException(nameof(milliseconds), "Cannot move event-loop time backwards.");
        _nowMs += milliseconds;
        RunUntilIdle();
    }

    public void RunUntilIdle()
    {
        DrainMicrotasks();
        while (_timers.TryPeek(out _, out var key) && key.DueMilliseconds <= _nowMs)
        {
            var task = _timers.Dequeue();
            task.Callback();
            DrainMicrotasks();
        }
    }

    private void DrainMicrotasks()
    {
        while (_microtasks.TryDequeue(out var callback))
            callback();
    }

    private readonly record struct TimerTask(int Id, Action Callback, bool Cancelled);

    private readonly record struct TimerKey(long DueMilliseconds, long Sequence) : IComparable<TimerKey>
    {
        public int CompareTo(TimerKey other)
        {
            var byDue = DueMilliseconds.CompareTo(other.DueMilliseconds);
            return byDue != 0 ? byDue : Sequence.CompareTo(other.Sequence);
        }
    }
}
