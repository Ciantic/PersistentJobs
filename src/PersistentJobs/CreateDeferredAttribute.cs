namespace PersistentJobs;

/// <summary>
/// Utility to generate a deferred version for the decorated method
///
/// Not to be used when generator is not wanted or used, use bare JobAttribute
/// in that case.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class CreateDeferredAttribute : JobAttribute { }
