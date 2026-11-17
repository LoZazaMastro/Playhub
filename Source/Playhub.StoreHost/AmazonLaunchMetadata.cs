// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 Andrea Sgarro (LoZazaMastro).
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace Playhub.StoreLibrary;

// Original bounded reader for Nile's documented fuel.json launch fields. No shell,
// environment expansion, executable search or fallback launch URI is supported.
internal sealed record AmazonLaunchMetadata(string Executable, string[] Arguments, string? WorkingDirectory)
{
    internal const int MaximumBytes = 256 * 1024;
    internal static AmazonLaunchMetadata Parse(byte[] bytes)
    {
        if (bytes.Length is 0 or > MaximumBytes) throw Invalid();
        try
        {
            var root = ReadDocument(bytes);
            if (!root.ContainsKey("SchemaVersion") || root["SchemaVersion"] is not JsonValue) throw Invalid();
            var main = root["Main"] as JsonObject ?? throw Invalid();
            var exe = main["Command"]?.GetValue<string>() ?? throw Invalid();
            AmazonManifest.ValidateRelative(exe.Replace('\\', '/'));
            if (!exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) throw Invalid();
            var args = main["Args"] is null ? [] : main["Args"] is JsonArray array
                ? array.Select(a => a?.GetValue<string>() ?? throw Invalid()).ToArray() : throw Invalid();
            if (args.Length > 256 || args.Any(a => a.IndexOfAny(['\0', '\r', '\n']) >= 0) || args.Sum(a => (long)a.Length + 3) > 30000) throw Invalid();
            var wd = main["WorkingSubdirOverride"]?.GetValue<string>();
            if (!string.IsNullOrEmpty(wd) && wd != ".") AmazonManifest.ValidateRelative(wd.Replace('\\', '/'));
            return new(exe.Replace('\\', '/'), args, wd?.Replace('\\', '/'));
        }
        catch (Exception e) when (e is DecoderFallbackException or InvalidOperationException or FormatException or OverflowException)
        { throw Invalid(); }
    }
    internal static JsonObject ReadDocument(byte[] bytes)
    {
        if (bytes.Length is 0 or > MaximumBytes) throw Invalid();
        try { return new Reader(new UTF8Encoding(false, true).GetString(bytes)).Read() as JsonObject ?? throw Invalid(); }
        catch (Exception e) when (e is DecoderFallbackException or InvalidOperationException or FormatException or OverflowException) { throw Invalid(); }
    }
    private static StoreOperationException Invalid() => new("amazon_launch_metadata_invalid");

    // JSON5 syntax actually used by fuel files: comments, single/double quoted
    // strings, identifier keys and trailing commas. Unsupported numeric values
    // (NaN/Infinity) and ambiguous duplicate keys are refused, never repaired.
    private sealed class Reader(string text)
    {
        private int offset, nodes;
        internal JsonNode? Read() { var value = Value(0); Skip(); if (offset != text.Length) throw Invalid(); return value; }
        private void Skip()
        {
            while (offset < text.Length)
            {
                if (char.IsWhiteSpace(text[offset]) || text[offset] == '\ufeff') { offset++; continue; }
                if (offset + 1 >= text.Length || text[offset] != '/') return;
                if (text[offset + 1] == '/') { offset += 2; while (offset < text.Length && text[offset] is not ('\r' or '\n')) offset++; }
                else if (text[offset + 1] == '*') { offset += 2; var end = text.IndexOf("*/", offset, StringComparison.Ordinal); if (end < 0) throw Invalid(); offset = end + 2; }
                else return;
            }
        }
        private bool Take(char value) { Skip(); if (offset >= text.Length || text[offset] != value) return false; offset++; return true; }
        private JsonNode? Value(int depth)
        {
            Skip(); if (depth > 24 || ++nodes > 4096 || offset >= text.Length) throw Invalid();
            if (Take('{'))
            {
                var obj = new JsonObject(); if (Take('}')) return obj;
                do
                {
                    Skip(); if (offset >= text.Length) throw Invalid(); var key = text[offset] is '\'' or '"' ? String() : Identifier();
                    if (obj.ContainsKey(key) || !Take(':')) throw Invalid();
                    obj.Add(key, Value(depth + 1)); if (Take('}')) return obj;
                    if (!Take(',')) throw Invalid(); if (Take('}')) return obj;
                } while (true);
            }
            if (Take('['))
            {
                var array = new JsonArray(); if (Take(']')) return array;
                do { array.Add(Value(depth + 1)); if (Take(']')) return array; if (!Take(',')) throw Invalid(); if (Take(']')) return array; } while (true);
            }
            if (text[offset] is '\'' or '"') return JsonValue.Create(String());
            var start = offset;
            while (offset < text.Length && !char.IsWhiteSpace(text[offset]) && text[offset] is not (',' or ']' or '}' or '/')) offset++;
            var token = text[start..offset];
            if (token == "true") return JsonValue.Create(true); if (token == "false") return JsonValue.Create(false); if (token == "null") return null;
            if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number)) return JsonValue.Create(number);
            throw Invalid();
        }
        private string Identifier()
        {
            var start = offset;
            if (offset >= text.Length || !(char.IsLetter(text[offset]) || text[offset] is '_' or '$')) throw Invalid();
            offset++; while (offset < text.Length && (char.IsLetterOrDigit(text[offset]) || text[offset] is '_' or '$')) offset++;
            return text[start..offset];
        }
        private string String()
        {
            var quote = text[offset++]; var result = new StringBuilder();
            while (offset < text.Length)
            {
                var c = text[offset++]; if (c == quote) return result.ToString(); if (c < ' ' || result.Length > 32768) throw Invalid();
                if (c != '\\') { result.Append(c); continue; }
                if (offset >= text.Length) throw Invalid(); c = text[offset++];
                switch (c)
                {
                    case '\r': if (offset < text.Length && text[offset] == '\n') offset++; break;
                    case '\n': case '\u2028': case '\u2029': break;
                    case 'n': result.Append('\n'); break; case 'r': result.Append('\r'); break;
                    case 't': result.Append('\t'); break; case 'b': result.Append('\b'); break;
                    case 'f': result.Append('\f'); break; case 'v': result.Append('\v'); break;
                    case '0': if (offset < text.Length && char.IsAsciiDigit(text[offset])) throw Invalid(); result.Append('\0'); break;
                    case 'u': case 'x':
                        var count = c == 'u' ? 4 : 2;
                        if (offset + count > text.Length || !ushort.TryParse(text.AsSpan(offset, count), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var code)) throw Invalid();
                        result.Append((char)code); offset += count; break;
                    default: result.Append(c); break;
                }
            }
            throw Invalid();
        }
    }
}
