# PersistentJobs

Does not work yet, don't try.

##

With a static method returning Task, e.g.

```C#
public partial class Worker
{
    [CreateDeferred]
    private static Task SendEmail(string input, IEmailSender sender)
    {
        // Your code...
        return Task.CompletedTask;
    }
}
```

It generates corresponding public method with `Deferred` suffix:

```c#
public partial class Worker
{
    public static DeferredTask SendEmailDeferred(string input, DbContext dbContext)
    {
        // Returns deferred task that allows to query is it ready? What is the output value? It does not allow to await for the task to finish.
    }
}
```

Generated method stores the call to the database, in effect deferring it's execution until the `JobService` executes it.

It's notable that it omits the `IEmailSender`, because it replaces the function with a version which just stores the task in the database.

<!--
as well as an IJob:

```C#
public class Example : IJob<int, bool>
{
    public Task<IJobRunning<bool>> StartAsync(int input)
    {
        // Your code...
    }
}
``` -->
