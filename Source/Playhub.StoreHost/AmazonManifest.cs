// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 Andrea Sgarro (LoZazaMastro).
// Original bounded reader for the signed Amazon SDS v3 wire format documented by
// pinned Nile 1ed408ab, models/manifest.py and proto/sds_proto2_pb2.py. No Nile code.
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using SharpCompress.Compressors.Xz;

namespace Playhub.StoreLibrary;
internal sealed record AmazonFile(string RelativePath, long Size, int Algorithm, byte[] Hash);
internal sealed record AmazonManifest(byte[] RawHash, IReadOnlyList<AmazonFile> Files)
{
    internal const int MaximumBytes = 64 * 1024 * 1024;
    private const string PublicKey = """
-----BEGIN PUBLIC KEY-----
MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEA6fSRMUi3VpTtv9P4+KvM
AcAIP4SYbTQfB1ns7vyUjsj8nrF2lGNtQTtGLnrNmM2ElZ2R7VmQtNiRtPMxToIW
Rajin0H0OyzGrHA8P6w96Mj4q1JeORCzJeVFgLOBClCCMmB+5bJBWnJcq/sEMwu9
gGynCeiYNLt7ZMVpL1GOsNjl+yLk7OMMGpMj1JWCVFfgYE9Lud1QZJllFAWhRBoT
wTctAUZTikObFUoBm+KEiCsKIcay4WOybvJwxTNBUl2GL8c+ihrT2ntLPpb9aIJE
/gXU3Ihl5oXe/0P/QN0CRu/ybXWLiGzIYqKIok4nepkdo8V3gWR55K801pOuck0B
awIDAQAB
-----END PUBLIC KEY-----
""";
    internal static AmazonManifest Parse(byte[] wire, RSA? testKey = null)
    {
        if (wire.Length < 8 || wire.Length > MaximumBytes) throw Invalid();
        var length = BinaryPrimitives.ReadUInt32BigEndian(wire);
        if (length == 0 || length > 65536 || length >= wire.Length - 4) throw Invalid();
        var header = Fields(wire.AsMemory(4, (int)length));
        var compression = Number(Fields(One(header, 1)), 1);
        var signature = Fields(One(header, 3));
        if (Number(signature, 1) != 0) throw Invalid();
        var input = wire.AsMemory(4 + (int)length);
        byte[] plain;
        if (compression == 0) plain = input.ToArray();
        else if (compression == 1)
        {
            // Python's lzma emits XZ by default. Unknown encodings are refused.
            if (!input.Span.StartsWith(new byte[] { 0xfd, 0x37, 0x7a, 0x58, 0x5a, 0 })) throw Invalid();
            ValidateXzBounds(input.ToArray());
            using var compressed = new MemoryStream(input.ToArray(), false);
            using var decoder = new XZStream(compressed);
            using var output = new MemoryStream();
            var buffer = new byte[16384]; int read;
            while ((read = decoder.Read(buffer, 0, buffer.Length)) != 0)
            { if (output.Length + read > MaximumBytes) throw Invalid(); output.Write(buffer, 0, read); }
            plain = output.ToArray();
        }
        else throw Invalid();
        using var official = RSA.Create(); if (testKey == null) official.ImportFromPem(PublicKey);
        if (!(testKey ?? official).VerifyData(plain, One(signature, 2).Span, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)) throw Invalid();
        var packages = Fields(plain).Where(f => f.Tag == 1).ToArray();
        if (packages.Length != 1) throw Invalid(); // Nile downloads only package zero.
        var files = new List<AmazonFile>(); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in Fields(packages[0].Bytes).Where(f => f.Tag == 2))
        {
            if (files.Count >= 200000) throw Invalid();
            var fields = Fields(item.Bytes); var relative = new UTF8Encoding(false, true).GetString(One(fields, 1).Span).Replace('\\', '/');
            ValidateRelative(relative);
            if (!seen.Add(relative)) throw Invalid();
            var size = Number(fields, 3); if (size > long.MaxValue) throw Invalid();
            var hash = Fields(One(fields, 5)); var algorithm = Number(hash, 1); var value = One(hash, 2).ToArray();
            if (algorithm > 1 || algorithm == 0 && value.Length != 32 || algorithm == 1 && (value.Length != 32 || !Shake128.IsSupported)) throw Invalid();
            files.Add(new(relative, (long)size, (int)algorithm, value));
        }
        if (files.Count == 0) throw Invalid();
        _ = files.Aggregate(0L, (sum, f) => checked(sum + f.Size));
        return new(SHA256.HashData(wire), files);
    }
    private static void ValidateXzBounds(byte[] bytes)
    {
        // XZ footer back-size identifies the index. Limit to one LZMA2 block,
        // an <=64 MiB dictionary and <=64 MiB output before allocating a decoder.
        if(bytes.Length<36||bytes[^2]!=0x59||bytes[^1]!=0x5a)throw Invalid();
        var indexLength=((ulong)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(bytes.Length-8,4))+1)*4;
        if(indexLength>(ulong)bytes.Length-24)throw Invalid();
        var indexStart=bytes.Length-12-(int)indexLength;var i=indexStart;
        if(bytes[i++]!=0)throw Invalid();
        ulong Read(ref int offset){ulong n=0;for(var shift=0;shift<63;shift+=7){if(offset>=bytes.Length-4)throw Invalid();var b=bytes[offset++];n|=(ulong)(b&127)<<shift;if(b<128)return n;}throw Invalid();}
        if(Read(ref i)!=1)throw Invalid();var unpadded=Read(ref i);var output=Read(ref i);
        if(output>MaximumBytes||unpadded>(ulong)indexStart||12+((unpadded+3)&~3UL)!=(ulong)indexStart)throw Invalid();
        i=12;var headerSize=(bytes[i]+1)*4;if(headerSize<8||i+headerSize>indexStart)throw Invalid();i++;
        var flags=bytes[i++];if((flags&63)!=0)throw Invalid();
        if((flags&64)!=0)_=Read(ref i);if((flags&128)!=0){if(Read(ref i)>MaximumBytes)throw Invalid();}
        if(Read(ref i)!=0x21||Read(ref i)!=1||i>=12+headerSize-4||bytes[i]>28)throw Invalid();
    }
    internal static void ValidateRelative(string path)
    {
        if (path.Equals(".playhub-install-owner",StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(path) || path.Length > 4096 || path.StartsWith('/') || path.Contains(':') || path.IndexOfAny(['\0','\r','\n']) >= 0 || path.Split('/').Any(p => p is "" or "." or ".." || p.EndsWith(' ') || p.EndsWith('.') || p.IndexOfAny(['*','?','"','<','>','|']) >= 0 || IsDevice(p))) throw Invalid();
    }
    private static bool IsDevice(string part) { var stem = part.Split('.')[0].ToUpperInvariant(); return stem is "CON" or "PRN" or "AUX" or "NUL" || stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && stem[3] is >= '1' and <= '9'; }
    internal static string Contained(string root, string relative)
    {
        ValidateRelative(relative.Replace('\\','/'));
        var result = Path.GetFullPath(Path.Combine(root, relative)); var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        if (!result.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw Invalid();
        for (var current = result; current != null; current = Path.GetDirectoryName(current))
        { if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw Invalid(); }
        return result;
    }
    internal bool Verify(string directory, CancellationToken token)
    {
        foreach (var file in Files)
        {
            token.ThrowIfCancellationRequested(); var path = Contained(directory, file.RelativePath);
            if (!File.Exists(path)) return false;
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (input.Length != file.Size) return false;
            var actual = file.Algorithm == 0 ? SHA256.HashData(input) : Shake128.HashData(input, 32);
            if (!CryptographicOperations.FixedTimeEquals(actual, file.Hash)) return false;
        }
        return true;
    }
    private sealed record Field(int Tag, int Type, ReadOnlyMemory<byte> Bytes, ulong Value);
    private static List<Field> Fields(ReadOnlyMemory<byte> bytes)
    {
        var fields = new List<Field>(); var i = 0;
        ulong Varint() { ulong result=0; for(var s=0;s<70;s+=7) { if(i>=bytes.Length) throw Invalid(); var b=bytes.Span[i++]; if(s==63 && b>1) throw Invalid(); result|=(ulong)(b&127)<<s; if(b<128)return result; } throw Invalid(); }
        while (i < bytes.Length)
        {
            if (fields.Count >= 600005) throw Invalid(); var key=Varint(); var tag=key>>3; var type=(int)(key&7);
            if(tag==0 || tag>0x1fffffff) throw Invalid();
            if(type==0) fields.Add(new((int)tag,type,default,Varint()));
            else if(type==2) { var n=Varint(); if(n>(ulong)(bytes.Length-i))throw Invalid(); fields.Add(new((int)tag,type,bytes.Slice(i,(int)n),0));i+=(int)n; }
            else if(type is 1 or 5) { var n=type==1?8:4; if(i+n>bytes.Length)throw Invalid();i+=n; }
            else throw Invalid();
        }
        return fields;
    }
    private static ReadOnlyMemory<byte> One(List<Field> fields,int tag) { var found=fields.Where(f=>f.Tag==tag).ToArray(); if(found.Length!=1||found[0].Type!=2)throw Invalid();return found[0].Bytes; }
    private static ulong Number(List<Field> fields,int tag) { var found=fields.Where(f=>f.Tag==tag).ToArray();if(found.Length==0)return 0;if(found.Length!=1||found[0].Type!=0)throw Invalid();return found[0].Value; }
    private static StoreOperationException Invalid() => new("amazon_manifest_invalid_or_unsupported");
}
