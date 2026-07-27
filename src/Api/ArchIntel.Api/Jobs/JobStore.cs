using System.Collections.Concurrent;

namespace ArchIntel.Api.Jobs;

/// <summary>In-memory Pending -> Running -> Completed|Failed job table (05-rest-api.md Section
/// 3.6). Registered as a singleton; JobRecord is immutable so updates are just dictionary
/// replacements — safe under the concurrent reads GET /jobs/{id} does against a job the
/// background task is still mutating.</summary>
public sealed class JobStore
{
    private readonly ConcurrentDictionary<string, JobRecord> _jobs = new();

    public JobRecord Create()
    {
        var jobId = $"job_{Guid.NewGuid():N}"[..12];
        var record = new JobRecord { JobId = jobId, Status = JobStatus.Pending };
        _jobs[jobId] = record;
        return record;
    }

    public JobRecord? Get(string jobId) => _jobs.GetValueOrDefault(jobId);

    public void Update(JobRecord updated) => _jobs[updated.JobId] = updated;
}
