using System.Text.Json;
namespace Playhub.StoreLibrary;
// Compile context for the unrelated supervisor serialization path. QueryAsync,
// the method under test, never calls this property or the supervisor.
internal static class StoreLibraryService
{
    internal static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web);
}
