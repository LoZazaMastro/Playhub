// SPDX-License-Identifier: MIT
#pragma once
#include <charconv>
#include <cstdint>
#include <functional>
#include <optional>
#include <string>
#include <string_view>
#include <vector>
#include <utility>

namespace progress {
struct Target {std::string id;std::uint64_t hwnd{},birth{};std::uint32_t pid{};std::wstring image,windowClass;};
struct Row {
 int index{},parent{-1},depth{},controlType{};
 bool focusable{},offscreen{},enabled{},progressPattern{};
 std::optional<std::string> automationId,className;
 bool invokeAvailable{},selectionItemAvailable{},expandCollapseAvailable{},isPassword{};
};
struct Result {bool complete{};std::string reason;std::vector<Row> rows;};
template<class T> bool Decimal(std::string_view value,T& result){
 if(value.empty()||value.front()=='+'||value.front()=='-')return false;
 auto parsed=std::from_chars(value.data(),value.data()+value.size(),result);
 return parsed.ec==std::errc{}&&parsed.ptr==value.data()+value.size()&&result>0;
}
inline bool HexId(std::string_view id){
 if(id.size()!=32)return false;
 for(char c:id)if(!((c>='0'&&c<='9')||(c>='a'&&c<='f')||(c>='A'&&c<='F')))return false;
 return true;
}
inline std::optional<std::string> Identifier(std::wstring_view value){
 if(value.size()>64)return std::nullopt;
 std::string out;out.reserve(value.size());
 for(wchar_t c:value){
  if(!((c>=L'A'&&c<=L'Z')||(c>=L'a'&&c<=L'z')||(c>=L'0'&&c<=L'9')||c==L'_'||c==L'-'||c==L'.'||c==L':'))return std::nullopt;
  out.push_back(static_cast<char>(c));
 }return out;
}
inline bool DecodeWide(std::string_view encoded,std::wstring& value){
 if(encoded.empty()||encoded.size()>10924||encoded.size()%4)return false;
 auto number=[](char c)->int {if(c>='A'&&c<='Z')return c-'A';if(c>='a'&&c<='z')return c-'a'+26;
  if(c>='0'&&c<='9')return c-'0'+52;if(c=='+')return 62;if(c=='/')return 63;return -1;};
 std::vector<unsigned char> bytes;
 for(std::size_t i=0;i<encoded.size();i+=4){
  int a=number(encoded[i]),b=number(encoded[i+1]);if(a<0||b<0)return false;
  bool two=encoded[i+2]=='=',one=encoded[i+3]=='=';
  if((two&&!one)||((two||one)&&i+4!=encoded.size()))return false;
  int c=two?0:number(encoded[i+2]),d=one?0:number(encoded[i+3]);if(c<0||d<0)return false;
  if((two&&(b&15))||(one&&!two&&(c&3)))return false;
  bytes.push_back(static_cast<unsigned char>((a<<2)|(b>>4)));
  if(!two)bytes.push_back(static_cast<unsigned char>((b<<4)|(c>>2)));
  if(!one)bytes.push_back(static_cast<unsigned char>((c<<6)|d));
 }
 if(bytes.empty()||bytes.size()%2)return false;value.clear();
 for(std::size_t i=0;i<bytes.size();i+=2){
  auto c=static_cast<wchar_t>(static_cast<unsigned>(bytes[i])|(static_cast<unsigned>(bytes[i+1])<<8));
  if(c<32||c==127)return false;value.push_back(c);
 }return true;
}
inline bool Parse(std::string_view line,Target& target){
 if(line.empty()||line.size()>16384)return false;
 std::vector<std::string_view> fields;std::size_t start=0;
 for(std::size_t i=0;i<=line.size();++i)if(i==line.size()||line[i]=='\t'){fields.push_back(line.substr(start,i-start));start=i+1;}
 if(fields.size()!=6||!HexId(fields[0])||!Decimal(fields[1],target.hwnd)
  ||!Decimal(fields[2],target.pid)||!Decimal(fields[3],target.birth)
  ||!DecodeWide(fields[4],target.image)||!DecodeWide(fields[5],target.windowClass))return false;
 if(target.image.size()<3||target.image.size()>4096||target.windowClass.empty()||target.windowClass.size()>256
  ||!((target.image[0]>=L'A'&&target.image[0]<=L'Z')||(target.image[0]>=L'a'&&target.image[0]<=L'z'))
  ||target.image[1]!=L':'||(target.image[2]!=L'\\'&&target.image[2]!=L'/'))return false;
 target.id=std::string(fields[0]);return true;
}
struct Identity {std::uint64_t hwnd{},birth{};std::uint32_t pid{},thread{},session{};bool alive{},root{};std::wstring image,windowClass;};
inline bool Match(const Target& t,const Identity& i,std::uint32_t thread,std::uint32_t session){
 return i.alive&&i.root&&i.hwnd==t.hwnd&&i.pid==t.pid&&i.birth==t.birth&&i.thread&&i.thread==thread
  &&i.session==session&&i.image==t.image&&i.windowClass==t.windowClass;
}
template<class Node,class Reader> Result Scan(Node root,Reader& reader,const std::function<bool()>& current){
 Result result;
 if(!current()){result.reason="identity-before";return result;}
 std::function<bool(Node,int,int)> visit=[&](Node node,int depth,int parent){
  if(result.rows.size()>=64){result.reason="element-cap";return false;}
  Row facts;if(!reader.Facts(node,facts)){result.reason="provider-facts";return false;}
  facts.index=static_cast<int>(result.rows.size());facts.parent=parent;facts.depth=depth;
  result.rows.push_back(facts);Node child;bool has=false;
  if(!reader.First(node,child,has)){result.reason="provider-child";return false;}
  if(has&&depth>=5){result.reason="depth-cap";return false;}
  while(has){if(!visit(child,depth+1,facts.index))return false;Node sibling;
   if(!reader.Next(child,sibling,has)){result.reason="provider-sibling";return false;}child=std::move(sibling);}
  return true;
 };
 if(!visit(root,0,-1))return result;
 if(!current()){result.reason="identity-after";return result;}
 result.complete=true;return result;
}
inline std::string Quoted(const std::optional<std::string>& value){return value?'"'+*value+'"':"null";}
inline std::string Json(const Result& r){
 int progressBars=0,interactive=0;
 if(r.complete)for(const auto& row:r.rows){
  if(row.controlType==50012&&row.enabled&&!row.offscreen)++progressBars;
  if((row.controlType==50000||row.controlType==50004)&&row.enabled&&row.focusable&&!row.offscreen)++interactive;
 }
 const char* state=!r.complete?"unknown":interactive?"interactive-controls-observed":progressBars?"busy-progress-evidence":"no-positive-busy-evidence";
 auto boolean=[](bool v){return v?"true":"false";};
 std::string out="{\"state\":\""+std::string(state)+"\",\"complete\":"+boolean(r.complete)
  +",\"reason\":"+(r.reason.empty()?"null":'"'+r.reason+'"')+",\"visibleProgressBars\":"+std::to_string(progressBars)
  +",\"interactiveEditOrButtons\":"+std::to_string(interactive)+",\"elements\":[";
 bool first=true;for(const auto& row:r.rows){if(!first)out+=',';first=false;
  out+="{\"index\":"+std::to_string(row.index)+",\"parent\":"+std::to_string(row.parent)+",\"depth\":"+std::to_string(row.depth)
   +",\"controlType\":"+std::to_string(row.controlType)+",\"focusable\":"+boolean(row.focusable)
   +",\"offscreen\":"+boolean(row.offscreen)+",\"enabled\":"+boolean(row.enabled)+",\"progressPattern\":"+boolean(row.progressPattern)
   +",\"automationId\":"+Quoted(row.automationId)+",\"className\":"+Quoted(row.className)
   +",\"invokeAvailable\":"+boolean(row.invokeAvailable)+",\"selectionItemAvailable\":"+boolean(row.selectionItemAvailable)
   +",\"expandCollapseAvailable\":"+boolean(row.expandCollapseAvailable)+",\"isPassword\":"+boolean(row.isPassword)+'}';
 }return out+"],\"coverAuthorized\":false,\"authenticationDistinguished\":false}";
}
}
