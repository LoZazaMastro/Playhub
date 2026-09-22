import {readFile,writeFile} from 'node:fs/promises';
const path=new URL('./mario-expanded-content.json',import.meta.url);
const proposal=JSON.parse(await readFile(path,'utf8'));
const theme=proposal.theme;
const languages=['de','es','fr','pt','ru','uk','ja','ko','zh','hi'];
const groups=[[theme.title,theme.intro],...theme.chapters.map(c=>[c.kicker,c.title,c.body])];
const pause=ms=>new Promise(resolve=>setTimeout(resolve,ms));
for(let index=0;index<groups.length;index++){
  const fields=groups[index];
  for(const lang of languages){
    if(fields.every(f=>f[lang]&&f[lang]!==f.en))continue;
    const params=new URLSearchParams({client:'gtx',sl:'en',tl:lang==='zh'?'zh-TW':lang,dt:'t',q:fields.map(f=>f.en).join('\n')});
    let complete=false;
    for(let attempt=0;attempt<4&&!complete;attempt++){
      try{
        const response=await fetch(`https://translate.googleapis.com/translate_a/single?${params}`,{signal:AbortSignal.timeout(30000)});
        if(!response.ok)throw new Error(`HTTP ${response.status}`);
        const data=await response.json();
        const lines=data[0].map(x=>x[0]??'').join('').split(/\r?\n/).map(s=>s.trim()).filter(Boolean);
        if(lines.length!==fields.length)throw new Error('line count mismatch');
        fields.forEach((f,i)=>{f[lang]=lines[i];});
        complete=true;
      }catch(error){if(attempt===3)throw error;await pause((attempt+1)*1500);}
    }
    await writeFile(path,JSON.stringify(proposal,null,2)+'\n');
    console.log(`${index} ${lang}`);await pause(150);
  }
}
proposal.translation_review={method:'Italian and English original prose; other locales machine translated with targeted editorial correction',locales:['it','en',...languages],native_review:'not performed'};
await writeFile(path,JSON.stringify(proposal,null,2)+'\n');
