using System.Data;
using System.Dynamic;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace PersistentJobs;

internal class CronJob
{
    internal Guid Id { get; set; } = Guid.NewGuid();
    internal string MethodName { get; set; } = "";
    private DateTime Created { get; set; } = DateTime.UtcNow;
    private DeferredJob? Current { get; set; } = null;
    private Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
    public int? Minute { get; set; } = null;
    public int? Hour { get; set; } = null;
    public int? Day { get; set; } = null;
    public int? Month { get; set; } = null;
    public int? DayOfWeek { get; set; } = null;

    private CronJob() { }

    internal static void ConfigureModelBuilder(ModelBuilder modelBuilder)
    {
        var model = modelBuilder.Entity<CronJob>();
        model.Property(p => p.Id);
        model.Property(p => p.MethodName);
        model.Property(p => p.Created);
        model.HasOne(p => p.Current);
        model.Property(p => p.ConcurrencyStamp).IsConcurrencyToken();
    }

    static internal class Repository
    {
    }

    static internal CronJob CreateFromMethod()
    {
        throw new NotImplementedException();
    }
}
