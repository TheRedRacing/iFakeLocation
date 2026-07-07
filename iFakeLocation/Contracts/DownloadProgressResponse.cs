namespace iFakeLocation.Contracts;

public sealed record DownloadProgressResponse(bool Done, string? FileName, double? ProgressPercent);
