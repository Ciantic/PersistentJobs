namespace PersistentJobs;

/// <summary>
/// Utility to generate a deferred version for the decorated method
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class CreateDeferredAttribute : JobAttribute { }
