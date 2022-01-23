using System.Text.Json;
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
        var output = await PersistentJob.Repository.GetOutputById(context, Id);
        if (output == null)
        {
            return default;
        }
        return JsonSerializer.Deserialize<Output>(output);
    }
}
