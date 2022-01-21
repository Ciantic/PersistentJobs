namespace PersistentJobs;

public class DeferredTask<Output>
{
    public Guid Id { get; }

    public DeferredTask(Guid taskId)
    {
        Id = taskId;
    }
}
