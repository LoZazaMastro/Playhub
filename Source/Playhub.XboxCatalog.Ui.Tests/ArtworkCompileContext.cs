// Inert compilation seams. The tested static URL predicate does not invoke these.
namespace Playhub.Integrations;
public static class ApplicationIntegrationDataStore { public static void GuardPath(string path) => throw new NotSupportedException(); }
public sealed class ApplicationImageCache(string root)
{
    public Task<string> SaveAsync(string identity,string category,byte[] bytes,CancellationToken token) => throw new NotSupportedException(root);
}
