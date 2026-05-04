using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace MyThreadPool;

public sealed class DynamicThreadPool : IDisposable
{
    private readonly Queue<WorkItem> _queue = new();
    private readonly List<WorkerState> _workers = new();
    private readonly object _lock = new();

    private readonly int _minWorkers;
    private readonly int _maxWorkers;
    private readonly TimeSpan _idleTimeout;
    private readonly TimeSpan _scaleCheckInterval;
    private readonly TimeSpan _queueWaitScaleThreshold;
    private readonly TimeSpan _hungTaskThreshold;

    private readonly Action<string>? _log;

    private volatile bool _isShutdown;
    private int _workerIdCounter;

    private readonly Thread _scalerThread;
    private readonly Thread _watchdogThread;

    public DynamicThreadPool(
        int minWorkers,
        int maxWorkers,
        TimeSpan idleTimeout,
        TimeSpan scaleCheckInterval,
        TimeSpan queueWaitScaleThreshold,
        TimeSpan hungTaskThreshold,
        Action<string>? log = null)
    {
        if (minWorkers <= 0)
            throw new ArgumentOutOfRangeException(nameof(minWorkers));
        if (maxWorkers < minWorkers)
            throw new ArgumentOutOfRangeException(nameof(maxWorkers));

        _minWorkers = minWorkers;
        _maxWorkers = maxWorkers;
        _idleTimeout = idleTimeout;
        _scaleCheckInterval = scaleCheckInterval;
        _queueWaitScaleThreshold = queueWaitScaleThreshold;
        _hungTaskThreshold = hungTaskThreshold;
        _log = log;

        for (var i = 0; i < _minWorkers; i++)
        {
            CreateWorker("initial");
        }

        _scalerThread = new Thread(ScalerLoop)
        {
            IsBackground = true,
            Name = "DynamicPool-Scaler"
        };
        _scalerThread.Start();

        _watchdogThread = new Thread(WatchdogLoop)
        {
            IsBackground = true,
            Name = "DynamicPool-Watchdog"
        };
        _watchdogThread.Start();
    }

    public void Enqueue(Action action)
    {
        if (action is null)
            throw new ArgumentNullException(nameof(action));

        lock (_lock)
        {
            if (_isShutdown)
                throw new InvalidOperationException("Thread pool is shutting down.");

            _queue.Enqueue(new WorkItem(action, DateTime.UtcNow));
            Monitor.PulseAll(_lock);
        }
    }

    public PoolSnapshot GetSnapshot()
    {
        lock (_lock)
        {
            var alive = 0;
            var busy = 0;
            foreach (var worker in _workers)
            {
                if (worker.IsAlive)
                {
                    alive++;
                    if (worker.IsExecuting)
                    {
                        busy++;
                    }
                }
            }

            return new PoolSnapshot(
                _queue.Count,
                alive,
                busy,
                Math.Max(0, alive - busy));
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _isShutdown = true;
            foreach (var worker in _workers)
            {
                worker.ShouldStop = true;
            }
            Monitor.PulseAll(_lock);
        }

        if (!_scalerThread.Join(TimeSpan.FromSeconds(2)))
        {
            _scalerThread.Interrupt();
        }

        if (!_watchdogThread.Join(TimeSpan.FromSeconds(2)))
        {
            _watchdogThread.Interrupt();
        }

        List<Thread> workersToJoin;
        lock (_lock)
        {
            workersToJoin = new List<Thread>();
            foreach (var worker in _workers)
            {
                if (worker.Thread is not null)
                {
                    workersToJoin.Add(worker.Thread);
                }
            }
        }

        foreach (var thread in workersToJoin)
        {
            thread.Join(TimeSpan.FromSeconds(2));
        }
    }

    private void ScalerLoop()
    {
        try
        {
            while (!_isShutdown)
            {
                Thread.Sleep(_scaleCheckInterval);

                lock (_lock)
                {
                    if (_isShutdown)
                    {
                        return;
                    }

                    EnsureMinWorkersLocked();

                    var alive = CountAliveWorkersLocked();
                    var idle = CountIdleWorkersLocked();
                    var queueSize = _queue.Count;
                    var oldestWait = queueSize > 0
                        ? DateTime.UtcNow - _queue.Peek().EnqueuedAtUtc
                        : TimeSpan.Zero;

                    var needScaleUp = queueSize > idle || oldestWait > _queueWaitScaleThreshold;
                    if (needScaleUp && alive < _maxWorkers)
                    {
                        CreateWorker("scale-up");
                    }
                }
            }
        }
        catch (ThreadInterruptedException)
        {
        }
        catch (Exception ex)
        {
            _log?.Invoke($"[POOL][ERROR] Scaler crashed: {ex.Message}");
        }
    }

