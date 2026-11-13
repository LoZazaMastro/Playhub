// SPDX-License-Identifier: GPL-3.0-only
// Includes the actual parser and native admission, without loading Valve code.
#include "RendererAttestation.cpp"
#include <iostream>
#include <cstdlib>
static unsigned checks;
static void Check(bool value,const char* why){++checks;if(!value){std::cerr<<"FAIL: "<<why<<'\n';std::exit(1);}}
struct Ops {
 int fail{},count{};bool Step(){return ++count!=fail;}
 bool BindSteamRoot(){return Step();}bool OpenRetainedFiles(){return Step();}
 bool TrustValveFiles(){return Step();}bool ReadImageContract(){return Step();}
 bool PinDigest(){return Step();}bool VerifyRetainedIdentity(){return Step();}
 bool VerifyDigest(){return Step();}bool BindLoadedIdentity(){return Step();}
 bool MatchLoadedContract(){return Step();}
};
struct ImageFixture {
 BYTE* base=static_cast<BYTE*>(VirtualAlloc(nullptr,32768,MEM_COMMIT|MEM_RESERVE,PAGE_READWRITE));
 ImageFixture(){
  Check(base!=nullptr,"owned parser memory allocated");auto dos=reinterpret_cast<IMAGE_DOS_HEADER*>(base);dos->e_magic=IMAGE_DOS_SIGNATURE;dos->e_lfanew=128;
  auto nt=reinterpret_cast<IMAGE_NT_HEADERS64*>(base+128);nt->Signature=IMAGE_NT_SIGNATURE;nt->FileHeader.Machine=IMAGE_FILE_MACHINE_AMD64;nt->FileHeader.NumberOfSections=1;nt->FileHeader.SizeOfOptionalHeader=sizeof(IMAGE_OPTIONAL_HEADER64);nt->OptionalHeader.Magic=IMAGE_NT_OPTIONAL_HDR64_MAGIC;nt->OptionalHeader.SizeOfImage=32768;nt->OptionalHeader.SizeOfHeaders=1024;nt->OptionalHeader.NumberOfRvaAndSizes=IMAGE_NUMBEROF_DIRECTORY_ENTRIES;
  auto section=IMAGE_FIRST_SECTION(nt);memcpy(section->Name,".text",5);section->VirtualAddress=0x1000;section->Misc.VirtualSize=0x1000;section->Characteristics=IMAGE_SCN_MEM_EXECUTE|IMAGE_SCN_MEM_READ;
  nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_EXPORT]={0x3000,0x100};auto ex=reinterpret_cast<IMAGE_EXPORT_DIRECTORY*>(base+0x3000);ex->NumberOfFunctions=4;ex->NumberOfNames=4;ex->AddressOfFunctions=0x3100;ex->AddressOfNames=0x3120;ex->AddressOfNameOrdinals=0x3140;
  auto funcs=reinterpret_cast<DWORD*>(base+0x3100),names=reinterpret_cast<DWORD*>(base+0x3120);auto ords=reinterpret_cast<WORD*>(base+0x3140);for(unsigned i=0;i<4;++i){funcs[i]=0x1100+i*16;names[i]=0x3200+i*64;ords[i]=WORD(i);strcpy_s(reinterpret_cast<char*>(base+names[i]),64,view::RequiredExports[i]);}
  nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT]={0x4000,40};auto im=reinterpret_cast<IMAGE_IMPORT_DESCRIPTOR*>(base+0x4000);im->Name=0x4100;strcpy_s(reinterpret_cast<char*>(base+0x4100),32,"USER32.dll");im->OriginalFirstThunk=0x4200;im->FirstThunk=0x4300;
  auto original=reinterpret_cast<ULONGLONG*>(base+0x4200),iat=reinterpret_cast<ULONGLONG*>(base+0x4300);for(unsigned i=0;i<2;++i){original[i]=0x4400+i*64;iat[i]=original[i];strcpy_s(reinterpret_cast<char*>(base+original[i]+2),60,view::RequiredImports[i]);}
 }
 ~ImageFixture(){VirtualFree(base,0,MEM_RELEASE);}
 bool Contract(){view::Image image{};view::Contract c{};return image.Read(base)&&view::ReadContract(image,c);}
};
int main(int argc,char** argv){
 Ops good{};Check(view::admission::File(good)&&good.count==7,"actual ordered file admission completes all seven stages");
 for(int stop=1;stop<=7;++stop){Ops o{};o.fail=stop;Check(!view::admission::File(o)&&o.count==stop,"every native file stage fails closed and prevents later stages");}
 Ops loaded{};Check(view::admission::Loaded(loaded)&&loaded.count==4,"actual loaded admission rechecks identity and digest");for(int stop=1;stop<=4;++stop){Ops o{};o.fail=stop;Check(!view::admission::Loaded(o)&&o.count==stop,"loaded mismatch or late change prevents publication");}
 Check(!view::RendererAttestation::OpenFile(nullptr),"null path refused");Check(!view::RendererAttestation::OpenFile(L"C:\\elsewhere\\gameoverlayrenderer64.dll"),"foreign directory refused before file trust");
 {ImageFixture f;Check(f.Contract(),"actual PE contract accepts complete inert AMD64 fixture");}
 for(unsigned i=0;i<4;++i){ImageFixture f;reinterpret_cast<DWORD*>(f.base+0x3100)[i]=0;Check(!f.Contract(),"each missing export refused");}
 {ImageFixture f;reinterpret_cast<DWORD*>(f.base+0x3100)[0]=0x3020;Check(!f.Contract(),"forwarded export refused");}
 {ImageFixture f;reinterpret_cast<DWORD*>(f.base+0x3100)[0]=0x5000;Check(!f.Contract(),"export outside executable text refused");}
 for(unsigned i=0;i<2;++i){ImageFixture f;f.base[0x4400+i*64+2]='X';Check(!f.Contract(),"each required USER32 API absent refused");}
 {ImageFixture f;reinterpret_cast<IMAGE_IMPORT_DESCRIPTOR*>(f.base+0x4000)->OriginalFirstThunk=0;Check(!f.Contract(),"absent import-name lineage refused");}
 {ImageFixture f;reinterpret_cast<IMAGE_IMPORT_DESCRIPTOR*>(f.base+0x4000)->FirstThunk=32764;Check(!f.Contract(),"out-of-bounds import slot refused");}
 {ImageFixture f;auto nt=reinterpret_cast<IMAGE_NT_HEADERS64*>(f.base+128);nt->FileHeader.Machine=IMAGE_FILE_MACHINE_I386;Check(!f.Contract(),"foreign architecture refused");}
 {ImageFixture f;auto nt=reinterpret_cast<IMAGE_NT_HEADERS64*>(f.base+128);nt->OptionalHeader.SizeOfImage=0xffffffff;Check(!f.Contract(),"excessive image span refused");}
 {ImageFixture a,b;view::Image disk{},memory{};view::Contract c{};Check(disk.Read(a.base)&&memory.Read(b.base)&&view::ReadContract(disk,c),"real contract acquired for loaded negative");reinterpret_cast<IMAGE_NT_HEADERS64*>(b.base+128)->FileHeader.TimeDateStamp=7;Check(!view::SameContract(disk,c,memory,GetModuleHandleW(nullptr)),"loaded PE metadata mismatch refused before export invocation");}
 {ImageFixture a,b;view::Image disk{},memory{};view::Contract c{};Check(disk.Read(a.base)&&memory.Read(b.base)&&view::ReadContract(disk,c),"real contract acquired for layout negative");reinterpret_cast<IMAGE_IMPORT_DESCRIPTOR*>(b.base+0x4000)->FirstThunk=0x4308;Check(!view::SameContract(disk,c,memory,GetModuleHandleW(nullptr)),"loaded import slot layout drift refused");}
 wchar_t temp[32768]{},first[32768]{},second[32768]{},moved[32768]{};Check(GetTempPathW(32768,temp)&&GetTempFileNameW(temp,L"phr",0,first)&&GetTempFileNameW(temp,L"phr",0,second)&&GetTempFileNameW(temp,L"phr",0,moved),"private identity-test files created");
 {
  view::Handle held(CreateFileW(first,GENERIC_READ,FILE_SHARE_READ|FILE_SHARE_WRITE|FILE_SHARE_DELETE,nullptr,OPEN_EXISTING,FILE_ATTRIBUTE_NORMAL,nullptr));FILE_ID_INFO before{};Check(held.Valid()&&view::Identity(held.value,before),"actual initial private file identity acquired");auto path=view::Final(held.value);Check(view::PathStillNames(path,held.value,before,false),"same private file identity accepts");FILE_ID_INFO wrong=before;wrong.FileId.Identifier[0]^=0x80;Check(!view::PathStillNames(path,held.value,wrong,false),"changed 128-bit file identity refuses");
  Check(MoveFileExW(first,moved,MOVEFILE_REPLACE_EXISTING)&&MoveFileExW(second,first,MOVEFILE_REPLACE_EXISTING),"only private test path replaced while original handle retained");Check(!view::PathStillNames(path,held.value,before,false),"actual replaced path refuses retained receipt");
 }
 Check(DeleteFileW(first)&&DeleteFileW(moved),"private replacement test files removed");
 {
  view::Handle file(CreateFileW(second,GENERIC_READ|GENERIC_WRITE,FILE_SHARE_READ,nullptr,CREATE_NEW,FILE_ATTRIBUTE_TEMPORARY,nullptr));Check(file.Valid(),"private digest fixture created");DWORD bytes{};const char initial[]="one";Check(WriteFile(file.value,initial,3,&bytes,nullptr)&&bytes==3,"private initial bytes written");auto digest=view::Sha(file.value);Check(digest.size()==64,"actual digest helper pins private bytes");LARGE_INTEGER start{};Check(SetFilePointerEx(file.value,start,nullptr,FILE_BEGIN)&&WriteFile(file.value,"two",3,&bytes,nullptr)&&bytes==3,"private fixture changed before recheck");Check(view::Sha(file.value)!=digest,"changed file bytes refuse pinned digest");
 }
 Check(DeleteFileW(second)!=FALSE,"private digest fixture removed");
 wchar_t own[32768]{};GetModuleFileNameW(nullptr,own,32768);view::Handle unsignedFile(CreateFileW(own,GENERIC_READ,FILE_SHARE_READ,nullptr,OPEN_EXISTING,FILE_ATTRIBUTE_NORMAL,nullptr));Check(unsignedFile.Valid()&&!view::SignedValve(own,unsignedFile.value),"actual unsigned own executable rejected by trust verifier");
 wchar_t system[32768]{};GetSystemDirectoryW(system,32768);std::wstring foreign=std::wstring(system)+L"\\kernel32.dll";view::Handle other(CreateFileW(foreign.c_str(),GENERIC_READ,FILE_SHARE_READ,nullptr,OPEN_EXISTING,FILE_ATTRIBUTE_NORMAL,nullptr));Check(other.Valid()&&!view::SignedValve(foreign,other.value),"actual trusted foreign publisher rejected");
 if(argc==2&&strcmp(argv[1],"--installed-read-only")==0){
 auto root=view::RegisteredRoot();Check(!root.empty(),"registered installed Steam root available");auto renderer=root+L"\\gameoverlayrenderer64.dll";auto proof=view::RendererAttestation::OpenFile(renderer.c_str());Check(!!proof,"actual Valve renderer passes retained file signature/name/PE/export/import admission");Check(strlen(proof->Digest())==64&&proof->VerifyUnchanged(),"actual renderer runtime digest and retained identities rechecked");Check(!proof->VerifyLoaded(GetModuleHandleW(nullptr)),"actual foreign loaded module rejected");
 view::Handle attemptedWrite(CreateFileW(renderer.c_str(),GENERIC_WRITE,FILE_SHARE_READ,nullptr,OPEN_EXISTING,FILE_ATTRIBUTE_NORMAL,nullptr));Check(!attemptedWrite.Valid()&&GetLastError()==ERROR_SHARING_VIOLATION,"retained renderer forbids write handles without modifying it");
 view::Handle attemptedDelete(CreateFileW(renderer.c_str(),DELETE,FILE_SHARE_READ,nullptr,OPEN_EXISTING,FILE_ATTRIBUTE_NORMAL,nullptr));Check(!attemptedDelete.Valid()&&GetLastError()==ERROR_SHARING_VIOLATION,"retained renderer forbids replace/delete handles without modifying it");
 }
 std::cout<<checks<<" renderer admission checks PASS; no Valve DLL loaded or executed\n";return 0;
}
