// Playhub Xbox Session. Distributed under GPL-3.0-only.
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

internal readonly record struct SteamVirtualInventory(TritonSource? Source, bool Shadow);

internal static class SteamInputEvidence
{
    internal const int MaxBytes = 16384;

    internal static SteamVirtualInventory Read(string path) => Read(
        () => new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete), milliseconds => Thread.Sleep(milliseconds));
    // Two complete concordant observations are required. A failed read never
    // substitutes a previous positive snapshot, and never blocks Steam writes.
    internal static SteamVirtualInventory Read(Func<Stream> open, Action<int> wait)
    {
        byte[] first=new byte[MaxBytes+1],second=new byte[MaxBytes+1];
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                int a=Snapshot(first),b=Snapshot(second);
                if(a>0&&a==b&&first.AsSpan(0,a).SequenceEqual(second.AsSpan(0,b)))
                    return Parse(Encoding.UTF8.GetString(first,0,a));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { return default; }
            if(attempt<2)wait(1);
        }
        return default;
        int Snapshot(byte[] bytes)
        {
            using var file=open();long expected=file.Length;
            if(expected is <=0 or >MaxBytes)return 0;
            int count=file.ReadAtLeast(bytes,bytes.Length,false);
            return count==expected&&file.Length==expected?count:0;
        }
    }

    internal static SteamVirtualInventory Parse(string text)
    {
        if (text.Length is <= 0 or > MaxBytes) return default;
        int index = -1, fields = 0;
        var seen = new HashSet<int>();
        ushort vendor = 0, product = 0;
        ulong handle = 0;
        string? type = null;
        TritonSource? candidate = null;
        var outputs = new List<(int Slot, ulong Handle)>();
        var identities = new List<(ushort Vendor, ushort Product, ulong Handle, string? Type, int Fields)>();
        bool shadow = false, invalid = false;
        void Commit()
        {
            if (index < 0) return;
            if (!seen.Add(index) || (fields & 3) != 3)
            {
                invalid = true;
                return;
            }
            identities.Add((vendor, product, handle, type, fields));
            if (vendor == VigemRelayOutput.Vendor && product == VigemRelayOutput.Product)
            {
                // Steam retains virtual output records after device removal,
                // and can enumerate our own output while a relay is active.
                // Neither is a physical Steam Controller input. Only a distinct
                // output identity may coexist with the exact source. Steam can
                // classify the same output as xbox360 or xboxone, and keeps
                // removed records. Classification alone is not live presence.
                if (fields != 15 || handle == 0 || !(string.Equals(type, "xbox360", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(type, "xboxone", StringComparison.OrdinalIgnoreCase)))
                    shadow = true;
                else outputs.Add((index, handle));
            }
            if (vendor == 0x28DE && product is 0x1302 or 0x1304)
            {
                // Steam's physical identity and its controller classification
                // must agree. A name, generic XInput or a Valve virtual output
                // is not evidence of an active Steam Controller.
                if (fields != 15 || !string.Equals(type, "steam", StringComparison.OrdinalIgnoreCase)) return;
                if (handle == 0) invalid = true;
                else if (candidate is { } previous)
                {
                    // Steam may list one physical controller in multiple slots.
                    // Only the exact nonzero handle/product/type may alias.
                    if (previous.Handle != handle || previous.Product != product) invalid = true;
                    else if (index < previous.Slot) candidate = new TritonSource(index, product, handle);
                }
                else candidate = new TritonSource(index, product, handle);
            }
        }

        foreach (var raw in text.Split('\n'))
        {
            string row = raw.Trim().TrimStart('\uFEFF');
            if (row.StartsWith("[slot ", StringComparison.OrdinalIgnoreCase) && row.EndsWith(']'))
            {
                Commit();
                if (!int.TryParse(row.AsSpan(6, row.Length - 7), NumberStyles.None,
                    CultureInfo.InvariantCulture, out index) || index < 0) invalid = true;
                fields = 0;
                vendor = product = 0;
                handle = 0;
                type = null;
                continue;
            }

            if (row.StartsWith("VID=", StringComparison.OrdinalIgnoreCase))
            {
                if ((fields & 1) != 0 || !ushort.TryParse(Hex(row.AsSpan(4)), NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out vendor)) invalid = true;
                fields |= 1;
            }
            else if (row.StartsWith("PID=", StringComparison.OrdinalIgnoreCase))
            {
                if ((fields & 2) != 0 || !ushort.TryParse(Hex(row.AsSpan(4)), NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out product)) invalid = true;
                fields |= 2;
            }
            else if (row.StartsWith("handle=", StringComparison.OrdinalIgnoreCase))
            {
                if ((fields & 4) != 0 || !ulong.TryParse(Hex(row.AsSpan(7)), NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out handle)) invalid = true;
                fields |= 4;
            }
            else if (row.StartsWith("type=", StringComparison.OrdinalIgnoreCase))
            {
                if ((fields & 8) != 0) invalid = true;
                type = row[5..].Trim();
                fields |= 8;
            }
        }
        Commit();
        if (candidate is { } typed && identities.Any(identity => identity.Handle == typed.Handle
            && (identity.Fields != 15 || identity.Vendor != 0x28DE || identity.Product != typed.Product
                || !string.Equals(identity.Type, "steam", StringComparison.OrdinalIgnoreCase)))) invalid = true;
        if (candidate is { } physical && outputs.Any(output => output.Slot == physical.Slot || output.Handle == physical.Handle))
            shadow = true;
        return new SteamVirtualInventory(invalid ? null : candidate, shadow);
    }

    private static ReadOnlySpan<char> Hex(ReadOnlySpan<char> value) =>
        value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? value[2..] : value;
}

// A disconnected/replaced controller cannot regain an existing lease even if
// Steam later reuses its slot and handle. Reconnection needs a fresh reader.
internal sealed class SteamControllerSourceLease(TritonSource source)
{
    internal bool Revoked { get; private set; }
    internal readonly record struct Failure(string Reason,TritonSource Expected,TritonSource? Observed,bool Shadow);
    internal Failure? FirstFailure { get; private set; }
    internal string FailureText => FirstFailure is { } f
        ? $"{f.Reason}; expected={f.Expected.Slot}:{f.Expected.Product:X4}:{f.Expected.Handle:X16}; observed={(f.Observed is {} o ? $"{o.Slot}:{o.Product:X4}:{o.Handle:X16}" : "none")}; shadow={f.Shadow}"
        : Revoked ? "explicitly-revoked" : "not-revoked";
    internal void Revoke() => Revoked = true;
    internal bool Current(SteamVirtualInventory inventory)
    {
        if (Revoked) return false;
        if (source.Valid && inventory.Source is null && !inventory.Shadow) return false;
        if (!source.Valid || inventory.Shadow || inventory.Source != source)
        {
            FirstFailure = new(!source.Valid ? "invalid-expected" : inventory.Shadow ? "shadow"
                : inventory.Source is null ? "missing-or-invalid-inventory-source" : "different-source",source,inventory.Source,inventory.Shadow);
            Revoked = true;
        }
        return !Revoked;
    }
}

