// SPDX-License-Identifier: GPL-3.0-or-later
// System curl fallback follows Unifideck 0.8.0's Epic transport workaround.
// Copyright (c) Unifideck contributors; Windows adaptation (c) 2026 LoZazaMastro.
using System.Diagnostics;
using System.Text;
namespace Playhub.StoreLibrary;
public sealed partial class StoreCliProvider
{
    // Windows' system TLS stack can be accepted where a .NET TLS fingerprint
    // receives a storefront challenge. Authentication is unchanged. The URL,
    // token and request body stay on the private stdin pipe, never argv/files.
    private static async Task<(int Code,string Body)?> EpicSystemRequest(string access,object body,CancellationToken token)
    {
        string executable=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"curl.exe");
        if(!File.Exists(executable)||(File.GetAttributes(executable)&FileAttributes.ReparsePoint)!=0)return null;
        if(access.Length is <1 or >32768||access.Any(c=>c<32||c==127))throw new StoreOperationException("authentication_expired");
        using var pin=new FileStream(executable,FileMode.Open,FileAccess.Read,FileShare.Read);
        static string Config(string text)=>"\""+text.Replace("\\","\\\\").Replace("\"","\\\"").Replace("\r","\\r").Replace("\n","\\n")+"\"";
        string config="url = \"https://store.epicgames.com/graphql\"\nrequest = \"POST\"\nheader = "+Config("Authorization: Bearer "+access)
            +"\nheader = \"Content-Type: application/json\"\nuser-agent = \"EpicGamesLauncher/15.17.1-35104619+++Portal+Release-Live\"\ndata-binary = "+Config(System.Text.Json.JsonSerializer.Serialize(body))+"\n";
        var info=new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(string arg in new[]{"--disable","--silent","--show-error","--proto","=https","--max-time","20","--max-filesize","8388608","--write-out","\n%{http_code}","--config","-"})info.ArgumentList.Add(arg);
        using var process=Process.Start(info)??throw new StoreOperationException("achievements_unavailable");
        using var budget=CancellationTokenSource.CreateLinkedTokenSource(token);budget.CancelAfter(TimeSpan.FromSeconds(25));
        static async Task<string> Read(StreamReader input,int maximum,CancellationToken stop)
        {
            var value=new StringBuilder();var buffer=new char[32768];int count;
            while((count=await input.ReadAsync(buffer.AsMemory(),stop))>0){if(value.Length>maximum-count)throw new StoreOperationException("achievements_response_too_large");value.Append(buffer,0,count);}return value.ToString();
        }
        try
        {
            var output=Read(process.StandardOutput,8*1024*1024+32,budget.Token);var errors=Read(process.StandardError,65536,budget.Token);
            await process.StandardInput.WriteAsync(config.AsMemory(),budget.Token);process.StandardInput.Close();
            await Task.WhenAll(output,errors,process.WaitForExitAsync(budget.Token));
            string raw=await output;int line=raw.LastIndexOf('\n');
            if(process.ExitCode!=0||line<0||!int.TryParse(raw.AsSpan(line+1),out int code))throw new StoreOperationException("achievements_unavailable");
            return(code,raw[..line]);
        }
        finally{if(!process.HasExited){try{process.Kill(true);}catch(InvalidOperationException){}await process.WaitForExitAsync(CancellationToken.None);}}
    }
}
