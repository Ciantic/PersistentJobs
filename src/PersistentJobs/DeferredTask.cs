using System.Security.Cryptography.X509Certificates;
using Microsoft.EntityFrameworkCore;

namespace PersistentJobs;

public class DeferredTask
{
    public Guid Id { get; }

    public enum Status
    {
        Queued,
        Running,
        Completed,
        Waiting
    }

    public record DeferredTaskException(string Name, string Message, DateTime Raised);

    public DeferredTask(Guid taskId)
    {
        Id = taskId;
    }

    async public Task<DeferredTaskException[]> GetExceptions(DbContext context)
    {
        try
        {
            var job = await PersistentJob.Repository.Get(context, Id);
            var exceptions = await job.GetExceptions(context);
            return exceptions
                .Select(p => new DeferredTaskException(p.Exception, p.Message, p.Raised))
                .ToArray();
        }
        catch (PersistentJob.Repository.ObjectNotFoundException)
        {
            throw new ObjectNotFoundException();
        }
    }

    async public Task<bool> Cancel(DbContext context)
    {
        // TODO: Exceptions: CompletedAlready
        throw new NotImplementedException();
    }

    async public Task<Status> GetStatus(DbContext context)
    {
        var job = await PersistentJob.Repository.Get(context, Id);
        if (job.IsCompleted())
        {
            return Status.Completed;
        }
        if (job.IsQueued() && !job.IsCompleted())
        {
            return Status.Running;
        }
        if (job.IsQueued())
        {
            return Status.Queued;
        }
        return Status.Waiting;
    }

    [Serializable]
    public class ObjectNotFoundException : Exception
    {
        public ObjectNotFoundException() { }
    }
}

public class DeferredTask<Output> : DeferredTask
{
    public DeferredTask(Guid taskId) : base(taskId) { }

    async public Task<Output> GetOutput(DbContext context)
    {
        // TODO: Exceptions: NotCompleted
        try
        {
            var output = await PersistentJob.Repository.GetCompletedOutput<Output>(context, Id);

            // If output *is* nullable, then returning null is fine
            if (Nullable.GetUnderlyingType(typeof(Output)) != null)
            {
                return output!;
            }
            else
            {
                // Output type is not nullable, but it contains null, this is violation
                if (output == null)
                {
                    throw new Exception(
                        $"Output type `{typeof(Output).FullName}` should not be null"
                    );
                }
            }
            return output;
        }
        catch (PersistentJob.Repository.ObjectNotFoundException)
        {
            throw new ObjectNotFoundException();
        }
    }
}
