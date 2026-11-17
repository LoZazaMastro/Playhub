// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 LoZazaMastro.
import type {StoreDigitalGame} from "./StoreDigitalModels";

export function digitalCoverSources(game:StoreDigitalGame):string[]{
 if(game.artworkKind!=="cover")return[];
 return [...new Set([game.artworkUrl,...game.artworkFallbackUrls??[]].filter((url):url is string=>typeof url==="string"&&url.length>0))].slice(0,4);
}
const warmed=new Set<string>(),waiting:string[]=[];let active=0;
function drain(){if(typeof Image==="undefined")return;while(active<4&&waiting.length){const url=waiting.shift()!;active++;const image=new Image();image.decoding="async";const finish=()=>{active--;image.onload=null;image.onerror=null;drain();};image.onload=finish;image.onerror=finish;image.src=url;}}
// Browser HTTP cache supplies persistence. This queue limits speculative work;
// actual cards still own their load/fallback state and never substitute a logo.
export function warmDigitalCovers(games:readonly StoreDigitalGame[]){if(typeof Image==="undefined")return;for(const game of games.slice(0,12)){const url=digitalCoverSources(game)[0];if(!url||warmed.has(url)||waiting.length>=24)continue;warmed.add(url);if(warmed.size>256)warmed.delete(warmed.values().next().value!);waiting.push(url);}drain();}