    private void WatchdogLoop()
    {
        try
        {
            while (!_isShutdown)
            {
                Thread.Sleep(_scaleCheckInterval);

                lock (_lock)
                {
                    if (_isShutdown)
                    {
                        return;
                    }

                    foreach (var worker in _workers)
                    {
                        if (!worker.IsAlive || !worker.IsExecuting || worker.CurrentTaskStartedAtUtc is null)
                        {
                            continue;
                        }

                        var elapsed = DateTime.UtcNow - worker.CurrentTaskStartedAtUtc.Value;
                        if (elapsed <= _hungTaskThreshold || worker.ReplacementSpawned)
                        {
                            continue;
                        }

                        worker.ReplacementSpawned = true;
                        _log?.Invoke($"[POOL][WARN] Worker {worker.WorkerId} seems hung ({elapsed.TotalMilliseconds:F0}ms). Spawning replacement.");

                        if (CountAliveWorkersLocked() < _maxWorkers)
                        {
                            CreateWorker("hung-replacement");
                        }
                    }

                    EnsureMinWorkersLocked();
                }
            }
        }
        catch (ThreadInterruptedException)
        {
        }
        catch (Exception ex)
        {
            _log?.Invoke($"[POOL][ERROR] Watchdog crashed: {ex.Message}");
        }
    }

    private void WorkerLoop(WorkerState state)
    {
        while (true)
        {
            WorkItem item = default;
            var hasItem = false;

            lock (_lock)
            {
                while (!_isShutdown && !state.ShouldStop && _queue.Count == 0)
                {
                    var alive = CountAliveWorkersLocked();
                    if (alive > _minWorkers)
                    {
                        if (!Monitor.Wait(_lock, _idleTimeout))
                        {
                            alive = CountAliveWorkersLocked();
                            if (alive > _minWorkers)
                            {
                                state.ShouldStop = true;
                                break;
                            }
                        }
                    }
                    else
                    {
                        Monitor.Wait(_lock);
                    }
                }

                if (_isShutdown || state.ShouldStop)
                {
                    state.IsAlive = false;
                    state.IsExecuting = false;
                    state.CurrentTaskStartedAtUtc = null;
                    return;
                }

                if (_queue.Count > 0)
                {
                    item = _queue.Dequeue();
                    hasItem = true;
                    state.IsExecuting = true;
                    state.CurrentTaskStartedAtUtc = DateTime.UtcNow;
                }
            }

            if (!hasItem)
            {
                continue;
            }

            try
            {
                item.Action();
            }
            catch (Exception ex)
            {
                _log?.Invoke($"[POOL][ERROR] Worker {state.WorkerId} task failed: {ex.Message}");
            }
            finally
            {
                lock (_lock)
                {
                    state.IsExecuting = false;
                    state.CurrentTaskStartedAtUtc = null;
                }
            }
        }
    }

    private void CreateWorker(string reason)
    {
        var workerId = Interlocked.Increment(ref _workerIdCounter);
        var state = new WorkerState(workerId)
        {
            IsAlive = true
        };

        var thread = new Thread(() => WorkerEntry(state))
        {
            IsBackground = true,
            Name = $"DynamicPool-Worker-{workerId}"
        };

        state.Thread = thread;
        _workers.Add(state);
        thread.Start();

        _log?.Invoke($"[POOL] Worker {workerId} created ({reason}). Active={CountAliveWorkersLocked()} Queue={_queue.Count}");
    }

    private void WorkerEntry(WorkerState state)
    {
        try
        {
            WorkerLoop(state);
        }
        catch (Exception ex)
        {
            lock (_lock)
            {
                state.IsAlive = false;
                state.IsExecuting = false;
                state.CurrentTaskStartedAtUtc = null;
                _log?.Invoke($"[POOL][ERROR] Worker {state.WorkerId} crashed: {ex.Message}");

                if (!_isShutdown && CountAliveWorkersLocked() < _minWorkers)
                {
                    CreateWorker("recovery-after-crash");
                }
            }
        }
        finally
        {
            lock (_lock)
            {
                state.IsAlive = false;
                state.IsExecuting = false;
                state.CurrentTaskStartedAtUtc = null;
                _log?.Invoke($"[POOL] Worker {state.WorkerId} stopped. Active={CountAliveWorkersLocked()}");
            }
        }
    }

    private void EnsureMinWorkersLocked()
    {
        var alive = CountAliveWorkersLocked();
        while (alive < _minWorkers)
        {
            CreateWorker("maintain-min");
            alive = CountAliveWorkersLocked();
        }
    }

    private int CountAliveWorkersLocked()
    {
        var count = 0;
        foreach (var worker in _workers)
        {
            if (worker.IsAlive)
            {
                count++;
            }
        }
        return count;
    }

    private int CountIdleWorkersLocked()
    {
        var count = 0;
        foreach (var worker in _workers)
        {
            if (worker.IsAlive && !worker.IsExecuting)
            {
                count++;
            }
        }
        return count;
    }

    private readonly record struct WorkItem(Action Action, DateTime EnqueuedAtUtc);

    private sealed class WorkerState
    {
        public WorkerState(int workerId)
        {
            WorkerId = workerId;
        }

        public int WorkerId { get; }
        public Thread? Thread { get; set; }
        public bool IsAlive { get; set; }
        public bool IsExecuting { get; set; }
        public bool ShouldStop { get; set; }
        public bool ReplacementSpawned { get; set; }
        public DateTime? CurrentTaskStartedAtUtc { get; set; }
    }
}

public readonly record struct PoolSnapshot(int QueueLength, int ActiveWorkers, int BusyWorkers, int IdleWorkers);

