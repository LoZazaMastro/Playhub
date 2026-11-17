// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 Andrea Sgarro (LoZazaMastro).
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace Playhub.StoreLibrary;
internal sealed record AmazonWorkerResult(int ExitCode,bool StopRequested,bool Drained,bool Forced);
internal interface IAmazonWorker : IAsyncDisposable { Task<AmazonWorkerResult> Completion {get;} Task StopAsync(CancellationToken token); }
internal delegate Task<IAmazonWorker> AmazonWorkerFactory(string host,string executable,IReadOnlyList<string> args,IReadOnlyDictionary<string,string> environment,string directory,Action<TransferProgress> progress,CancellationToken token);
internal sealed class AmazonWorker : IAmazonWorker
{
    private readonly Process process;private int stop;public Task<AmazonWorkerResult> Completion{get;}
    private AmazonWorker(Process process,Action<TransferProgress> progress){this.process=process;Completion=Observe(progress);}
    internal static async Task<IAmazonWorker> StartAsync(string host,string executable,IReadOnlyList<string> args,IReadOnlyDictionary<string,string> environment,string directory,Action<TransferProgress> progress,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var info=new ProcessStartInfo(host){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8,ArgumentList={"--supervise"}};
        var process=Process.Start(info)??throw new StoreOperationException("supervisor_start_failed");
        var owned=new AmazonWorker(process,progress);
        try
        {
            // The supervisor owns and drains the exact child job. Handshake must
            // finish even if the caller cancels after creating this owned process.
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new SupervisedToolRequest(executable,args.ToArray(),environment.ToDictionary(p=>p.Key,p=>p.Value),directory),StoreLibraryService.JsonOptions));
            await process.StandardInput.FlushAsync();return owned;
        }
        catch
        {
            process.StandardInput.Close();
            // Never kill a user process or infer drain from closing a pipe.
            await owned.Completion;await owned.DisposeAsync();throw;
        }
    }
    private Task<AmazonWorkerResult> Observe(Action<TransferProgress> progress)=>ReadAsync(process.StandardOutput,process.StandardError,process.WaitForExitAsync,progress);
    internal static async Task<AmazonWorkerResult> ReadAsync(TextReader output,TextReader errors,Func<CancellationToken,Task> exited,Action<TransferProgress> progress,Func<double>? seconds=null)
    {
        var stderr=Task.Run(async()=>{while(await errors.ReadLineAsync()!=null){}});
        AmazonWorkerResult? result=null;var invalid=false;
        var timer=Stopwatch.StartNew();long? previousBytes=null;double previousSeconds=0;double? speed=null;
        try
        {
            while(await output.ReadLineAsync() is { } line)
            {
                // Keep draining the exact worker after a protocol error. A bad
                // line is never permission to release the credential/queue owner.
                if(invalid)continue;
                try
                {
                    if(line.Length>131072)throw new StoreOperationException("supervisor_response_invalid");
                    using var doc=JsonDocument.Parse(line);var row=doc.RootElement;var kind=row.GetProperty("kind").GetString();
                    if(kind is "stdout" or "stderr")
                    {
                        var text=row.GetProperty("line").GetString()??"";
                        var match=Regex.Match(text,@"= Progress: [0-9.]+ (\d+)/(\d+),");
                        if(match.Success&&long.TryParse(match.Groups[1].Value,NumberStyles.None,CultureInfo.InvariantCulture,out var done)&&long.TryParse(match.Groups[2].Value,NumberStyles.None,CultureInfo.InvariantCulture,out var total)&&done>=0&&total>0&&done<=total)
                        {
                            var now=seconds?.Invoke()??timer.Elapsed.TotalSeconds;
                            if(previousBytes is null||done<previousBytes){previousBytes=done;previousSeconds=now;speed=null;}
                            else if(now-previousSeconds>=0.1){speed=(done-previousBytes.Value)/(now-previousSeconds);previousBytes=done;previousSeconds=now;}
                            progress(new(done,total,speed,"downloading",100.0*done/total));
                        }
                    }
                    else if(kind=="exit")
                    {
                        if(result!=null)throw new StoreOperationException("supervisor_response_invalid");
                        result=new(row.GetProperty("exitCode").GetInt32(),row.GetProperty("stopRequested").GetBoolean(),row.GetProperty("drained").GetBoolean(),row.GetProperty("forced").GetBoolean());
                    }
                    else if(kind=="error")invalid=true;
                }
                catch(Exception e) when(e is JsonException or InvalidOperationException or KeyNotFoundException or StoreOperationException){invalid=true;}
            }
        }
        finally { await Task.WhenAll(stderr,exited(CancellationToken.None)); }
        return !invalid&&result!=null?result:new(3,false,false,false);
    }
    public async Task StopAsync(CancellationToken token)
    {
        if(Interlocked.Exchange(ref stop,1)==0&&!process.HasExited){await process.StandardInput.WriteLineAsync("checkpoint".AsMemory(),token);await process.StandardInput.FlushAsync(token);}
    }
    public ValueTask DisposeAsync(){process.Dispose();return ValueTask.CompletedTask;}
}
