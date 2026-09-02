using System.Net.Http;

namespace UpdateTests
{
    internal static class State
    {
        public static readonly string Root = Path.Combine(AppContext.BaseDirectory, "isolated-" + Guid.NewGuid().ToString("N"));
        public static Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Reply =
            (_, _) => throw new InvalidOperationException("External network access is forbidden in these tests.");
        public static void AssertPath(string path)
        {
            if (!Path.GetFullPath(path).StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Path outside test directory: " + path);
        }
    }
    internal sealed class InlineProgress<T>(Action<T> action) : IProgress<T>
    {
        public void Report(T value) => action(value);
    }
}

// The production source is compiled unchanged. Only network, OS and user-data
// boundaries are replaced; no installer or application process is launched.
namespace Playhub.Services
{
    public sealed class HttpClient : System.Net.Http.HttpClient
    {
        public HttpClient() : base(new OfflineHandler()) { }
        private sealed class OfflineHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => UpdateTests.State.Reply(request, token);
        }
    }
    public static class AppPaths
    {
        public static string DownloadsRoot => Path.Combine(UpdateTests.State.Root, "downloads");
        public static void EnsureRoots() => Directory.CreateDirectory(DownloadsRoot);
    }
}

namespace PlayhubSetup
{
    public static class Environment
    {
        public enum SpecialFolder { LocalApplicationData, ApplicationData, Programs, DesktopDirectory, Startup }
        public static int ProcessId => System.Environment.ProcessId;
        public static string? ProcessPath { get; set; }
        public static string GetFolderPath(SpecialFolder folder) => Path.Combine(UpdateTests.State.Root, folder.ToString());
    }
    public sealed class Process : IDisposable
    {
        public int Id => -1;
        public static readonly List<System.Diagnostics.ProcessStartInfo> Launches = new();
        public static Process[] GetProcessesByName(string name) => Array.Empty<Process>();
        public static Process Start(System.Diagnostics.ProcessStartInfo info)
        {
            UpdateTests.State.AssertPath(info.FileName);
            Launches.Add(info);
            return new Process();
        }
        public void CloseMainWindow() { }
        public bool WaitForExit(int ms) => true;
        public void Kill(bool entireProcessTree = false) { }
        public void Dispose() { }
    }
    public static class Registry
    {
        public static MemoryKey CurrentUser { get; } = new();
    }
    public sealed class MemoryKey : IDisposable
    {
        private readonly Dictionary<string, object> values = new();
        public MemoryKey CreateSubKey(string name) => this;
        public MemoryKey OpenSubKey(string name) => this;
        public object? GetValue(string name) => values.GetValueOrDefault(name);
        public void SetValue(string name, object value) => values[name] = value;
        public void SetValue(string name, object value, Microsoft.Win32.RegistryValueKind kind) => values[name] = value;
        public void DeleteSubKeyTree(string name, bool throwOnMissingSubKey) => values.Clear();
        public void Dispose() { }
    }
    public static class Shortcuts
    {
        public static void Create(string path, string exe, string dir, string icon)
        {
            UpdateTests.State.AssertPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, exe);
        }
    }
    public static class Loc
    {
        public static (string Code, string Native)[] Languages = { ("it", "Italiano"), ("en", "English") };
        public static string T(string text) => text;
    }
}
