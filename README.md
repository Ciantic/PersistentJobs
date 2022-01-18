# PersistentJobs

Does not work yet, don't try.

##

For given it a static method with returning Task, e.g.

```C#
public partial class Worker
{
    [Job]
    private static Task _ExampleJob(int input, DbContext dbContext)
    {
        // Your code...
        return Task.CompletedTask;
    }
}
```

It generates corresponding public method for invoking persistent Job:

```c#
public partial class Worker
{
    public static PersistentTask ExampleJob(int input)
    {
        // Returns persistent task that allows to query is it ready? What is the output value?
    }
}
```

as well as an IJob:

```C#
public class ExampleJob : IJob<int, bool>
{
    public Task<IJobRunning<bool>> StartAsync(int input)
    {
        // Your code...
    }
}
```
