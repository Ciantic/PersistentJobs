using Microsoft.EntityFrameworkCore;

namespace PersistentJobs;

public class DeferredTask<Output>
{
    public Guid Id { get; }

    public enum Status
    {
        Queued,
        Running,
        Completed
    }

    public DeferredTask(Guid taskId)
    {
        Id = taskId;
    }

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

    async public Task<bool> Cancel(DbContext context)
    {
        // TODO: Exceptions: CompletedAlready
        throw new NotImplementedException();
    }

    async public Task<Status> GetStatus(DbContext context)
    {
        throw new NotImplementedException();
    }

    [Serializable]
    public class ObjectNotFoundException : Exception
    {
        public ObjectNotFoundException() { }
    }
}
