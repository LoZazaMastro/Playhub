using System.Diagnostics;
using System.Text;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace Playhub.Integrations;

public sealed record IntegrationToolCommand(string Tool, IReadOnlyList<string> Arguments, string WorkingDirectory, TimeSpan Timeout);
public sealed record IntegrationToolResult(int ExitCode, string Output);

/// <summary>Only app-owned packaged tools; no PATH lookup, plugin process or automatic update.</summary>
public sealed class ApplicationToolRunner(string toolsRoot, string dataRoot)
{
    private readonly string _tools = Path.GetFullPath(toolsRoot);
    private readonly string _data = Path.GetFullPath(dataRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
    public string Resolve(string name)
    {
        if (name is not ("yt-dlp.exe" or "ffmpeg.exe" or "ffprobe.exe" or "node.exe")) throw new ArgumentException("Unsupported app tool.");
        string path = Path.Combine(_tools, name);
        if (!File.Exists(path)) throw new FileNotFoundException("Install the Playhub media tools before downloading.", path);
        for (string? part = path; part is not null; part = Path.GetDirectoryName(part))
            if ((File.GetAttributes(part) & FileAttributes.ReparsePoint) != 0) throw new IOException("App tool path is redirected.");
        string manifest = Path.Combine(_tools, "runtime-manifest.json");
        ApplicationIntegrationDataStore.GuardPath(manifest);
        if (!File.Exists(manifest) || new FileInfo(manifest).Length > 256 * 1024) throw new IOException("The app media tool inventory is unavailable.");
        var inventory = JsonNode.Parse(File.ReadAllBytes(manifest));
        if (inventory?["tools"]?[name]?["files"] is not JsonObject files || files.Count == 0) throw new IOException("The app tool is absent from its inventory.");
        foreach (var file in files)
        {
            string checkedPath = Path.GetFullPath(Path.Combine(_tools, file.Key));
            if (!checkedPath.StartsWith(_tools.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("Invalid media tool inventory path.");
            ApplicationIntegrationDataStore.GuardPath(checkedPath);
            using var input = File.OpenRead(checkedPath);
            if (!string.Equals(Convert.ToHexString(SHA256.HashData(input)), file.Value?.ToString(), StringComparison.OrdinalIgnoreCase)) throw new IOException("The app media tool does not match its inventory.");
        }
        return path;
    }

    public async Task<IntegrationToolResult> RunAsync(IntegrationToolCommand command, CancellationToken ct)
    {
        string work = Path.GetFullPath(command.WorkingDirectory);
        if (!work.StartsWith(_data, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(work)) throw new ArgumentException("Invalid app workspace.");
        ApplicationIntegrationDataStore.GuardPath(work);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(command.Timeout);
        var start = new ProcessStartInfo(Resolve(command.Tool))
        {
            WorkingDirectory = work, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        start.Environment["PYTHONUTF8"] = "1";
        foreach (string argument in command.Arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("The app media tool could not start.");
        try { process.PriorityClass = ProcessPriorityClass.BelowNormal; } catch (System.ComponentModel.Win32Exception) { } catch (InvalidOperationException) { }
        var output = ReadBoundedAsync(process.StandardOutput, 2 * 1024 * 1024, limit);
        var error = ReadBoundedAsync(process.StandardError, 512 * 1024, limit);
        try
        {
            await Task.WhenAll(process.WaitForExitAsync(limit.Token), output, error);
            return new(process.ExitCode, await output);
        }
        catch
        {
            // This held Process is the child we created, never a name/PID search.
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); } catch (TimeoutException) { }
            }
            throw;
        }
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, int maximum, CancellationTokenSource lifetime)
    {
        try
        {
            var output = new StringBuilder(); var block = new char[4096]; int count;
            while ((count = await reader.ReadAsync(block.AsMemory(), lifetime.Token)) > 0)
            {
                if (output.Length + count > maximum) throw new IOException("App media tool output exceeded its limit.");
                output.Append(block, 0, count);
            }
            return output.ToString();
        }
        catch { lifetime.Cancel(); throw; }
    }
}
