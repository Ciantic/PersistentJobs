# PersistentJobs

Does not work yet, don't try.

## Use case examples

-   Email queue with retry mechanism and max parallelization
-   Resize images on background
-   Run cleanup commands on defined cron schedule
-   Check failed payments in subscription system

## Defined behavior

Two types of persistent jobs:

-   Deferred jobs
    -   Single method call
    -   Ability to retry
    -   Wait between failures
    -   Max parallelization per method
    -   Execution can delayed after certain timestamp
    -   Atomic: Can be queued within database transaction
    -   Ignores methods not defined in the executable
    -   Does not delete non-existing deferred methods
-   Cron jobs
    -   Uses crontabs Hour / Min / Day / Month / DayOfWeek format
    -   Cron job works by creating deferred jobs on demand
    -   All deferred jobs can be cron jobs
    -   Can be dedfined in code and manually in database
    -   Can be disabled from database
    -   Source defined cron jobs are recreated on start
    -   Ignores cron jobs not defined in the executable
    -   Does not delete non-existing cron jobs

## Usage with source generator attribute `CreateDeferred`

With a static method returning Task, e.g.

```C#
public partial class Worker
{
    [CreateDeferred]
    public static Task SendEmail(string input, IEmailSender sender)
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
    public static Task SendEmail(string input, IEmailSender sender)
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
