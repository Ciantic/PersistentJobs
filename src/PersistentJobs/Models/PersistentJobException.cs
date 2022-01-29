using System.Data;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace PersistentJobs;

internal class PersistentJobException
{
    private Guid Id { get; set; } = Guid.NewGuid();
    internal PersistentJob PersistentJob { get; set; } = null!;
    internal DateTime Raised { get; set; } = DateTime.UtcNow;
    internal string Exception { get; set; } = "";
    internal string Message { get; set; } = "";
    internal string StackTrace { get; set; } = "";

    private PersistentJobException() { }

    internal static void ConfigureModelBuilder(ModelBuilder modelBuilder)
    {
        var model = modelBuilder.Entity<PersistentJobException>();
        model.Property(p => p.Id);
        model.HasOne(p => p.PersistentJob);
        model.Property(p => p.Exception);
        model.Property(p => p.Message);
        model.Property(p => p.StackTrace);
        model.Property(p => p.Raised);
    }

    static async internal Task Insert(DbContext context, PersistentJob job, Exception ex)
    {
        var exj = new PersistentJobException()
        {
            Raised = DateTime.UtcNow,
            Exception = ex.GetType().FullName ?? ex.ToString(),
            Message = ex.Message,
            StackTrace = ex.StackTrace ?? "",
            PersistentJob = job
        };
        await context.AddAsync(exj);
    }

    static async internal Task<PersistentJobException[]> GetAllForJob(
        DbContext context,
        PersistentJob job
    )
    {
        return await context
            .Set<PersistentJobException>()
            .Where(t => t.PersistentJob == job)
            .OrderBy(p => p.Raised)
            .ToArrayAsync();
    }
}
