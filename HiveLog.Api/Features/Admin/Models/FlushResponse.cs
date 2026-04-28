namespace HiveLog.Api.Features.Admin.Models;

public sealed record FlushResponse(int EntriesFlushed, double ElapsedMs);
