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
    private DateTime? Started { get; set; } = null;
    private DateTime? Ended { get; set; } = null;
    private TimeSpan TimeLimit { get; set; } = TimeSpan.FromMinutes(30);
    private Guid IdempotencyKey { get; set; } = Guid.NewGuid();

    internal static void ConfigureModelBuilder(ModelBuilder modelBuilder)
    {
        var model = modelBuilder.Entity<PersistentJob>();
        model.Property(p => p.Id);
        model.Property(p => p.MethodName);
        model.Property(p => p.InputJson);
        model.Property(p => p.OutputJson);
        model.Property(p => p.Created);
        model.Property(p => p.Started);
        model.Property(p => p.Ended);
        model.Property(p => p.TimeLimit);
        model.Property(p => p.IdempotencyKey).IsConcurrencyToken();
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

        async static internal Task<List<PersistentJob>> GetUnstarted(DbContext context)
        {
            // Get all unstarted work items
            return await context.Set<PersistentJob>().Where(p => p.Started == null).ToListAsync();
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

    async internal Task<object?> Start(DbContext context, Type inputType)
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

        Started = DateTime.UtcNow;
        IdempotencyKey = Guid.NewGuid();
        await context.SaveChangesAsync();
        return inputObject;
    }

    async internal Task<object?> Complete(DbContext context, object outputValue)
    {
        OutputJson = JsonSerializer.Serialize(outputValue);
        IdempotencyKey = Guid.NewGuid();
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
