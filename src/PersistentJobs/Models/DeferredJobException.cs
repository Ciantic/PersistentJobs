using System.Data;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace PersistentJobs;

internal class DeferredJobException
{
    private Guid Id { get; set; } = Guid.NewGuid();
    internal DeferredJob PersistentJob { get; set; } = null!;
    internal DateTime Raised { get; set; } = DateTime.UtcNow;
    internal string Exception { get; set; } = "";
    internal string Message { get; set; } = "";
    internal string StackTrace { get; set; } = "";

    private DeferredJobException() { }

    internal static void ConfigureModelBuilder(ModelBuilder modelBuilder)
    {
        var model = modelBuilder.Entity<DeferredJobException>();
        model.Property(p => p.Id);
        model.HasOne(p => p.PersistentJob);
        model.Property(p => p.Exception);
        model.Property(p => p.Message);
        model.Property(p => p.StackTrace);
        model.Property(p => p.Raised);
    }

    static async internal Task Insert(DbContext context, DeferredJob job, Exception ex)
    {
        var exj = new DeferredJobException()
        {
            Raised = DateTime.UtcNow,
            Exception = ex.GetType().FullName ?? ex.ToString(),
            Message = ex.Message,
            StackTrace = ex.StackTrace ?? "",
            PersistentJob = job
        };
        await context.AddAsync(exj);
    }

    static async internal Task<DeferredJobException[]> GetAllForJob(
        DbContext context,
        DeferredJob job
    )
    {
        return await context
            .Set<DeferredJobException>()
            .Where(t => t.PersistentJob == job)
            .OrderBy(p => p.Raised)
            .ToArrayAsync();
    }
}
