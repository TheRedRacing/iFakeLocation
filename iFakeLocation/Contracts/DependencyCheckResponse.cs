namespace iFakeLocation.Contracts;

public sealed record DependencyCheckResponse(bool HasDependencies, string IosVersion);
