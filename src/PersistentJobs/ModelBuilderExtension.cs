using Microsoft.EntityFrameworkCore;

namespace PersistentJobs;

public static class ModelBuilderExtension
{
    public static void AddPersistentJobs(this ModelBuilder modelBuilder)
    {
        DeferredJob.ConfigureModelBuilder(modelBuilder);
        DeferredJobException.ConfigureModelBuilder(modelBuilder);
        CronJob.ConfigureModelBuilder(modelBuilder);
    }
}
