using System.Collections.Concurrent;

namespace PersistentJobs;

internal class TaskQueue
{
    public class QueueLimitReachedException : Exception
    {
    }

    public record QueueItem(Func<CancellationToken, Task> Func)
    {
        internal CancellationTokenSource CancellationTokenSource { get; set; } = new();
        internal Action<Exception>? ExceptionHandler { get; set; }
        internal TimeSpan? TimeLimit { get; set; }
        internal Task? RunningTask { get; set; }

        public QueueItem WithExceptionHandler(Action<Exception> exceptionHandler)
        {
            ExceptionHandler = exceptionHandler;
            return this;
        }

        public QueueItem WithTimeLimit(TimeSpan timeLimit)
        {
            if (timeLimit.Ticks > 0)
            {
                TimeLimit = timeLimit;
            }
            return this;
        }

        internal Task Invoke()
        {
            if (TimeLimit is not null)
            {
                CancellationTokenSource.CancelAfter((int)TimeLimit.Value.TotalMilliseconds);
            }
            RunningTask = Task.Run(
                async () =>
                {
                    if (ExceptionHandler is not null)
                    {
                        try
                        {
                            await Func.Invoke(CancellationTokenSource.Token);
                        }
                        catch (Exception e)
                        {
                            ExceptionHandler(e);
                        }
                    }
                    else
                    {
                        await Func.Invoke(CancellationTokenSource.Token);
                    }
                },
                CancellationTokenSource.Token
            );
            return RunningTask;
        }

        public void Cancel()
        {
            CancellationTokenSource.Cancel();
        }

        public void Cancel(TimeSpan afterDelay)
        {
            CancellationTokenSource.CancelAfter(afterDelay);
        }

        public bool IsCancellationRequested
        {
            get { return CancellationTokenSource.IsCancellationRequested; }
        }
    }

    private readonly ConcurrentQueue<QueueItem> _processingQueue = new();
    private readonly ConcurrentDictionary<int, QueueItem> _runningTasks = new();
    private readonly int _maxParallelizationCount;
    private readonly int _maxQueueLength;
    private TaskCompletionSource<bool> _tscQueue = new();

    public TaskQueue(int? maxParallelizationCount = null, int? maxQueueLength = null)
    {
        _maxParallelizationCount = maxParallelizationCount ?? int.MaxValue;
        _maxQueueLength = maxQueueLength ?? int.MaxValue;
    }

    public QueueItem Enqueue(Func<Task> futureTask)
    {
        if (_processingQueue.Count >= _maxQueueLength)
        {
            throw new QueueLimitReachedException();
        }
        var queueItem = new QueueItem((c) => futureTask.Invoke());
        _processingQueue.Enqueue(queueItem);
        return queueItem;
    }

    public QueueItem Enqueue(Func<CancellationToken, Task> futureTask)
    {
        if (_processingQueue.Count >= _maxQueueLength)
        {
            throw new QueueLimitReachedException();
        }

        var queueItem = new QueueItem(futureTask);
        _processingQueue.Enqueue(queueItem);
        return queueItem;
    }

    public int Count
    {
        get { return _processingQueue.Count; }
    }

    public int RunningCount
    {
        get { return _runningTasks.Count; }
    }

    public void Cancel()
    {
        // Remove queued tasks, and cancel them
        while (_processingQueue.TryDequeue(out var item))
        {
            item.CancellationTokenSource.Cancel();
        }

        // Cancel all running tasks
        foreach (var (_, runningItem) in _runningTasks)
        {
            runningItem.CancellationTokenSource.Cancel();
        }
    }

    public void Cancel(TimeSpan delay)
    {
        // TODO: No tests yet, but it should work just as regular Cancel does.

        // Remove queued tasks, and cancel them
        while (_processingQueue.TryDequeue(out var item))
        {
            item.CancellationTokenSource.CancelAfter(delay);
        }

        // Clear the queue
        _processingQueue.Clear();

        // Cancel all running tasks
        foreach (var (_, runningItem) in _runningTasks)
        {
            runningItem.CancellationTokenSource.CancelAfter(delay);
        }
    }

    public async Task Process()
    {
        var t = _tscQueue.Task;
        var exceptions = StartTasks();
        await t;
        if (exceptions.Any())
        {
            throw new AggregateException(exceptions);
        }
    }

    public void ProcessBackground(Action<AggregateException>? exception = null)
    {
        Task.Run(Process)
            .ContinueWith(
                t =>
                {
                    // OnlyOnFaulted guarentees Task.Exception is not null
                    exception?.Invoke(t.Exception!);
                },
                TaskContinuationOptions.OnlyOnFaulted
            );
    }

    private IEnumerable<Exception> StartTasks()
    {
        var exceptions = new ConcurrentBag<Exception>();
        var startMaxCount = _maxParallelizationCount - _runningTasks.Count;
        for (int i = 0; i < startMaxCount; i++)
        {
            if (!_processingQueue.TryDequeue(out var tokenFutureTask))
            {
                // Queue is most likely empty
                break;
            }

            // Start and add to running tasks
            var t = tokenFutureTask.Invoke();
            if (!_runningTasks.TryAdd(t.GetHashCode(), tokenFutureTask))
            {
                throw new Exception("Should not happen, hash codes are unique");
            }

            // After the task is finished, remove it from running tasks
            t.ContinueWith(
                (t2) =>
                {
                    if (t2.Exception is not null)
                    {
                        foreach (var ex in t2.Exception.InnerExceptions)
                        {
                            exceptions.Add(ex);
                        }
                    }
                    if (!_runningTasks.TryRemove(t2.GetHashCode(), out var _temp))
                    {
                        throw new Exception("Should not happen, hash codes are unique");
                    }

                    // Continue the queue processing
                    foreach (var ex in StartTasks())
                    {
                        exceptions.Add(ex);
                    }
                }
            );
        }

        if (_processingQueue.IsEmpty && _runningTasks.IsEmpty)
        {
            // Interlocked.Exchange might not be necessary
            var _oldQueue = Interlocked.Exchange(ref _tscQueue, new TaskCompletionSource<bool>());
            _oldQueue.TrySetResult(true);
        }
        return exceptions;
    }
}
