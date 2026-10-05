using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace GamingMode.Services;

public sealed class XboxShellBroker
{
    private readonly byte[] _token;
    private readonly object _gate = new();
    private readonly Func<bool> _shellPresent;
    private readonly Action _startShell;
    private readonly Func<long> _clock;
    private long? _lastStart;

    public XboxShellBroker(string directory, Func<bool> shellPresent, Action startShell, Func<long>? clock = null)
    {
        _shellPresent = shellPresent;
        _startShell = startShell;
        _clock = clock ?? (() => Environment.TickCount64);
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        _token = Encoding.ASCII.GetBytes(token);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "xbox-shell-token"), token);
    }

    public bool IsAuthorized(IPAddress? remoteAddress, string suppliedToken) =>
        remoteAddress is not null && IPAddress.IsLoopback(remoteAddress) &&
        CryptographicOperations.FixedTimeEquals(_token, Encoding.ASCII.GetBytes(suppliedToken));

    public bool RequestShell(IPAddress? remoteAddress, string suppliedToken)
    {
        if (!IsAuthorized(remoteAddress, suppliedToken)) return false;
        lock (_gate)
        {
            if (_shellPresent() || _lastStart.HasValue && _clock() - _lastStart.Value < 15000) return true;
            _startShell();
            _lastStart = _clock();
            return true;
        }
    }
}
