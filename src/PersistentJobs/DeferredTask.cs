using Microsoft.EntityFrameworkCore;

namespace PersistentJobs;

public class DeferredTask<Output>
{
    public Guid Id { get; }

    public DeferredTask(Guid taskId)
    {
        Id = taskId;
    }

    async public Task<Output?> GetOutput(DbContext context)
    {
        return await PersistentJob.Repository.GetOutputById<Output>(context, Id);
    }
}
