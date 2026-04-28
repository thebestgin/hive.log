namespace HiveLog.Api.Features.Admin.Models;

public sealed record RetentionResponse(int AppDays, int AgentDays, int AuditDays);
