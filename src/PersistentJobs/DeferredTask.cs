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
        var output = await context
            .Set<PersistentJob>()
            .Where(p => p.Id == Id && p.OutputJson != null)
            .FirstOrDefaultAsync();
        if (output == null)
        {
            return default;
        }
        return JsonSerializer.Deserialize<Output>(output.OutputJson!);
    }
}
