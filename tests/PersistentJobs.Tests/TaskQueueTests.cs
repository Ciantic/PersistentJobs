using System;
using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Linq;
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
        var source = t.Queue(
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
        var c1 = t.Queue(
            async (cancel) =>
            {
                await Task.Delay(80, cancel);
                n += 1;
            }
        );
        var c2 = t.Queue(
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
        var c1 = t.Queue(
            async (cancel) =>
            {
                await Task.Delay(80, cancel);
                n += 1;
            }
        );
        var c2 = t.Queue(
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
        Assert.Equal(2, t.GetRunningCount());
        Assert.False(c1.IsCancellationRequested);
        Assert.False(c2.IsCancellationRequested);
        t.Cancel();
        Assert.True(c1.IsCancellationRequested);
        Assert.True(c2.IsCancellationRequested);

        // Cancellation should take effect shortly
        await Task.Delay(10);
        Assert.Equal(0, t.GetRunningCount());

        // Wait for queue to empty
        await t.Process();

        // The n+=1 did not run
        Assert.Equal(0, n);
    }

    [Fact]
    public async Task TestQueueLength()
    {
        var t = new TaskQueue(maxQueueLength: 2);
        t.Queue(
            async () =>
            {
                await Task.Delay(40);
            }
        );
        t.Queue(
            async () =>
            {
                await Task.Delay(40);
            }
        );
        Assert.Throws<TaskQueue.QueueLimitReachedException>(
            () =>
                t.Queue(
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
        t.Queue(
            async () =>
            {
                await Task.Delay(40);
                n++;
            }
        );
        t.Queue(
            async () =>
            {
                await Task.Delay(50);
                n++;
            }
        );
        t.Queue(
            async () =>
            {
                await Task.Delay(60);
                n++;
            }
        );
        t.Queue(
            async () =>
            {
                await Task.Delay(70);
                n++;
            }
        );

        // Following are queued and will be run as above tasks complete
        // Task delay for the first must be 40 because 40 + 40 > 70
        t.Queue(
            async () =>
            {
                await Task.Delay(40);
                n++;
            }
        );
        t.Queue(
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
        Assert.Equal(4, t.GetRunningCount());

        await t.Process();

        // Queue and running tasks should now have ran to completion
        Assert.Equal(0, t.GetRunningCount());
        Assert.Equal(0, t.GetQueueCount());
        Assert.Equal(6, n);
    }

    [Fact]
    public async Task TestEmptyRun()
    {
        var t = new TaskQueue(maxParallelizationCount: 4);
        await t.Process();
    }
}
