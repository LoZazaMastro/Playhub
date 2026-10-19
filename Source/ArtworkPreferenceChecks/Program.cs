using System.Text.Json.Nodes;
using Playhub.Integrations;

int checks=0;
void Check(bool success,string label){if(!success)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}
var artwork=JsonNode.Parse("""{"defaultProviders":{"cover":"ign","hero":"igdb"},"filters":{"cover_square":{"dimensions":["512x512"],"mimes":["image/png"]},"cover_vertical":{"dimensions":["600x900"]},"hero":{"dimensions":["1920x620"]},"icon":{"dimensions":["256"]}}} """)!.AsObject();
var prefs=new ApplicationArtworkPreferences(artwork);
Check(prefs.DefaultProvider("cover")=="ign","per-type default provider");
Check(prefs.Order("cover",["steamgriddb","ign","xbox"]).SequenceEqual(["ign","steamgriddb","xbox"]),"preferred provider first with requested fallback preserved");
Check(!prefs.Order("cover",["steamgriddb"]).Contains("ign"),"preference does not add an excluded provider to requested acquisition");
Check(prefs.Order("cover",[]).Length==0&&prefs.Order("cover",["unknown"]).Length==0,"explicit empty or invalid provider lists never authorize fallback downloads");
Check(prefs.Query("cover","square").Contains("dimensions=512x512&")&&!prefs.Query("cover","square").Contains("image/jpeg"),"square-only dimensions and PNG query");
Check(prefs.Query("cover","vertical").Contains("600x900")&&!prefs.Query("cover","vertical").Contains("512x512"),"portrait filter independent from square");
Check(prefs.Query("logo","vertical")=="mimes=image/png","logo original dimensions and PNG without unsupported endpoint type filters");
Check(!prefs.Accepts(new(){["width"]=512,["height"]=512,["animated"]=true},"cover","square"),"animated response refused even when provider ignores query");
Check(!prefs.Accepts(new(){["width"]=1024,["height"]=1024},"cover","square"),"response exact dimensions enforced");
Check(prefs.Query("icon","square")=="mimes=image/png"&&prefs.Accepts(new(){["width"]=256,["height"]=128},"icon","square"),"stale icon dimension choices ignored and no size filter is sent");
Check(!ApplicationArtworkPreferences.Supports("ign","hero")&&!ApplicationArtworkPreferences.Supports("igdb","logo")&&!ApplicationArtworkPreferences.Supports("iidb","cover")&&ApplicationArtworkPreferences.Supports("steamgriddb","icon"),"per-type capability matches actual adapters");
Check(prefs.Order("hero",["ign","steamgriddb","alphacoders"]).SequenceEqual(["steamgriddb","alphacoders"]),"unsupported automatic providers cannot enter acquisition order");
Check(new ApplicationArtworkPreferences(JsonNode.Parse("""{"filters":{"cover_square":{"dimensions":["1x1","bad"],"mimes":["image/webp"]}},"defaultProviders":{"hero":"evil"}}""")!.AsObject()).Query("cover","square")==new ApplicationArtworkPreferences().Query("cover","square"),"invalid persisted filters normalize to supported defaults");
Check(ApplicationArtworkPreferences.SteamUsers(new(),["1","2"]).SequenceEqual(["1","2"]),"all accounts default");
Check(ApplicationArtworkPreferences.SteamUsers(new(){["steamUsers"]=new JsonArray("2","999")},["1","2"]).SequenceEqual(["1","2"]),"historical hidden account subset no longer limits import");
Check(ApplicationArtworkPreferences.SteamUsers(new(){["steamUsers"]=new JsonArray()},["1","2"]).Length==2,"historical empty account preference does not hide available users");

Console.WriteLine($"{checks} artwork preference checks passed.");
