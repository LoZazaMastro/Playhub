// SPDX-License-Identifier: MIT. Original Playhub inert metadata checks.
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Playhub.StoreLibrary;

internal static class PartialSourceChecks
{
    internal static async Task Run(string root, Action<bool,string> check)
    {
        var bundle=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../Playhub.StoreHost/Runtime"));
        var state=Path.Combine(root,"metadata-state");
        const string file="payload/original.dat";
        var full=new byte[]{1,2,3};string md5=Convert.ToHexString(MD5.HashData(full));
        var epicJson=JsonSerializer.Serialize(new[]{new{filename=file,file_size=3,sha_hash=Convert.ToHexString(SHA1.HashData(full))}});
        var epic=new StoreCliProvider(StoreKind.Epic,bundle,state,Path.Combine(root,"never-executed.exe"),new HttpClient(new MetadataHttp(_=>throw new Exception("Epic unexpected HTTP"))),
            (_,args,env,_)=>
            {
                check(args.SequenceEqual(new[]{"list-files","original","--json","--platform","Windows"}),"Epic uses actual Windows list-files JSON schema");
                check(env.ContainsKey("LEGENDARY_CONFIG_PATH"),"Epic metadata isolated to own credential scope");
                return Task.FromResult((0,epicJson));
            });
        StoreCredentialVault.Write(Path.Combine(state,"epic","credentials.dpapi"),"{\"account_id\":\"inert-only\",\"access_token\":\"original-fixture\"}");
        var manifest=await ((IStorePartialDownloadSource)epic).ReadPartialManifestAsync("original",default);
        StoreOwnedPartialFiles.ValidateManifest(manifest,root);
        check(manifest.Files.Single().RelativePath==file&&manifest.Files.Single().HashAlgorithm=="SHA1"&&manifest.PartialSuffixes.SequenceEqual(new[]{".tmp"}),"Epic manifest yields exact original files and actual .tmp suffix");
        check(!File.Exists(Path.Combine(state,"epic","native","user.json")),"Epic temporary auth sealed after metadata query");
        int metaCalls=0;
        foreach(int version in new[]{1,2})
        {
            string hash=new('a',32),prefix="https://gog-cdn-fastly.gog.com/content-system/";
            string rootLink=prefix+(version==1?"v1/manifests/100/windows/123/root":"v2/meta/bb/bb/"+new string('b',32));
            string meta=version==1?
                JsonSerializer.Serialize(new{version=1,product=new{rootGameID="100",timestamp=123,depots=new[]{new{gameIDs=new[]{"100"},languages=new[]{"Neutral"},manifest=hash}}}}):
                JsonSerializer.Serialize(new{baseProductId="100",dependencies=Array.Empty<string>(),depots=new[]{new{productId="100",languages=new[]{"*"},manifest=hash,osBitness=Array.Empty<string>()}}});
            string depot=version==1?JsonSerializer.Serialize(new{depot=new{files=new[]{new{path="/"+file,size=3,hash=md5}}}}):
                JsonSerializer.Serialize(new{depot=new{items=new[]{new{type="DepotFile",path=file,chunks=new[]{new{size=3,md5}},flags=Array.Empty<string>()}}}});
            string builds=JsonSerializer.Serialize(new{builds=new{items=new object[]{new{branch="beta",link="https://foreign.invalid/not-selected"},new{branch=(string?)null,link=rootLink,generation=version}}}});
            var network=new MetadataHttp(uri=>
            {
                metaCalls++;
                if(uri.AbsoluteUri==rootLink)return Zip(meta);
                check(uri.AbsoluteUri==prefix+(version==1?$"v1/manifests/100/windows/123/{hash}":$"v2/meta/aa/aa/{hash}"),"GOG only exact declared depot metadata requested");
                return version==1?Encoding.UTF8.GetBytes(depot):Zip(depot);
            });
            var gogState=Path.Combine(state,"gog-v"+version);
            var gog=new StoreCliProvider(StoreKind.Gog,bundle,gogState,Path.Combine(root,"never-executed.exe"),new HttpClient(network),(_,args,_,_)=>
            {
                check(args.Count==7&&args[2]=="info"&&args[3]=="100"&&args[^1]=="--skip-dlcs","GOG uses actual pinned Windows default-build info contract");
                return Task.FromResult((0,builds));
            });
            StoreCredentialVault.Write(Path.Combine(gogState,"gog","credentials.dpapi"),"{\"46899977096215655\":{\"user_id\":\"inert-only\",\"access_token\":\"original-fixture\"}}");
            manifest=await ((IStorePartialDownloadSource)gog).ReadPartialManifestAsync("100",default);
            StoreOwnedPartialFiles.ValidateManifest(manifest,root);
            check(manifest.Files.Single().RelativePath==file&&manifest.Files.Single().Size==3&&manifest.StateFile==".gogdl-resume","GOG v1/v2 retains exact base-game file and resume metadata policy");
            check(version==1?manifest.Files.Single().HashAlgorithm=="MD5":manifest.Files.Single().Chunks?.Single().Hash==md5,"GOG v2 chunk-only checksum is not fabricated as whole-file MD5");
            check(!File.Exists(Path.Combine(gogState,"gog","native","auth.json")),"GOG scope sealed without invoking a downloader");
        }
        check(metaCalls==4,"GOG two exact root/depot pairs, no game payload request");
        var amazonState=Path.Combine(state,"amazon-source");int parsed=0;
        byte[] fixture=[19,20,21];string amazonId="original-amazon";
        var amazon=new StoreAmazonProvider(bundle,amazonState,Path.Combine(root,"never-executed.exe"),new HttpClient(new MetadataHttp(uri=>
        {
            if(uri.AbsoluteUri=="https://gaming.amazon.com/api/distribution/v2/public")return Encoding.UTF8.GetBytes("{\"versionId\":\"original-version\",\"downloadUrl\":\"https://pg.distribution.games.a2z.com/original\"}");
            check(uri.AbsoluteUri=="https://pg.distribution.games.a2z.com/original/manifest.proto","Amazon fetches only signed manifest metadata, not game payload");return fixture;
        })),(_,_,_,_)=>throw new Exception("No CLI for Amazon manifest metadata"),(_,_,_,_,_,_,_)=>throw new Exception("Never start retail worker"),bytes=>
        {
            parsed++;check(bytes.SequenceEqual(fixture),"Amazon actual Plan invokes injected original-signature fixture parser");
            return new(SHA256.HashData(fixture),[new(file,3,1,Shake128.HashData(full,32))]);
        });
        var vault=JsonSerializer.Serialize(new{extensions=new{customer_info=new{user_id="original-inert-account",name="Fixture"}},tokens=new{bearer=new{access_token="original-fixture",refresh_token="original-fixture",expires_in=3600}},NILE=new{token_obtain_time=DateTimeOffset.UtcNow.ToUnixTimeSeconds()}});
        StoreCredentialVault.Write(Path.Combine(amazonState,"amazon","credentials.dpapi"),vault);
        string native=Path.Combine(amazonState,"amazon","native","nile");File.WriteAllText(Path.Combine(native,"library.json"),JsonSerializer.Serialize(new[]{new{id="original-entitlement",product=new{id=amazonId}}}));
        manifest=await ((IStorePartialDownloadSource)amazon).ReadPartialManifestAsync(amazonId,default);
        StoreOwnedPartialFiles.ValidateManifest(manifest,root);
        check(parsed==1&&manifest.Files.Single().HashAlgorithm=="SHAKE128"&&manifest.PartialSuffixes.SequenceEqual(new[]{".patch"}),"Nile signed SHAKE128 preserved, actual .patch policy, no SHA256 substitution");
        check(!File.Exists(Path.Combine(native,"current_user.json")),"Amazon native auth sealed after metadata-only plan");
    }
    static byte[] Zip(string text){using var output=new MemoryStream();using(var zip=new ZLibStream(output,CompressionLevel.Fastest,true))zip.Write(Encoding.UTF8.GetBytes(text));return output.ToArray();}
    sealed class MetadataHttp(Func<Uri,byte[]> reply):HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){RequestMessage=request,Content=new ByteArrayContent(reply(request.RequestUri!))});}
}
