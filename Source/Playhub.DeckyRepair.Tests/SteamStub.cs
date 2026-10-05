namespace Playhub.Services;
public sealed class SteamService
{
    public Task RestartSteamAsync() => throw new InvalidOperationException("The isolated repair fixture cannot restart Steam.");
}
