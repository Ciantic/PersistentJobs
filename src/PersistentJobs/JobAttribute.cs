namespace PersistentJobs;

[AttributeUsage(AttributeTargets.Method)]
public class JobAttribute : Attribute { }

// TODO: Parameter to control wether input is used or not?
