using HiveLog.Api.Features.Retention;
using Microsoft.Extensions.Options;

namespace HiveLog.Api.Features.Admin;

/// <summary>
/// Holds mutable runtime retention settings that can be updated via /admin/retention
/// without requiring a server restart.
///
/// Initialized from RetentionOptions at startup.
/// App maps to InfoRetentionDays, Agent maps to DebugTraceRetentionDays.
/// </summary>
public sealed class RuntimeRetentionService
{
    private int _appDays;
    private int _agentDays;
    private int _auditDays;

    public RuntimeRetentionService(IOptions<RetentionOptions> opts)
    {
        _appDays = opts.Value.InfoRetentionDays;
        _agentDays = opts.Value.DebugTraceRetentionDays;
        _auditDays = opts.Value.AuditRetentionDays;
    }

    public int AppDays => Volatile.Read(ref _appDays);
    public int AgentDays => Volatile.Read(ref _agentDays);
    public int AuditDays => Volatile.Read(ref _auditDays);

    public void Update(int? appDays, int? agentDays, int? auditDays)
    {
        if (appDays.HasValue) Volatile.Write(ref _appDays, appDays.Value);
        if (agentDays.HasValue) Volatile.Write(ref _agentDays, agentDays.Value);
        if (auditDays.HasValue) Volatile.Write(ref _auditDays, auditDays.Value);
    }
}
