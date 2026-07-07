namespace iFakeLocation.Contracts;

public sealed record DeviceDto(string Name, string DisplayName, string Udid, bool IsNetwork);

public sealed record DevicesResponse(IReadOnlyList<DeviceDto> Devices);
