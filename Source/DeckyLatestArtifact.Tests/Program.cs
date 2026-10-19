using Playhub.Services;

int passed = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); passed++; }
var good = new byte[] { 7 };
bool Valid(byte[] bytes) => bytes.SequenceEqual(good);
var calls = new List<long>();
var result = await DeckyLatestArtifact.DownloadAsync(() => Task.FromResult<byte[]?>(good),
    () => throw new Exception("Unnecessary API request"), _ => throw new Exception("Unnecessary fallback"), Valid);
Check(ReferenceEquals(result, good), "Valid alias avoids extra API traffic");
result = await DeckyLatestArtifact.DownloadAsync(() => Task.FromResult<byte[]?>(null),
    () => Task.FromResult<IEnumerable<long>>([10, 9, 8]), id => { calls.Add(id); return Task.FromResult<byte[]?>(id == 9 ? good : null); }, Valid);
Check(ReferenceEquals(result, good) && calls.SequenceEqual([10L, 9L]), "Expired alias uses most recent mirrored successful run");
calls.Clear();
result = await DeckyLatestArtifact.DownloadAsync(() => Task.FromResult<byte[]?>([0]),
    () => Task.FromResult<IEnumerable<long>>([-1, 4, 4, 3, 2, 1]), id => { calls.Add(id); return Task.FromResult<byte[]?>([0]); }, Valid);
Check(result is null && calls.SequenceEqual([4L, 3L, 2L]), "Invalid archives rejected and duplicate/invalid IDs skipped with three-request bound");
result = await DeckyLatestArtifact.DownloadAsync(() => Task.FromResult<byte[]?>(null),
    () => Task.FromException<IEnumerable<long>>(new IOException("Offline")), _ => throw new Exception("Unexpected request"), Valid);
Check(result is null, "Offline workflow API returns actionable download failure");
result = await DeckyLatestArtifact.DownloadAsync(() => Task.FromResult<byte[]?>(null),
    () => Task.FromResult<IEnumerable<long>>([]), _ => throw new Exception("Unexpected request"), Valid);
Check(result is null, "Empty workflow list does not install unverified data");
result = await DeckyLatestArtifact.DownloadAsync(() => Task.FromResult<byte[]?>(null),
    () => throw new Exception("Existing installation must not request historical runs"),
    _ => throw new Exception("Existing installation must not download historical runs"), Valid, allowFallback: false);
Check(result is null, "Missing latest alias preserves existing installation without querying fallback runs");
result = await DeckyLatestArtifact.DownloadAsync(() => Task.FromResult<byte[]?>([0]),
    () => throw new Exception("Invalid alias must not request historical runs"),
    _ => throw new Exception("Invalid alias must not download historical runs"), Valid, allowFallback: false);
Check(result is null, "Invalid latest alias preserves existing installation without querying fallback runs");
result = await DeckyLatestArtifact.DownloadAsync(() => Task.FromResult<byte[]?>(good),
    () => throw new Exception("Valid alias must not request historical runs"),
    _ => throw new Exception("Valid alias must not download historical runs"), Valid, allowFallback: false);
Check(ReferenceEquals(result, good), "Valid alias remains available for updating existing installation");
Console.WriteLine($"Latest Decky artifact: {passed} checks passed; no download, install or process actions.");
