// SPDX-License-Identifier: GPL-3.0-only
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Playhub.FirstContent;
namespace GamingMode.Services;
internal static class LaunchCurtainCoverEndpoints
{
 internal static void Map(WebApplication app,XboxShellBroker shell,LaunchCurtainCoverController controller,Func<HttpContext,CurtainPublisher?> publisher)
 {
  app.MapPost("/session/launch-curtain/cover",(RequestDelegate)(async context=>{
   var result=await HandleAsync(context,
    ()=>shell.IsAuthorized(context.Connection.RemoteIpAddress,context.Request.Headers["X-Playhub-Shell-Token"].ToString()),controller,publisher);
   await result.ExecuteAsync(context);
  }));
 }
 internal static async Task<IResult> HandleAsync(HttpContext context,Func<bool> authorized,LaunchCurtainCoverController controller,Func<HttpContext,CurtainPublisher?>? publisher=null)
 {
   if(!authorized())return Results.StatusCode(401);
   var origin=publisher?.Invoke(context);if(publisher is not null&&origin is null)return Results.StatusCode(403);
   byte[] body=new byte[4097];int count=0;
   try{
    while(count<body.Length){int got=await context.Request.Body.ReadAsync(body.AsMemory(count),context.RequestAborted);if(got==0)break;count+=got;}
    var request=CurtainCoverProtocol.Parse(body.AsMemory(0,count));if(request is null)return Results.StatusCode(400);
    return Results.Json(await controller.ApplyAsync(request,context.RequestAborted,origin));
   }catch(OperationCanceledException){return Results.StatusCode(408);}
 }
}
