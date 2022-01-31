namespace PersistentJobs;

public class DeferredOptions
{
    public TimeSpan? TimeLimit { get; set; } = null;
    public TimeSpan? WaitBetweenAttempts { get; set; } = null;
    public DateTime? AttemptAfter { get; set; } = null;
    public uint? MaxAttempts { get; set; } = null;
}
