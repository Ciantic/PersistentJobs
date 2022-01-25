using System.Data;
using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace PersistentJobs;

internal class PersistentJob
{
    private Guid Id { get; set; } = Guid.NewGuid();
    internal string MethodName { get; set; } = "";
    private string InputJson { get; set; } = "";
    private string? OutputJson { get; set; } = null;
    private DateTime Created { get; set; } = DateTime.UtcNow;
    private DateTime? Queued { get; set; } = null;
    private DateTime? Completed { get; set; } = null;
    private TimeSpan TimeLimit { get; set; } = TimeSpan.FromMinutes(30);
    private uint Retry { get; set; } = 0;
    private uint MaxRetries { get; set; } = 0;
    private Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    private PersistentJob() { }

    internal static void ConfigureModelBuilder(ModelBuilder modelBuilder)
    {
        var model = modelBuilder.Entity<PersistentJob>();
        model.Property(p => p.Id);
        model.Property(p => p.MethodName);
        model.Property(p => p.InputJson);
        model.Property(p => p.OutputJson);
        model.Property(p => p.Created);
        model.Property(p => p.Queued);
        model.Property(p => p.Completed);
        model.Property(p => p.TimeLimit);
        model.Property(p => p.ConcurrencyStamp).IsConcurrencyToken();
    }

    internal static class Repository
    {
        async static internal Task<Output?> GetOutputById<Output>(DbContext context, Guid id)
        {
            var json = (
                await context
                    .Set<PersistentJob>()
                    .Where(p => p.Id == id && p.OutputJson != null)
                    .FirstOrDefaultAsync()
            )?.OutputJson;
            if (json == null)
            {
                return default;
            }
            return JsonSerializer.Deserialize<Output>(json);
        }

        async static internal Task<List<PersistentJob>> GetAvailable(DbContext context)
        {
            // Get all unstarted work items
            return await context.Set<PersistentJob>().Where(p => p.Queued == null).ToListAsync();
        }

        internal async static Task<DeferredTask<O>> Insert<O>(
            DbContext context,
            Delegate method,
            object input
        )
        {
            var job = CreateFromMethod(method, input);
            await context.Set<PersistentJob>().AddAsync(job);
            return new DeferredTask<O>(job.Id);
        }
    }

    async internal Task<object?> Queue(DbContext context, Type inputType)
    {
        object? inputObject;
        try
        {
            inputObject = JsonSerializer.Deserialize(InputJson, inputType);
        }
        catch (JsonException)
        {
            throw new ArgumentException(
                "Unable to serialize to given input type",
                nameof(inputType)
            );
        }

        Queued = DateTime.UtcNow;
        ConcurrencyStamp = Guid.NewGuid();
        await context.SaveChangesAsync();
        return inputObject;
    }

    async internal Task<object?> Complete(DbContext context, object outputValue)
    {
        OutputJson = JsonSerializer.Serialize(outputValue);
        Completed = DateTime.UtcNow;
        ConcurrencyStamp = Guid.NewGuid();
        await context.SaveChangesAsync();
        return outputValue;
    }

    static private PersistentJob CreateFromMethod(Delegate methodDelegate, object input)
    {
        var method = methodDelegate.GetMethodInfo();
        var methodName = method.Name;
        return new PersistentJob()
        {
            MethodName = methodName,
            InputJson = JsonSerializer.Serialize(input)
        };
    }
}
