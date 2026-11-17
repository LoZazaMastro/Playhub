// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 LoZazaMastro.

// A single depot language can describe a multilingual package; it is not a
// choice of the language used inside the game.
export function hasStoreInstallLanguageChoice(languages:readonly string[]|null|undefined){
 return new Set((languages??[]).map(value=>value.replace(/_/g,"-").toLowerCase())).size>1;
}

export function formatStoreInstallLanguage(language:string,locale:string){
 const code=language.replace(/_/g,"-");
 try{
  const names=new Intl.DisplayNames([locale,"en"],{type:"language"});
  const english=/^en-(US|GB|UK)$/i.exec(code);
  const label=english?`${names.of("en")??"English"} (${english[1].toUpperCase()==="US"?"US":"UK"})`:names.of(code)??language;
  return label.charAt(0).toLocaleUpperCase(locale)+label.slice(1);
 }catch{return language.charAt(0).toUpperCase()+language.slice(1);}
}
