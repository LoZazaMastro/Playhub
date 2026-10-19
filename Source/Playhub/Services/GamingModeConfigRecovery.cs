using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Playhub.Services;

internal static class GamingModeConfigRecovery
{
    // Restoring a valid backup must never rotate a corrupt primary into .bak.
    internal static async Task RestoreAsync(string path, string contents, string diagnosticsDirectory)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await stream.WriteAsync(new UTF8Encoding(false).GetBytes(contents)).ConfigureAwait(false);
                await stream.FlushAsync().ConfigureAwait(false);
                stream.Flush(true);
            }
            if (File.Exists(path))
            {
                Directory.CreateDirectory(diagnosticsDirectory);
                var diagnostic = Path.Combine(diagnosticsDirectory, "config-corrupt-" +
                    DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff", CultureInfo.InvariantCulture) + "-" +
                    Guid.NewGuid().ToString("N") + ".json");
                File.Copy(path, diagnostic, overwrite: false);
                File.Replace(temporary, path, null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporary, path);
            }
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
