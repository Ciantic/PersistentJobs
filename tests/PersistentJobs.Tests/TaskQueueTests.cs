using System;
using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.ValueGeneration;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestPlatform.Utilities;
using PersistentJobs;
using Xunit;
using Xunit.Sdk;

public class TaskQueueTests
{
    [Fact]
    public async Task TestCancellation()
    {
        var n = 0;
        var t = new TaskQueue();
        var source = t.Enqueue(
            async (cancel) =>
            {
                await Task.Delay(80, cancel);
                n += 1;
            }
        );

        // Start processing the queue
        t.ProcessBackground();

        // Cancel the task after 40 ms
        await Task.Delay(40);
        source.Cancel();

        // Wait for queue to empty
        await t.Process();

        // The n+=1 did not run
        Assert.Equal(0, n);
    }

    [Fact]
    public void TestCancellationQueued()
    {
        var n = 0;
        var t = new TaskQueue();
        var c1 = t.Enqueue(
            async (cancel) =>
            {
                await Task.Delay(80, cancel);
                n += 1;
            }
        );
        var c2 = t.Enqueue(
            async (cancel) =>
            {
                await Task.Delay(120, cancel);
                n += 1;
            }
        );

        Assert.False(c1.IsCancellationRequested);
        Assert.False(c2.IsCancellationRequested);
        t.Cancel();
        Assert.True(c1.IsCancellationRequested);
        Assert.True(c2.IsCancellationRequested);
    }

    [Fact]
    public async Task TestCancellationRunningTasks()
    {
        var n = 0;
        var t = new TaskQueue();
        var c1 = t.Enqueue(
            async (cancel) =>
            {
                await Task.Delay(80, cancel);
                n += 1;
            }
        );
        var c2 = t.Enqueue(
            async (cancel) =>
            {
                await Task.Delay(120, cancel);
                n += 1;
            }
        );

        // Start processing the queue
        t.ProcessBackground();

        // Cancel the task after 40 ms
        await Task.Delay(40);
        Assert.Equal(2, t.RunningCount);
        Assert.False(c1.IsCancellationRequested);
        Assert.False(c2.IsCancellationRequested);
        t.Cancel();
        Assert.True(c1.IsCancellationRequested);
        Assert.True(c2.IsCancellationRequested);

        // Cancellation should take effect shortly
        await Task.Delay(10);
        Assert.Equal(0, t.RunningCount);

        // Wait for queue to empty
        await t.Process();

        // The n+=1 did not run
        Assert.Equal(0, n);
    }

    [Fact]
    public async Task TestQueueLength()
    {
        var t = new TaskQueue(maxQueueLength: 2);
        t.Enqueue(
            async () =>
            {
                await Task.Delay(40);
            }
        );
        t.Enqueue(
            async () =>
            {
                await Task.Delay(40);
            }
        );
        Assert.Throws<TaskQueue.QueueLimitReachedException>(
            () =>
                t.Enqueue(
                    async () =>
                    {
                        await Task.Delay(40);
                    }
                )
        );
        await t.Process();
    }

    [Fact]
    public async Task TestMaxParallelization()
    {
        var t = new TaskQueue(maxParallelizationCount: 4);
        var n = 0;
        // Sequential delays should ensure that tasks complete in order for
        // `n` to grow linearly
        t.Enqueue(
            async () =>
            {
                await Task.Delay(40);
                n++;
            }
        );
        t.Enqueue(
            async () =>
            {
                await Task.Delay(50);
                n++;
            }
        );
        t.Enqueue(
            async () =>
            {
                await Task.Delay(60);
                n++;
            }
        );
        t.Enqueue(
            async () =>
            {
                await Task.Delay(70);
                n++;
            }
        );

        // Following are queued and will be run as above tasks complete
        // Task delay for the first must be 40 because 40 + 40 > 70
        t.Enqueue(
            async () =>
            {
                await Task.Delay(40);
                n++;
            }
        );
        t.Enqueue(
            async () =>
            {
                await Task.Delay(50);
                n++;
            }
        );

        // Intentionally not awaited, starts tasks asynchronously
        t.ProcessBackground();

        // Wait for tasks to start
        await Task.Delay(10);

        // Tasks should now be running
        Assert.Equal(4, t.RunningCount);

        await t.Process();

        // Queue and running tasks should now have ran to completion
        Assert.Equal(0, t.RunningCount);
        Assert.Equal(0, t.Count);
        Assert.Equal(6, n);

        /*
        This happened once, when under higher load, maybe it's okay?:
        
        Failed TaskQueueTests.TestMaxParallelization [127 ms]
        Error Message: Assert.Equal() Failure
        Expected: 6
        Actual:   5
        */
    }

    [Fact]
    public async Task TestTimeLimit()
    {
        var t = new TaskQueue();
        var atom = 0;
        t.Enqueue(
                async (CancellationToken cancellationToken) =>
                {
                    await Task.Delay(40, cancellationToken);
                    atom = 1;
                }
            )
            .WithTimeLimit(TimeSpan.FromMilliseconds(10));
        await t.Process();
        Assert.Equal(0, atom);
    }

    [Fact]
    public async Task TestException()
    {
        var gotit = "";
        var t = new TaskQueue();
        t.Enqueue(
                (CancellationToken token) =>
                {
                    throw new Exception("What you did there?");
                }
            )
            .WithExceptionHandler(
                ex =>
                {
                    gotit = ex.Message;
                }
            );
        await t.Process();
        Assert.Equal("What you did there?", gotit);
    }

    [Fact]
    public async Task TestExceptionWithoutHandler()
    {
        var t = new TaskQueue();
        t.Enqueue(
            (CancellationToken token) =>
            {
                throw new Exception("What you did there?");
            }
        );
        await Assert.ThrowsAsync<AggregateException>(t.Process);
    }

    [Fact]
    public async Task TestEmptyRun()
    {
        var t = new TaskQueue(maxParallelizationCount: 4);
        await t.Process();
    }
}
