using System.Net;
using System.Text;
using System.Text.Json;
using Playhub.StoreLibrary;
static partial class Checks
{
    static JsonElement Achievement(string id,uint title=77,bool revoked=false,string? icon=null)=>JsonSerializer.SerializeToElement(new{
        id,serviceConfigId="fixture-sc",name="Fixture "+id,description="Unlocked",lockedDescription="Locked",achievementType="Persistent",progressState="Achieved",isRevoked=revoked,isSecret=false,
        progression=new{timeUnlocked="2020-01-01T12:00:00Z"},titleAssociations=new[]{new{id=title.ToString()}},mediaAssets=new[]{new{type="Icon",url=icon??"https://images-eds-ssl.xboxlive.com/image/fixture"}},rarity=new{currentPercentage=12.5},authorization="MUST-NOT-LEAVE-READER"});
    static async Task LiveAchievementReader()
    {
        var now=DateTimeOffset.Parse("2026-11-16T12:00:00Z");
        var account=new XboxAccountTokens(new("ACCESS","REFRESH",now.AddHours(1)),"123","999","MARKET",now.AddHours(1),null,"LIVE-SECRET");
        using(var handler=new AchievementHttp()){using var http=new HttpClient(handler);var auth=new StoreXboxAccountAuth(http,()=>now);
            var rows=await auth.ReadLiveAchievementsAsync(account,77,"it-IT",default);
            Require(handler.Calls==2&&rows.Length==3,"Xbox Live follows bounded encoded paging, excludes another title and deduplicates exact achievement IDs");
            Require(rows.Single(r=>r.GetProperty("id").GetString()=="1").GetProperty("progressState").GetString()=="Achieved","current player's real unlock retained");
            Require(rows.Single(r=>r.GetProperty("id").GetString()=="2").GetProperty("progressState").GetString()=="NotStarted","revoked achievement cannot be displayed as unlocked");
            Require(rows.Single(r=>r.GetProperty("id").GetString()=="3").GetProperty("mediaAssets").GetArrayLength()==0,"foreign icon host removed before reaching Metadata");
            string serialized=JsonSerializer.Serialize(rows);Require(!serialized.Contains("LIVE-SECRET")&&!serialized.Contains("MUST-NOT-LEAVE-READER"),"only curated achievement fields leave the authenticated reader");
        }
        foreach(string mode in new[]{"unauthorized","repeated-cursor","too-many-pages","too-many-rows"}){
            using var handler=new AchievementHttp{Mode=mode};using var http=new HttpClient(handler);var auth=new StoreXboxAccountAuth(http,()=>now);
            try{await auth.ReadLiveAchievementsAsync(account,77,"it-IT",default);throw new Exception("unbounded or unauthorized achievement response accepted");}
            catch(StoreOperationException e){Require(e.Code==(mode=="unauthorized"?"xbox_signin_expired":mode=="repeated-cursor"?"xbox_response_invalid":"xbox_response_too_large"),"typed failure for "+mode);}
            Require(handler.Calls<=10,"Xbox Live paging always bounded");
        }
    }
    sealed class AchievementHttp:HttpMessageHandler
    {
        internal int Calls;internal string Mode="normal";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
        {
            Calls++;Require(request.Method==HttpMethod.Get&&request.RequestUri!.Host=="achievements.xboxlive.com"&&request.RequestUri.AbsolutePath=="/users/xuid(999)/achievements","only the current connected account is queried");
            Require(request.Headers.GetValues("Authorization").Single()=="XBL3.0 x=123;LIVE-SECRET"&&request.Headers.GetValues("x-xbl-contract-version").Single()=="4"&&request.Headers.GetValues("Accept-Language").Single()=="it-IT","Xbox Live account token, v4 contract and Steam locale are private request headers");
            if(Calls==2&&Mode=="normal")Require(request.RequestUri!.Query.Contains("continuationToken=page%20%2F%20two"),"opaque paging token URL encoded");
            JsonElement[] rows=Mode=="too-many-rows"?Enumerable.Range(0,501).Select(n=>Achievement(n.ToString())).ToArray():Calls==1?[Achievement("1"),Achievement("foreign",9999),Achievement("1")]:[Achievement("2",revoked:true),Achievement("3",icon:"https://127.0.0.1/private")];
            string? next=Mode=="repeated-cursor"?"same":Mode=="too-many-pages"?"page"+Calls:Calls==1&&Mode=="normal"?"page / two":null;
            var response=new HttpResponseMessage(Mode=="unauthorized"?HttpStatusCode.Unauthorized:HttpStatusCode.OK){RequestMessage=request,Content=new StringContent(JsonSerializer.Serialize(new{achievements=rows,pagingInfo=new{continuationToken=next}}),Encoding.UTF8,"application/json")};
            return Task.FromResult(response);
        }
    }
}
