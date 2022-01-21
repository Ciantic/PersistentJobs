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

public class PersistentJob
{
    internal Guid Id { get; set; } = Guid.NewGuid();

    // internal string AssemblyName { get; set; } = "";
    // internal string ClassName { get; set; } = "";
    internal string MethodName { get; set; } = "";
    internal string InputJson { get; set; } = "";
    internal string? OutputJson { get; set; } = null;
    internal DateTime Created { get; set; } = DateTime.UtcNow;
    internal DateTime? Started { get; set; } = null;
    internal DateTime? Ended { get; set; } = null;
    internal TimeSpan TimeLimit { get; set; } = TimeSpan.FromMinutes(30);
    internal Guid IdempotencyKey { get; set; } = Guid.NewGuid();

    // private MethodInfo GetMethodInfo()
    // {
    //     var method = Assembly.Load(AssemblyName).GetType(ClassName)?.GetMethod(MethodName);

    //     if (method == null)
    //     {
    //         // TODO: This failure is pretty bad, do something to log it
    //         throw new Exception("Method not found");
    //     }

    //     return method;
    // }

    async internal Task<object?> Execute(
        DbContext context,
        MethodInfo method,
        IServiceProvider? serviceProvider = null
    )
    {
        Started = DateTime.UtcNow;
        try
        {
            await context.SaveChangesAsync();
        }
        catch (DBConcurrencyException)
        {
            return null;
            // pass
        }

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

        // Execute and store to OutputJson
        var outputValue = method.Invoke(null, invokeParams.ToArray());
        OutputJson = JsonSerializer.Serialize(outputValue);
        await context.SaveChangesAsync();

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

    public async static Task<DeferredTask<O>> Insert<O>(DbContext context, Delegate d, object input)
    {
        var job = CreateFromMethod(d, input);
        await context.Set<PersistentJob>().AddAsync(job);
        await context.SaveChangesAsync();
        return new DeferredTask<O>(job.Id);
    }

    public async static Task<bool> GotIt(DbContext context, Guid id)
    {
        await context.Set<PersistentJob>().Where(p => p.Id == id).FirstAsync();
        return true;
    }

    // public delegate object Dell(params object[] a);

    // static public PersistentJob CreateFromMethod2(Dell fun, object input)
    // {
    //     throw new NotImplementedException();
    // }

    /*
    async static public Task<PersistentJob> InsertJob(
        DbContext dbContext,
        string assemblyName,
        string className,
        string methodName,
        object inputValue
    )
    {
        throw new NotImplementedException();
    }

    async static public Task<PersistentJob> InsertJob2(
        DbContext dbContext,
        // Func<int, Task<int>> a,
        Delegate a,
        object inputValue
    )
    {
        var del = a.GetMethodInfo();
        if (del != null)
        {
            var aa = del.DeclaringType.Assembly.Location;
        }
        throw new NotImplementedException();
    }
    */


}
