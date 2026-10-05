namespace Playhub.Shared;

public sealed record UwpSessionIdentity(uint WrapperPid, long WrapperBirth, uint GamePid, long GameBirth,
    string Aumid, string PackageFamily, string Executable)
{
    public static string PipeName(uint pid, long birth) => $"Playhub.UwpIdentity.{pid}.{birth:X16}";
}
