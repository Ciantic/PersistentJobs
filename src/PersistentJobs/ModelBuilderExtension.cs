using Microsoft.EntityFrameworkCore;

namespace PersistentJobs;

public static class ModelBuilderExtension
{
    public static void AddPersistentJobs(this ModelBuilder modelBuilder)
    {
        var model = modelBuilder.Entity<PersistentJob>();
        model.Property(p => p.IdempotencyKey).IsConcurrencyToken();
        model.Property(p => p.Id);
        // model.Property(p => p.AssemblyName);
        // model.Property(p => p.ClassName);
        model.Property(p => p.Created);
        model.Property(p => p.MethodName);
        model.Property(p => p.InputJson);
        model.Property(p => p.OutputJson);
        model.Property(p => p.Started);
    }
}
