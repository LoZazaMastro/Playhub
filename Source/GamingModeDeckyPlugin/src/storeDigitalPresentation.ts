// SPDX-License-Identifier: MIT. Display projections never rename Steam shortcuts or catalog records.
import {readStoreLibrary,type StoreJob,type StoreKey} from "./storeLibraryRuntime";
import {type StoreDigitalGame,sameStoreKey} from "./StoreDigitalModels";

type NamedStoreItem={key:StoreKey;title:string;accountRevision:number};
const ownedShortcut=(item:NamedStoreItem)=>[...readStoreLibrary().values()].find(row=>row.accountCurrent&&row.game?.accountRevision===item.accountRevision&&sameStoreKey(row.key,item.key));
export function storeDigitalDisplayTitle(item:NamedStoreItem):string{
 const owned=ownedShortcut(item);if(!owned)return item.title;
 try{const name=(globalThis as any).appStore?.GetAppOverviewByAppID?.(owned.appId)?.display_name;return typeof name==="string"&&name.trim()?name:item.title;}catch{return item.title;}
}
export function presentStoreDigitalGames(games:readonly StoreDigitalGame[],jobs:readonly StoreJob[]=[]):StoreDigitalGame[]{
 const winners=new Map<string,StoreDigitalGame>(),packageKey=(game:StoreDigitalGame)=>{
  // Only exact installed Xbox package directories establish an alias. Titles,
  // drive roots, other stores and uninstalled catalog entries never do.
  const path=game.installPath?.replace(/\//g,"\\").replace(/\\+$/g,"").toLowerCase();
  return game.key.store==="xbox"&&game.isInstalled&&path&&/\\windowsapps\\[^\\]+$/.test(path)?JSON.stringify([game.accountRevision,path]):undefined;
 };
 const rank=(game:StoreDigitalGame)=>(ownedShortcut(game)?100:0)+(jobs.some(job=>sameStoreKey(job.key,game.key)&&job.accountRevision===game.accountRevision&&job.libraryKind==="gamePass")?20:0)+(game.libraryKind==="gamePass"?10:0);
 for(const game of games){const key=packageKey(game);if(!key)continue;const old=winners.get(key);if(!old||rank(game)>rank(old))winners.set(key,game);}
 return games.filter(game=>{const key=packageKey(game);return !key||winners.get(key)===game;}).map(game=>{const title=game.isInstalled?storeDigitalDisplayTitle(game):game.title;return title===game.title?game:{...game,title};});
}
