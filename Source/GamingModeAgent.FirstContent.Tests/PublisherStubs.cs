// Inert identity seam: pure table tests never call the Windows default reader.
namespace Playhub.DeckyHost;
internal static class DeckyProcessTree{
 internal sealed record Identity(int Pid,int ParentPid,long Birth,string Path,string Sid,uint Session);
 internal static Identity? Read(int pid){_ =pid;throw new Exception("Unexpected live process query");}
 internal static bool IsLoaderPath(string path){_ =path;return false;}
}
