using Microsoft.EntityFrameworkCore;

namespace PersistentJobs;

public static class ModelBuilderExtension
{
    public static void AddPersistentJobs(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PersistentJob>().Property(p => p.IdempotencyKey).IsConcurrencyToken();
    }
}
