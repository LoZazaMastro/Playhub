using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Playhub.Services;

// The branch alias can expire before nightly.link mirrors a successful build.
// Retry only successful main-workflow run IDs supplied by the official API.
internal static class DeckyLatestArtifact
{
    internal static async Task<byte[]?> DownloadAsync(Func<Task<byte[]?>> latest,
        Func<Task<IEnumerable<long>>> successfulRuns, Func<long, Task<byte[]?>> run,
        Func<byte[], bool> valid, bool allowFallback = true)
    {
        var bytes = await latest();
        if (bytes is not null && valid(bytes)) return bytes;
        if (!allowFallback) return null;
        IEnumerable<long> candidates;
        try { candidates = (await successfulRuns()).Where(id => id > 0).Distinct().Take(3).ToArray(); }
        catch { return null; }
        foreach (var id in candidates)
        {
            bytes = await run(id);
            if (bytes is not null && valid(bytes)) return bytes;
        }
        return null;
    }
}
