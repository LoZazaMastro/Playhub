using GamingMode.Services;
using Playhub.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using System.Net;

int checks=0;void Check(bool b){if(!b)throw new Exception("check "+checks);checks++;}
long now=0;int calls=0;
var pending=new TaskCompletionSource<PlayhubUpdateService.UpdateInfo?>(TaskCreationOptions.RunContinuationsAsynchronously);
var cache=new PlayhubUpdateNotice(()=>{calls++;return pending.Task;},()=>now);
var first=cache.Read();var second=cache.Read();Check(ReferenceEquals(first,second)&&calls==1);
now=3600000;Check(ReferenceEquals(first,cache.Read())&&calls==1);
var info=new PlayhubUpdateService.UpdateInfo(true,"2.8.0","2.7.0",null,null,"https://example.invalid",null,0,null);
pending.SetResult(info);Check(await first==info);Check(await cache.Read()==info&&calls==2);
now+=1799999;Check(await cache.Read()==info&&calls==2);
now++;Check(await cache.Read()==info&&calls==3);
now=-1;Check(await cache.Read()==info&&calls==3);
int failed=0;var offline=new PlayhubUpdateNotice(()=>{failed++;throw new IOException();},()=>0);
Check(await offline.Read() is null);Check(await offline.Read() is null&&failed==1);
string folder=Path.Combine(Path.GetTempPath(),"Playhub-UpdateNotice-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
try {
 var auth=new XboxShellBroker(folder,()=>false,()=>throw new Exception("must not start shell"),()=>0);
 var builder=WebApplication.CreateBuilder();await using var app=builder.Build();PlayhubUpdateNotice.Map(app,auth);
 var endpoint=((IEndpointRouteBuilder)app).DataSources.SelectMany(s=>s.Endpoints).OfType<RouteEndpoint>().Single(e=>e.RoutePattern.RawText=="/playhub/update");
 var ctx=new DefaultHttpContext();ctx.RequestServices=app.Services;ctx.Connection.RemoteIpAddress=IPAddress.Loopback;
 await endpoint.RequestDelegate!(ctx);Check(ctx.Response.StatusCode==401);
 ctx=new DefaultHttpContext();ctx.RequestServices=app.Services;ctx.Connection.RemoteIpAddress=IPAddress.Parse("192.0.2.1");
 ctx.Request.Headers["X-Playhub-Shell-Token"]=File.ReadAllText(Path.Combine(folder,"xbox-shell-token"));
 await endpoint.RequestDelegate!(ctx);Check(ctx.Response.StatusCode==401);
}finally{Directory.Delete(folder,true);}
Console.WriteLine(checks+" actual update notice cache/auth checks PASS; no network, OS or Steam calls");
