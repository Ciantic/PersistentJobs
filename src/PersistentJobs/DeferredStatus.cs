namespace PersistentJobs;

public enum DeferredStatus
{
    Queued,
    Succeeded,
    Failed,
    Canceled,
    Waiting
}
