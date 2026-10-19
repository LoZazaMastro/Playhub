using System.Text.Json.Nodes;
using Playhub.Integrations;
int passed=0;
void Check(bool value,string name){if(!value)throw new Exception(name);passed++;Console.WriteLine("PASS "+name);}
Check(ArtworkLogoPolicy.IsZazaHero("hero","steamgriddb","ZazaMastro",null),"exact trusted author name matches case independently");
Check(ArtworkLogoPolicy.IsZazaHero("hero","SteamGridDB","LoZazaMastro",null),"existing authoritative author alias supported");
Check(ArtworkLogoPolicy.IsZazaHero("hero","steamgriddb",null,"76561198128354791"),"exact known author Steam ID supported");
Check(!ArtworkLogoPolicy.IsZazaHero("banner","steamgriddb","zazamastro",null),"banner does not trigger hero author rule");
Check(!ArtworkLogoPolicy.IsZazaHero("hero","ign","zazamastro",null),"other provider cannot impersonate SteamGridDB author");
Check(!ArtworkLogoPolicy.IsZazaHero("hero",null,"https://cdn2.steamgriddb.com/zazamastro.jpg",null),"URL is not guessed as author metadata");
Check(!ArtworkLogoPolicy.IsZazaHero("hero","steamgriddb","not-zazamastro",null),"substring author resemblance is insufficient");
Check(ArtworkLogoPolicy.AuthorName(new JsonObject{["name"]=42}) is null,"malformed author name is not promoted");
Check(ArtworkLogoPolicy.AuthorName(new JsonObject{["name"]="zazamastro"})=="zazamastro","actual API author object projected");
Check(ArtworkLogoPolicy.AuthorSteamId(new JsonObject{["steam64"]="76561198128354791"})=="76561198128354791","actual API Steam ID projected");
var state=new JsonObject{["hero_source"]=new JsonObject{["provider"]="steamgriddb",["authorName"]="zazamastro",["sha256"]="ABC"}};
Check(ArtworkLogoPolicy.HasVerifiedAuthorHero(state,"ABC"),"automatic hide requires matching actual chosen output hash");
Check(!ArtworkLogoPolicy.HasVerifiedAuthorHero(state,"DEF"),"replaced artwork cannot inherit old author visibility");
state["steamLogoManualOverride"]=false;
Check(ArtworkLogoPolicy.ManualOverride(state)==false&&!ArtworkLogoPolicy.HasVerifiedAuthorHero(state,"ABC"),"manual visible choice defeats author automatic hide");
state["steamLogoManualOverride"]=true;
Check(ArtworkLogoPolicy.ManualOverride(state)==true&&ArtworkLogoPolicy.HasVerifiedAuthorHero(state,"ABC"),"manual hide is retained");
state.Remove("steamLogoManualOverride");state["perfect_hero_hideSteamLogo"]=false;
Check(ArtworkLogoPolicy.ManualOverride(state)==false,"older explicit visible preference retained");

Console.WriteLine($"{passed} artwork author and logo policy checks passed.");
