namespace PersistentJobs;

public record DeferredException(string Name, string Message, DateTime Raised);
