﻿using System.Data;
using System.Dynamic;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace PersistentJobs;

internal class PersistentJob
{
    internal Guid Id { get; set; } = Guid.NewGuid();
    internal string MethodName { get; set; } = "";
    private string InputJson { get; set; } = "";
    private string? OutputJson { get; set; } = null;
    private DateTime Created { get; set; } = DateTime.UtcNow;
    private DateTime? Queued { get; set; } = null;
    private DateTime? Completed { get; set; } = null;
    private TimeSpan TimeLimit { get; set; } = TimeSpan.FromMinutes(30);
    private uint Attempts { get; set; } = 0;
    private uint MaxAttempts { get; set; } = 1;
    private Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    private PersistentJob() { }

    internal static void ConfigureModelBuilder(ModelBuilder modelBuilder)
    {
        var model = modelBuilder.Entity<PersistentJob>();
        model.Property(p => p.Id);
        model.Property(p => p.MethodName);
        model.Property(p => p.InputJson);
        model.Property(p => p.OutputJson);
        model.Property(p => p.Created);
        model.Property(p => p.Queued);
        model.Property(p => p.Completed);
        model.Property(p => p.TimeLimit);
        model.Property(p => p.Attempts);
        model.Property(p => p.MaxAttempts);
        model.Property(p => p.ConcurrencyStamp).IsConcurrencyToken();
    }

    internal static class Repository
    {
        async static internal Task<PersistentJob> Get(DbContext context, Guid id)
        {
            var job = (
                await context.Set<PersistentJob>().Where(p => p.Id == id).FirstOrDefaultAsync()
            );
            if (job == null)
            {
                throw new ObjectNotFoundException();
            }
            return job;
        }

        async static internal Task<Output?> GetCompletedOutput<Output>(DbContext context, Guid id)
        {
            var json = (
                await context
                    .Set<PersistentJob>()
                    .Where(p => p.Id == id && p.Completed != null)
                    .FirstOrDefaultAsync()
            );
            if (json == null)
            {
                throw new ObjectNotFoundException();
            }
            if (json.OutputJson == null)
            {
                return default;
            }
            return JsonSerializer.Deserialize<Output>(json.OutputJson);
        }

        async static internal Task<List<PersistentJob>> GetAvailable(DbContext context)
        {
            // Get all unstarted work items
            return await context.Set<PersistentJob>().Where(p => p.Queued == null).ToListAsync();
        }

        internal async static Task<DeferredTask> Insert(
            DbContext context,
            Delegate method,
            object input
        )
        {
            var job = CreateFromMethod(method, input);
            await context.Set<PersistentJob>().AddAsync(job);
            return new DeferredTask(job.Id);
        }

        internal async static Task<DeferredTask<O>> Insert<O>(
            DbContext context,
            Delegate method,
            object input
        )
        {
            var job = CreateFromMethod(method, input);
            await context.Set<PersistentJob>().AddAsync(job);
            return new DeferredTask<O>(job.Id);
        }

        [Serializable]
        internal class ObjectNotFoundException : Exception
        {
            internal ObjectNotFoundException() { }
        }
    }

    async internal Task<PersistentJobException[]> GetExceptions(DbContext context)
    {
        return await PersistentJobException.GetAllForJob(context, this);
    }

    internal object? Queue(Type? inputType)
    {
        object? inputObject = null;
        if (Attempts >= MaxAttempts)
        {
            throw new InvalidOperationException(
                "Maximum retries exceeded, queueing is not allowed"
            );
        }

        if (inputType != null)
        {
            try
            {
                inputObject = JsonSerializer.Deserialize(InputJson, inputType);
            }
            catch (JsonException)
            {
                throw new ArgumentException(
                    "Unable to serialize to given input type",
                    nameof(inputType)
                );
            }
        }

        Attempts += 1;
        Queued = DateTime.UtcNow;
        ConcurrencyStamp = Guid.NewGuid();
        return inputObject;
    }

    internal void Complete(object outputValue)
    {
        if (Queued == null)
        {
            throw new InvalidOperationException("Complete does not work for non queued items");
        }
        OutputJson = JsonSerializer.Serialize(outputValue);
        Completed = DateTime.UtcNow;
        ConcurrencyStamp = Guid.NewGuid();
    }

    async internal Task InsertException(DbContext context, Exception exception)
    {
        if (Queued == null)
        {
            throw new InvalidOperationException("Only queued item can raise exceptions");
        }
        Queued = null;
        ConcurrencyStamp = Guid.NewGuid();
        await PersistentJobException.Insert(context, this, exception);
    }

    internal bool IsCompleted()
    {
        return Completed != null;
    }

    internal bool IsQueued()
    {
        return Queued != null;
    }

    static private PersistentJob CreateFromMethod(Delegate methodDelegate, object input)
    {
        var method = methodDelegate.GetMethodInfo();
        var methodName = method.Name;
        return new PersistentJob()
        {
            MethodName = methodName,
            InputJson = JsonSerializer.Serialize(input)
        };
    }
}