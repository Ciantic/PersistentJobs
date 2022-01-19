using System.Data;
using System.Reflection;
using System.Reflection.Metadata.Ecma335;
using System.Security.AccessControl;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.DependencyInjection;

namespace PersistentJobs;

public class PersistentJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string AssemblyName { get; set; } = "";
    public string ClassName { get; set; } = "";
    public string MethodName { get; set; } = "";
    public string InputJson { get; set; } = "";
    public string? OutputJson { get; set; } = null;
    public DateTime Created { get; set; } = DateTime.UtcNow;
    public DateTime? Started { get; set; } = null;
    public DateTime? Ended { get; set; } = null;
    public TimeSpan TimeLimit { get; set; } = TimeSpan.FromMinutes(30);
    public Guid IdempotencyKey { get; set; } = Guid.NewGuid();

    private MethodInfo GetMethodInfo()
    {
        var method = Assembly.Load(AssemblyName).GetType(ClassName)?.GetMethod(MethodName);

        if (method == null)
        {
            // TODO: This failure is pretty bad, do something to log it
            throw new Exception("Method not found");
        }

        return method;
    }

    async public Task<object?> Execute(DbContext context, IServiceProvider? serviceProvider = null)
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

        var method = GetMethodInfo();
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


}
