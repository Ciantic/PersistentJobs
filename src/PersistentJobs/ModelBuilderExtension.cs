using Microsoft.EntityFrameworkCore;

namespace PersistentJobs;

public static class ModelBuilderExtension
{
    public static void AddPersistentJobs(this ModelBuilder modelBuilder)
    {
        PersistentJob.ConfigureModelBuilder(modelBuilder);
        PersistentJobException.ConfigureModelBuilder(modelBuilder);
        CronJob.ConfigureModelBuilder(modelBuilder);
    }
}
