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
    private string Exception { get; set; } = "";
    private string Message { get; set; } = "";
    private DateTime Raised { get; set; } = DateTime.UtcNow;

    private PersistentJobException() { }

    internal static void ConfigureModelBuilder(ModelBuilder modelBuilder)
    {
        var model = modelBuilder.Entity<PersistentJobException>();
        model.Property(p => p.Id);
        model.HasOne(p => p.PersistentJob).WithMany(p => p.Exceptions);
        model.Property(p => p.Exception);
        model.Property(p => p.Message);
        model.Property(p => p.Raised);
    }

    static async internal Task CreateFromException(
        DbContext context,
        PersistentJob job,
        Exception ex
    )
    {
        var exj = new PersistentJobException()
        {
            Raised = DateTime.UtcNow,
            Exception = ex.GetType().FullName,
            Message = ex.Message,
            PersistentJob = job
        };
        context.Add(exj);
        await context.SaveChangesAsync();
    }
}
