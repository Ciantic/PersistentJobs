using System.Security.Cryptography.X509Certificates;
using Microsoft.EntityFrameworkCore;

namespace PersistentJobs;

public class Deferred
{
    public Guid Id { get; }

    public record DeferredTaskException(string Name, string Message, DateTime Raised);

    public Deferred(Guid taskId)
    {
        Id = taskId;
    }

    async public Task<DeferredTaskException[]> GetExceptions(DbContext context)
    {
        try
        {
            var job = await DeferredJob.Repository.Get(context, Id);
            var exceptions = await job.GetExceptions(context);
            return exceptions
                .Select(p => new DeferredTaskException(p.Exception, p.Message, p.Raised))
                .ToArray();
        }
        catch (DeferredJob.Repository.ObjectNotFoundException)
        {
            throw new ObjectNotFoundException();
        }
    }

    // TODO: async public Task<bool> Cancel(DbContext context)
    // {
    //     // TODO: Exceptions: CompletedAlready
    //     throw new NotImplementedException();
    // }

    async public Task<DeferredStatus> GetStatus(DbContext context)
    {
        try
        {
            var job = await DeferredJob.Repository.Get(context, Id);
            return job.Status;
        }
        catch (DeferredJob.Repository.ObjectNotFoundException)
        {
            throw new ObjectNotFoundException();
        }
    }

    [Serializable]
    public class ObjectNotFoundException : Exception
    {
        public ObjectNotFoundException() { }
    }
}

public class Deferred<Output> : Deferred
{
    public Deferred(Guid taskId) : base(taskId) { }

    async public Task<Output> GetOutput(DbContext context)
    {
        // TODO: Exceptions: NotCompleted
        try
        {
            var output = await DeferredJob.Repository.GetCompletedOutput<Output>(context, Id);

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
        catch (DeferredJob.Repository.ObjectNotFoundException)
        {
            throw new ObjectNotFoundException();
        }
    }
}
