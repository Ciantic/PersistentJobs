using System.Collections.Concurrent;
using System.Security.AccessControl;

namespace PersistentJobs;

internal class TaskQueue
{
    public class QueueLimitReachedException : Exception
    {
    }

    private readonly ConcurrentQueue<(CancellationTokenSource, Func<
            CancellationToken,
            Task
        >)> _processingQueue = new();
    private readonly ConcurrentDictionary<int, (Task, CancellationTokenSource)> _runningTasks =
        new();
    private readonly int _maxParallelizationCount;
    private readonly int _maxQueueLength;
    private TaskCompletionSource<bool> _tscQueue = new();

    public TaskQueue(int? maxParallelizationCount = null, int? maxQueueLength = null)
    {
        _maxParallelizationCount = maxParallelizationCount ?? int.MaxValue;
        _maxQueueLength = maxQueueLength ?? int.MaxValue;
    }

    public void Queue(Func<Task> futureTask)
    {
        if (_processingQueue.Count >= _maxQueueLength)
        {
            throw new QueueLimitReachedException();
        }
        _processingQueue.Enqueue((new CancellationTokenSource(), (c) => futureTask.Invoke()));
    }

    public CancellationTokenSource Queue(
        Func<CancellationToken, Task> futureTask,
        TimeSpan? timeLimit = null
    )
    {
        if (_processingQueue.Count >= _maxQueueLength)
        {
            throw new QueueLimitReachedException();
        }
        CancellationTokenSource cancelSource;
        var timeLimitMillis = timeLimit?.TotalMilliseconds ?? 0;
        if (timeLimitMillis != 0)
        {
            cancelSource = new CancellationTokenSource((int)timeLimitMillis);
        }
        else
        {
            cancelSource = new CancellationTokenSource();
        }

        _processingQueue.Enqueue((cancelSource, futureTask));
        return cancelSource;
    }

    public int GetQueueCount()
    {
        return _processingQueue.Count;
    }

    public int GetRunningCount()
    {
        return _runningTasks.Count;
    }

    public void Cancel()
    {
        // Remove queued tasks, and cancel them
        while (_processingQueue.TryDequeue(out var item))
        {
            item.Item1.Cancel();
        }

        // Cancel all running tasks
        foreach (var (_, (_, cancelSource)) in _runningTasks)
        {
            cancelSource.Cancel();
        }
    }

    public void Cancel(TimeSpan delay)
    {
        // TODO: No tests yet, but it should work just as regular Cancel does.

        // Remove queued tasks, and cancel them
        while (_processingQueue.TryDequeue(out var item))
        {
            item.Item1.CancelAfter(delay);
        }

        // Clear the queue
        _processingQueue.Clear();

        // Cancel all running tasks
        foreach (var (_, (_, cancelSource)) in _runningTasks)
        {
            cancelSource.CancelAfter(delay);
        }
    }

    public async Task Process()
    {
        var t = _tscQueue.Task;
        StartTasks();
        await t;
    }

    public void ProcessBackground(Action<Exception>? exception = null)
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

    private void StartTasks()
    {
        var startMaxCount = _maxParallelizationCount - _runningTasks.Count;
        for (int i = 0; i < startMaxCount; i++)
        {
            if (!_processingQueue.TryDequeue(out var tokenFutureTask))
            {
                // Queue is most likely empty
                break;
            }

            var (cancelSource, futureTask) = tokenFutureTask;
            var t = Task.Run(() => futureTask.Invoke(cancelSource.Token), cancelSource.Token);
            if (!_runningTasks.TryAdd(t.GetHashCode(), (t, cancelSource)))
            {
                throw new Exception("Should not happen, hash codes are unique");
            }

            t.ContinueWith(
                (t2) =>
                {
                    if (!_runningTasks.TryRemove(t2.GetHashCode(), out var _temp))
                    {
                        throw new Exception("Should not happen, hash codes are unique");
                    }

                    // Continue the queue processing
                    StartTasks();
                }
            );
        }

        if (_processingQueue.IsEmpty && _runningTasks.IsEmpty)
        {
            // Interlocked.Exchange might not be necessary
            var _oldQueue = Interlocked.Exchange(ref _tscQueue, new TaskCompletionSource<bool>());
            _oldQueue.TrySetResult(true);
        }
    }
}
