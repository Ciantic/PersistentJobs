# PersistentJobs

Does not work yet, don't try.

## Usage with source generator attribute `CreateDeferred`

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
public class Worker
{
    public static Task<Deferred> SendEmailDeferred(string input, DbContext dbContext) {
        // Code and method generated

        // Returns `Deferred` which allows to query is it ready? What is the
        // output value? What are the exceptions? It does not allow to await
        // for the task to finish.
    }

}
```

Generated method stores the call to the database, in effect deferring it's execution until the `DeferredQueue` executes it.

It's notable that it omits the `IEmailSender` and other paramters in original method, because it replaces the function with a version which just stores the task in the database.

## Usage without source generator

It's also rather easy to use deferred execution without the source generator.

One only need to call `DeferredQueue.Enqueue` with method delegate having attribute `DeferredAttribute`, e.g.

```C#
public partial class Worker
{
    [Deferred]
    private static Task SendEmail(string input, IEmailSender sender)
    {
        // Your code...
        return Task.CompletedTask;
    }

    public static Task<Deferred> SendEmailDeferred(string input, DbContext dbContext) {
        await DeferredQueue.Enqueue(dbContext, SendEmail, input);
    }

}
```

## Notes

-   Stream success or exceptions from `Deferred`?
-   Channel for incoming Deferred tasks instead of polling?
