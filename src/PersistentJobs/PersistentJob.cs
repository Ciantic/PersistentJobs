using System.ComponentModel.DataAnnotations.Schema;
using System.Data;
using System.Diagnostics.Metrics;
using System.Reflection;
using System.Reflection.Metadata.Ecma335;
using System.Security.AccessControl;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking.Internal;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace PersistentJobs;

internal class PersistentJob
{
    private Guid Id { get; set; } = Guid.NewGuid();

    // private string AssemblyName { get; set; } = "";
    // private string ClassName { get; set; } = "";
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
        async static internal Task<string?> GetOutputById(DbContext context, Guid id)
        {
            return (
                await context
                    .Set<PersistentJob>()
                    .Where(p => p.Id == id && p.OutputJson != null)
                    .FirstOrDefaultAsync()
            )?.OutputJson;
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

    async internal Task Start(DbContext context)
    {
        Started = DateTime.UtcNow;
        IdempotencyKey = Guid.NewGuid();
        try
        {
            await context.SaveChangesAsync();
        }
        catch (DBConcurrencyException) { }
    }

    async internal Task<object?> Execute(
        DbContext context,
        MethodInfo method,
        IServiceProvider? serviceProvider = null
    )
    {
        // var method = jobService.GetMethod(MethodName);

        // TODO: input and services should be cached on the JobService:
        var parameters = method.GetParameters();

        // Get input parameter
        var inputPar = parameters.First();
        if (inputPar.Name != "input")
        {
            throw new Exception("First parameter is required to be `input`");
        }
        var inputValue = JsonSerializer.Deserialize(InputJson, inputPar.ParameterType);
        var invokeParams = new List<object?>() { inputValue };

        // Get service parameters
        if (serviceProvider != null)
        {
            var serviceTypes = parameters.Skip(1).ToArray();
            var services = serviceTypes.Select(
                p => serviceProvider.GetRequiredService(p.ParameterType)
            );
            invokeParams.AddRange(services);
        }

        // Execute and store to OutputJson, some reason Task<_> is not castable to Task<object>
        Task outputTask = (Task)method.Invoke(null, invokeParams.ToArray())!;
        await outputTask.ConfigureAwait(false);

        // TODO: Maybe the calling function could provide accurate type here?
        var outputValue = ((dynamic)outputTask).Result;

        OutputJson = JsonSerializer.Serialize(outputValue);
        IdempotencyKey = Guid.NewGuid();
        var changed = await context.SaveChangesAsync();
        return outputValue;
    }

    static private PersistentJob CreateFromMethod(Delegate d, object input)
    {
        var method = d.GetMethodInfo();
        var assemblyName = method.DeclaringType?.Assembly.GetName().Name;
        var className = method.DeclaringType?.FullName;
        var methodName = method.Name;
        if (assemblyName == null)
        {
            throw new ArgumentException("Assembly not determined");
        }
        if (className == null)
        {
            throw new ArgumentException("Class not determined");
        }
        return new PersistentJob()
        {
            // AssemblyName = assemblyName,
            // ClassName = className,
            MethodName = methodName,
            InputJson = JsonSerializer.Serialize(input)
        };
    }
}
