namespace PersistentJobs;

[System.Serializable]
public class DeferredCanceledException : Exception
{
    public DeferredCanceledException() { }

    public DeferredCanceledException(string message) : base(message) { }
}
