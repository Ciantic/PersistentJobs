using Microsoft.EntityFrameworkCore.Internal;

namespace PersistentJobs;

[AttributeUsage(AttributeTargets.Method)]
public class CronAttribute : Attribute
{
    public int Minute { get; set; }
    public int Hour { get; set; } = -1;
    public int Day { get; set; } = -1;
    public int Month { get; set; } = -1;
    public int DayOfWeek { get; set; } = -1;
}
