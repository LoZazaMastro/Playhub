import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import vm from 'node:vm';
import test from 'node:test';
import ts from 'typescript';
const exports={};
vm.runInNewContext(ts.transpileModule(readFileSync(new URL('../src/historyMedia.ts',import.meta.url),'utf8'),{compilerOptions:{module:ts.ModuleKind.CommonJS}}).outputText,{exports});
const {orderHistoryImages}=exports;
const all=[{file:'portrait.png',url:'portrait',subject:'Creator',kind:'portrait'},{file:'early.png',url:'early',subject:'Creator',kind:'portrait'},{file:'game.png',url:'game',subject:'Game',kind:'screenshot'},{file:'cover.png',url:'cover',subject:'Game',kind:'cover'}];
test('chapter keeps assigned photograph regardless of chapter position',()=>{
 for(let index=1;index<10;index++) assert.equal(orderHistoryImages(all,{subject:'Creator',image_file:'early.png'},index)[0].url,'early');
});
test('missing assigned image never borrows a different subject or photograph',()=>{
 assert.equal(orderHistoryImages(all,{subject:'Creator',image_file:'game.png'},1).length,0);
 assert.equal(orderHistoryImages(all,{image_file:'missing.png'},1).length,0);
});
test('game chapter excludes cover and creator portraits',()=>{
 const result=orderHistoryImages(all,{subject:'Game',image_role:'game'},3);
 assert.equal(result.length,1);assert.equal(result[0].url,'game');
});
test('cover is available only when explicitly requested',()=>{
 assert.equal(orderHistoryImages(all,{subject:'Game',image_role:'cover'},3)[0].url,'cover');
});


test('dedicated opening image preserves all chapter assignments',()=>{
 const chapters=[{image_file:'early.png'},{image_file:'game.png'}];
 assert.equal(orderHistoryImages(all,undefined,0,chapters,{image_file:'portrait.png'})[0].file,'portrait.png');
 assert.equal(orderHistoryImages(all,chapters[0],1,chapters,{image_file:'portrait.png'})[0].file,'early.png');
 assert.equal(orderHistoryImages(all,chapters[1],2,chapters,{image_file:'portrait.png'})[0].file,'game.png');
});
test('only explicitly assigned opening cover is allowed',()=>{
 const chapters=[{image_file:'early.png'},{image_file:'game.png'}];
 assert.equal(orderHistoryImages(all,undefined,0,chapters,{image_file:'cover.png',image_role:'cover'})[0].file,'cover.png');
 assert.equal(orderHistoryImages(all,undefined,0,chapters,{image_file:'cover.png'}).length,0);
 assert.equal(orderHistoryImages(all,undefined,0,undefined,{image_file:'cover.png',image_role:'cover'})[0].file,'cover.png');
});
test('localized label does not change canonical image binding',()=>{
 assert.equal(orderHistoryImages(all,{subject:'Autore',image_subject:'Creator',image_file:'early.png'},1)[0].file,'early.png');
 assert.equal(orderHistoryImages(all,{subject:'Autore',image_subject:'Creator',image_file:'game.png'},1).length,0);
});
test('opening fallback can use spare photos but cannot repeat a chapter image',()=>{
 const chapters=[{image_file:'game.png'}];
 assert.deepEqual(Array.from(orderHistoryImages(all,undefined,0,chapters),x=>x.file),['portrait.png','early.png']);
 const positional=[{subject:'Creator'}];
 assert.deepEqual(Array.from(orderHistoryImages(all,undefined,0,positional),x=>x.file),['portrait.png','game.png']);
});
test('all chapter images being reserved does not silently duplicate them for the opening',()=>{
 const chapters=[{image_file:'portrait.png'},{image_file:'early.png'},{image_file:'game.png'}];
 assert.equal(orderHistoryImages(all,undefined,0,chapters).length,0);
});

test('game release heading uses its explicit cover independently from story media',()=>{
 assert.equal(exports.historyHeadingCover(all,'game','cover.png').file,'cover.png');
 assert.equal(exports.historyHeadingCover(all,'game').file,'cover.png');
 assert.equal(exports.historyHeadingCover(all,'author'),undefined);
 assert.equal(exports.historyHeadingCover(all,'game','missing.png'),undefined);
 assert.equal(exports.historyHeadingCover(all.filter(x=>x.kind!=='cover'),'game'),undefined);
});

test('imported release kinds retain their heading covers',()=>{for(const kind of ['release','uscita','uscita_regionale','uscita_mondiale','release_regional']) assert.equal(exports.historyHeadingCover(all,kind).file,'cover.png');});
