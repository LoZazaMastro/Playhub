// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 LoZazaMastro.
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Playhub.StoreLibrary;

internal sealed record WindowsStoreProduct(string ProductId, string PackageFamilyName);

/// <summary>
/// Official Microsoft Store CLI boundary. A caller
/// must re-check current ownership and exact installed PFN before publishing
/// any installed state; starting this command is never installation proof.
/// </summary>
internal sealed class StoreWindowsStoreCli
{
    readonly string executable;
    readonly StoreToolQuery query;
    internal StoreWindowsStoreCli(string? aliasPath = null, StoreToolQuery? read = null)
    {
        executable = Path.GetFullPath(aliasPath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "store.exe"));
        if (!Path.GetFileName(executable).Equals("store.exe", StringComparison.OrdinalIgnoreCase)) throw new StoreOperationException("windows_store_cli_unavailable");
        query = read ?? StoreToolProcess.QueryAsync;
    }
    internal bool Available => File.Exists(executable);
    static void Validate(string productId)
    {
        if (!StoreXboxAccountCollections.ValidId(productId)) throw new StoreOperationException("xbox_product_invalid");
    }
    internal async Task<WindowsStoreProduct> ReadProductAsync(string productId, CancellationToken token)
    {
        Validate(productId);
        if (!Available) throw new StoreOperationException("windows_store_cli_unavailable");
        var result = await query(executable, ["show", productId], new Dictionary<string, string>(), token);
        if (result.Code != 0 || result.Json.Length > 1024 * 1024) throw new StoreOperationException("windows_store_product_unavailable");
        // CLI has no JSON option. Read only its final technical section, never
        // a similarly named line embedded in a publisher's description.
        const string header = "Technical Information";
        int section = result.Json.LastIndexOf(header, StringComparison.Ordinal);
        if (section < 0) throw new StoreOperationException("windows_store_product_invalid");
        string technical = result.Json[section..];
        var id = Regex.Matches(technical, @"^[ \t]*Product ID[ \t]*:[ \t]*([^\r\n]+)", RegexOptions.Multiline);
        var pfn = Regex.Matches(technical, @"^[ \t]*PFN[ \t]*:[ \t]*([^\r\n]+)", RegexOptions.Multiline);
        if (id.Count != 1 || id[0].Groups[1].Value.Trim() != productId || pfn.Count != 1) throw new StoreOperationException("windows_store_product_invalid");
        string family = pfn[0].Groups[1].Value.Trim();
        if (family.Length > 255 || !Regex.IsMatch(family, @"^[A-Za-z0-9._-]+_[a-z0-9]{13}$")) throw new StoreOperationException("windows_store_product_invalid");
        return new(productId, family);
    }
    internal ProcessStartInfo CreateInstallStartInfo(string productId)
    {
        Validate(productId);
        if (!Available) throw new StoreOperationException("windows_store_cli_unavailable");
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
            RedirectStandardError = true, RedirectStandardInput = true,
            WorkingDirectory = Path.GetDirectoryName(executable)!
        };
        start.ArgumentList.Add("install"); start.ArgumentList.Add(productId);
        return start;
    }
}
