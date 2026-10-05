import assert from "node:assert/strict";
import {readFileSync} from "node:fs";
import vm from "node:vm";
import test from "node:test";
import ts from "typescript";
const source=readFileSync(new URL("../src/DashboardPage.tsx",import.meta.url),"utf8");
const ast=ts.createSourceFile("DashboardPage.tsx",source,ts.ScriptTarget.Latest,true,ts.ScriptKind.TSX);
let expression;
const visit=node=>{if(ts.isVariableDeclaration(node)&&node.name.getText(ast)==="EXTRA_COPY")expression=node.initializer;ts.forEachChild(node,visit);};visit(ast);
const copy=vm.runInNewContext('('+ts.createPrinter().printNode(ts.EmitHint.Expression,expression,ast)+')');
test("SDL3 app option label and description are usable UTF8 in every supported locale",()=>{
  for(const locale of ["en","it","es","fr","de","pt","uk","zh","ja","ko","hi","ru"]){
    assert.ok(copy[locale].sdl3.trim().length>4,locale);
    assert.ok(copy[locale].sdl3Description.length>20,locale);
    assert.match(copy[locale].sdl3Description,/SDL3/,locale);
    assert.doesNotMatch(copy[locale].sdl3+copy[locale].sdl3Description,/\uFFFD|Ã|Â|Ð|Ñ/,locale);
  }
});
test("actual ToggleField uses selected locale description with existing English fallback",()=>{
  assert.match(source,/<ToggleField label=\{extra.sdl3\} description=\{extra.sdl3Description\}/);
  assert.match(source,/const extra = EXTRA_COPY\[steamLocale\] \?\? EXTRA_COPY.en/);
  assert.equal((copy.unknown??copy.en).sdl3Description,"Launch this game with native SDL3 controller detection.");
  assert.doesNotMatch(source,/description="Avvia questo gioco/);
});
