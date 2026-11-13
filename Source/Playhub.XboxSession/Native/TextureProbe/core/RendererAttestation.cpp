// SPDX-License-Identifier: GPL-3.0-only
#include "RendererAttestation.h"
#include "RendererAdmissionPolicy.h"
#include <wintrust.h>
#include <softpub.h>
#include <wincrypt.h>
#include <bcrypt.h>
#include <array>
#include <vector>
#include <string>
#include <cstring>
#include <cwchar>
#include <algorithm>
#include <mutex>
namespace view {
namespace {
struct Handle {
 HANDLE value=INVALID_HANDLE_VALUE;
 Handle()=default;explicit Handle(HANDLE h):value(h){}
 ~Handle(){if(value!=INVALID_HANDLE_VALUE&&value)CloseHandle(value);}
 Handle(const Handle&)=delete;Handle& operator=(const Handle&)=delete;
 bool Valid()const{return value!=INVALID_HANDLE_VALUE&&value;}
};
bool Equal(const std::wstring& a,const std::wstring& b){return !a.empty()&&!b.empty()&&_wcsicmp(a.c_str(),b.c_str())==0;}
std::wstring Final(HANDLE h){wchar_t text[32768]{};DWORD n=GetFinalPathNameByHandleW(h,text,32768,FILE_NAME_NORMALIZED|VOLUME_NAME_DOS);if(!n||n>=32768)return {};std::wstring p(text,n);if(p.rfind(L"\\\\?\\",0)==0)p.erase(0,4);return p;}
bool Identity(HANDLE h,FILE_ID_INFO& id){return GetFileInformationByHandleEx(h,FileIdInfo,&id,sizeof(id))!=FALSE;}
bool SameId(const FILE_ID_INFO& a,const FILE_ID_INFO& b){return a.VolumeSerialNumber==b.VolumeSerialNumber&&memcmp(a.FileId.Identifier,b.FileId.Identifier,sizeof(a.FileId.Identifier))==0;}
bool Normal(HANDLE h,bool directory){FILE_ATTRIBUTE_TAG_INFO info{};return GetFileInformationByHandleEx(h,FileAttributeTagInfo,&info,sizeof(info))&&!(info.FileAttributes&FILE_ATTRIBUTE_REPARSE_POINT)&&!!(info.FileAttributes&FILE_ATTRIBUTE_DIRECTORY)==directory;}
bool PathStillNames(const std::wstring& p,HANDLE held,const FILE_ID_INFO& expected,bool directory){
 FILE_ID_INFO now{};if(!Identity(held,now)||!SameId(now,expected)||!Equal(Final(held),p))return false;
 Handle current(CreateFileW(p.c_str(),GENERIC_READ,FILE_SHARE_READ|FILE_SHARE_WRITE,nullptr,OPEN_EXISTING,FILE_FLAG_OPEN_REPARSE_POINT|(directory?FILE_FLAG_BACKUP_SEMANTICS:0),nullptr));
 return current.Valid()&&Normal(current.value,directory)&&Identity(current.value,now)&&SameId(now,expected)&&Equal(Final(current.value),p);
}
std::wstring RegisteredRoot(){
 struct Key{HKEY hive;const wchar_t* key;const wchar_t* name;};
 const Key keys[]={{HKEY_CURRENT_USER,L"Software\\Valve\\Steam",L"SteamPath"},{HKEY_LOCAL_MACHINE,L"Software\\Valve\\Steam",L"InstallPath"},{HKEY_LOCAL_MACHINE,L"Software\\WOW6432Node\\Valve\\Steam",L"InstallPath"}};
 for(const auto& k:keys){wchar_t text[32768]{};DWORD bytes=sizeof(text),type{};if(RegGetValueW(k.hive,k.key,k.name,RRF_RT_REG_SZ,&type,text,&bytes)==ERROR_SUCCESS){std::wstring p(text);std::replace(p.begin(),p.end(),L'/',L'\\');while(p.size()>3&&p.back()==L'\\')p.pop_back();if(p.size()>3&&p[1]==L':'&&p[2]==L'\\')return p;}}
 return {};
}
bool SignedValve(const std::wstring& path,HANDLE h){
 WINTRUST_FILE_INFO file{sizeof(file)};file.pcwszFilePath=path.c_str();file.hFile=h;
 WINTRUST_DATA data{sizeof(data)};data.dwUIChoice=WTD_UI_NONE;data.fdwRevocationChecks=WTD_REVOKE_NONE;data.dwUnionChoice=WTD_CHOICE_FILE;data.pFile=&file;data.dwStateAction=WTD_STATEACTION_VERIFY;data.dwProvFlags=WTD_CACHE_ONLY_URL_RETRIEVAL|WTD_REVOCATION_CHECK_NONE;
 GUID action=WINTRUST_ACTION_GENERIC_VERIFY_V2;bool valid=false;
 if(WinVerifyTrust(nullptr,&action,&data)==ERROR_SUCCESS){auto p=WTHelperProvDataFromStateData(data.hWVTStateData);auto s=p?WTHelperGetProvSignerFromChain(p,0,FALSE,0):nullptr;
  if(s&&s->csCertChain&&s->pasCertChain[0].pCert){wchar_t name[128]{},org[128]{};auto cert=s->pasCertChain[0].pCert;DWORD a=CertGetNameStringW(cert,CERT_NAME_SIMPLE_DISPLAY_TYPE,0,nullptr,name,128),b=CertGetNameStringW(cert,CERT_NAME_ATTR_TYPE,0,const_cast<char*>(szOID_ORGANIZATION_NAME),org,128);valid=a>1&&a<=128&&b>1&&b<=128&&wcscmp(name,L"Valve Corp.")==0&&wcscmp(org,L"Valve Corp.")==0;}}
 data.dwStateAction=WTD_STATEACTION_CLOSE;WinVerifyTrust(nullptr,&action,&data);return valid;
}
bool OriginalName(const std::wstring& path,const wchar_t* expected){
 DWORD ignored{},size=GetFileVersionInfoSizeW(path.c_str(),&ignored);if(!size||size>1024*1024)return false;std::vector<BYTE> data(size);if(!GetFileVersionInfoW(path.c_str(),0,size,data.data()))return false;
 struct Lang{WORD language,codepage;};Lang* langs{};UINT n{};if(!VerQueryValueW(data.data(),L"\\VarFileInfo\\Translation",reinterpret_cast<void**>(&langs),&n)||n<sizeof(Lang)||n>1024||n%sizeof(Lang))return false;
 for(unsigned i=0;i<n/sizeof(Lang);++i){wchar_t key[128]{};swprintf_s(key,L"\\StringFileInfo\\%04x%04x\\OriginalFilename",langs[i].language,langs[i].codepage);wchar_t* value{};UINT count{};if(VerQueryValueW(data.data(),key,reinterpret_cast<void**>(&value),&count)&&count>1&&count<256&&value[count-1]==0&&_wcsicmp(value,expected)==0)return true;}
 return false;
}
std::string Sha(HANDLE file){
 BCRYPT_ALG_HANDLE alg{};BCRYPT_HASH_HANDLE hash{};if(BCryptOpenAlgorithmProvider(&alg,BCRYPT_SHA256_ALGORITHM,nullptr,0)<0)return {};std::string result;
 if(BCryptCreateHash(alg,&hash,nullptr,0,nullptr,0,0)>=0){LARGE_INTEGER start{};bool ok=SetFilePointerEx(file,start,nullptr,FILE_BEGIN)!=FALSE;std::array<BYTE,65536> buffer{};DWORD n{};while(ok){if(!ReadFile(file,buffer.data(),DWORD(buffer.size()),&n,nullptr)){ok=false;break;}if(!n)break;if(BCryptHashData(hash,buffer.data(),n,0)<0)ok=false;}BYTE digest[32]{};if(ok&&BCryptFinishHash(hash,digest,32,0)>=0){constexpr char hex[]="0123456789abcdef";result.resize(64);for(unsigned i=0;i<32;++i){result[i*2]=hex[digest[i]>>4];result[i*2+1]=hex[digest[i]&15];}}BCryptDestroyHash(hash);}
 BCryptCloseAlgorithmProvider(alg,0);return result;
}
bool Readable(const void* address,size_t length){
 auto at=reinterpret_cast<uintptr_t>(address);if(!at||length>UINTPTR_MAX-at)return false;auto end=at+length;
 while(at<end){MEMORY_BASIC_INFORMATION m{};if(!VirtualQuery(reinterpret_cast<void*>(at),&m,sizeof(m))||m.State!=MEM_COMMIT||(m.Protect&(PAGE_NOACCESS|PAGE_GUARD)))return false;auto base=reinterpret_cast<uintptr_t>(m.BaseAddress);if(m.RegionSize>UINTPTR_MAX-base||base+m.RegionSize<=at)return false;at=(std::min)(end,base+m.RegionSize);}return true;
}
struct Image {
 const BYTE* base{};DWORD size{};const IMAGE_NT_HEADERS64* nt{};const IMAGE_SECTION_HEADER* sections{};
 bool Read(const BYTE* p){
  base=p;if(!Readable(p,sizeof(IMAGE_DOS_HEADER)))return false;auto d=reinterpret_cast<const IMAGE_DOS_HEADER*>(p);if(d->e_magic!=IMAGE_DOS_SIGNATURE||d->e_lfanew<sizeof(*d)||d->e_lfanew>1024*1024)return false;
  nt=reinterpret_cast<const IMAGE_NT_HEADERS64*>(p+d->e_lfanew);if(!Readable(nt,sizeof(*nt))||nt->Signature!=IMAGE_NT_SIGNATURE||nt->FileHeader.Machine!=IMAGE_FILE_MACHINE_AMD64||nt->FileHeader.SizeOfOptionalHeader!=sizeof(IMAGE_OPTIONAL_HEADER64)||nt->OptionalHeader.Magic!=IMAGE_NT_OPTIONAL_HDR64_MAGIC||nt->FileHeader.NumberOfSections==0||nt->FileHeader.NumberOfSections>96||nt->OptionalHeader.NumberOfRvaAndSizes<IMAGE_NUMBEROF_DIRECTORY_ENTRIES)return false;
  size=nt->OptionalHeader.SizeOfImage;if(size<4096||size>128*1024*1024||nt->OptionalHeader.SizeOfHeaders>size)return false;sections=IMAGE_FIRST_SECTION(nt);if(!Readable(sections,nt->FileHeader.NumberOfSections*sizeof(*sections)))return false;
  for(unsigned i=0;i<nt->FileHeader.NumberOfSections;++i){const auto& s=sections[i];if(s.VirtualAddress>=size||s.Misc.VirtualSize>size-s.VirtualAddress)return false;}return true;
 }
 const void* At(DWORD rva,size_t n)const{return rva<size&&n<=size-rva&&Readable(base+rva,n)?base+rva:nullptr;}
 template<class T>const T* At(DWORD rva,size_t n=1)const{if(n>SIZE_MAX/sizeof(T))return nullptr;return static_cast<const T*>(At(rva,n*sizeof(T)));}
 const char* String(DWORD rva)const{for(unsigned i=0;i<512;++i){auto p=At<char>(rva+i);if(!p)return nullptr;if(!*p)return reinterpret_cast<const char*>(base+rva);}return nullptr;}
 bool Text(DWORD rva)const{constexpr BYTE name[8]{'.','t','e','x','t',0,0,0};for(unsigned i=0;i<nt->FileHeader.NumberOfSections;++i){const auto& s=sections[i];if(memcmp(s.Name,name,8)==0&&(s.Characteristics&IMAGE_SCN_MEM_EXECUTE)&&rva>=s.VirtualAddress&&rva-s.VirtualAddress<s.Misc.VirtualSize)return true;}return false;}
};
constexpr const char* RequiredExports[]={"BOverlayNeedsPresent","SteamOverlayIsUsingGamepad","SteamOverlayIsUsingKeyboard","SteamOverlayIsUsingMouse"};
constexpr const char* RequiredImports[]={"GetForegroundWindow","GetActiveWindow"};
struct Contract {std::array<DWORD,4> exports{};std::array<DWORD,2> iat{};DWORD importDescriptor{},originalThunk{},firstThunk{};};
bool ReadContract(const Image& image,Contract& out){
 auto ex=image.nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_EXPORT];auto e=image.At<IMAGE_EXPORT_DIRECTORY>(ex.VirtualAddress);if(!e||ex.Size<sizeof(*e)||ex.Size>image.size-ex.VirtualAddress||!e->NumberOfFunctions||e->NumberOfFunctions>65536||!e->NumberOfNames||e->NumberOfNames>65536)return false;
 auto functions=image.At<DWORD>(e->AddressOfFunctions,e->NumberOfFunctions),names=image.At<DWORD>(e->AddressOfNames,e->NumberOfNames);auto ordinals=image.At<WORD>(e->AddressOfNameOrdinals,e->NumberOfNames);if(!functions||!names||!ordinals)return false;
 for(unsigned i=0;i<e->NumberOfNames;++i){auto name=image.String(names[i]);if(!name||ordinals[i]>=e->NumberOfFunctions)return false;for(unsigned n=0;n<out.exports.size();++n)if(strcmp(name,RequiredExports[n])==0){auto rva=functions[ordinals[i]];if(out.exports[n]||!image.Text(rva)||(rva>=ex.VirtualAddress&&rva-ex.VirtualAddress<ex.Size))return false;out.exports[n]=rva;}}
 for(auto rva:out.exports)if(!rva)return false;
 auto im=image.nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT];if(!im.VirtualAddress||im.Size<sizeof(IMAGE_IMPORT_DESCRIPTOR)||im.Size>image.size-im.VirtualAddress)return false;bool terminated=false,user32=false;
 for(unsigned i=0;i<im.Size/sizeof(IMAGE_IMPORT_DESCRIPTOR)&&i<512;++i){DWORD rva=im.VirtualAddress+i*sizeof(IMAGE_IMPORT_DESCRIPTOR);auto d=image.At<IMAGE_IMPORT_DESCRIPTOR>(rva);if(!d)return false;if(!d->Name&&!d->FirstThunk&&!d->OriginalFirstThunk){terminated=true;break;}auto name=image.String(d->Name);if(!name)return false;if(_stricmp(name,"USER32.dll"))continue;if(user32||!d->OriginalFirstThunk||!d->FirstThunk)return false;user32=true;out.importDescriptor=rva;out.originalThunk=d->OriginalFirstThunk;out.firstThunk=d->FirstThunk;bool end=false;
  for(unsigned index=0;index<65536;++index){if(index>(UINT32_MAX-d->OriginalFirstThunk)/8||index>(UINT32_MAX-d->FirstThunk)/8)return false;auto original=image.At<ULONGLONG>(d->OriginalFirstThunk+index*8);auto slot=image.At<ULONGLONG>(d->FirstThunk+index*8);if(!original||!slot)return false;if(!*original){end=true;break;}if(*original&IMAGE_ORDINAL_FLAG64)continue;if(*original>UINT32_MAX-2)return false;auto imported=image.String(DWORD(*original)+2);if(!imported)return false;for(unsigned n=0;n<out.iat.size();++n)if(strcmp(imported,RequiredImports[n])==0){if(out.iat[n])return false;out.iat[n]=d->FirstThunk+index*8;}}
  if(!end)return false;
 }
 return terminated&&user32&&out.iat[0]&&out.iat[1];
}
bool SameContract(const Image& disk,const Contract& expected,const Image& loaded,HMODULE module){
 if(memcmp(&disk.nt->FileHeader,&loaded.nt->FileHeader,sizeof(IMAGE_FILE_HEADER))||memcmp(&disk.nt->OptionalHeader,&loaded.nt->OptionalHeader,sizeof(IMAGE_OPTIONAL_HEADER64))||memcmp(disk.sections,loaded.sections,disk.nt->FileHeader.NumberOfSections*sizeof(IMAGE_SECTION_HEADER)))return false;
 Contract actual{};if(!ReadContract(loaded,actual)||memcmp(&expected,&actual,sizeof(actual)))return false;
 auto a=disk.At<IMAGE_IMPORT_DESCRIPTOR>(expected.importDescriptor),b=loaded.At<IMAGE_IMPORT_DESCRIPTOR>(expected.importDescriptor);if(!a||!b||memcmp(a,b,sizeof(*a)))return false;
 for(unsigned i=0;i<65536;++i){auto x=disk.At<ULONGLONG>(expected.originalThunk+i*8),y=loaded.At<ULONGLONG>(expected.originalThunk+i*8);if(!x||!y||*x!=*y)return false;if(!*x)break;if(i==65535)return false;}
 for(unsigned i=0;i<expected.exports.size();++i)if(reinterpret_cast<const BYTE*>(GetProcAddress(module,RequiredExports[i]))!=loaded.base+expected.exports[i])return false;
 HMODULE user32=GetModuleHandleW(L"USER32.dll");if(!user32)return false;
 for(unsigned i=0;i<expected.iat.size();++i){auto slot=loaded.At<ULONGLONG>(expected.iat[i]);auto api=GetProcAddress(user32,RequiredImports[i]);MEMORY_BASIC_INFORMATION memory{};if(!slot||!api||*slot!=reinterpret_cast<ULONGLONG>(api)||!VirtualQuery(reinterpret_cast<const void*>(api),&memory,sizeof(memory))||memory.AllocationBase!=user32||memory.Type!=MEM_IMAGE||memory.State!=MEM_COMMIT)return false;}
 return true;
}
}
struct RendererAttestation::State {
 std::wstring requested,root,renderer,steam;Handle directory,rendererFile,steamFile,mapping;const BYTE* mapped{};FILE_ID_INFO directoryId{},rendererId{},steamId{};std::string digest;Image disk{};Contract contract{};std::mutex recheckGate;
 ~State(){if(mapped)UnmapViewOfFile(mapped);}
 bool BindSteamRoot(){
  root=RegisteredRoot();if(root.empty())return false;renderer=root+L"\\gameoverlayrenderer64.dll";steam=root+L"\\steam.exe";if(!Equal(requested,renderer))return false;
  directory.value=CreateFileW(root.c_str(),GENERIC_READ,FILE_SHARE_READ|FILE_SHARE_WRITE,nullptr,OPEN_EXISTING,FILE_FLAG_BACKUP_SEMANTICS|FILE_FLAG_OPEN_REPARSE_POINT,nullptr);
  return directory.Valid()&&Normal(directory.value,true)&&Equal(Final(directory.value),root)&&Identity(directory.value,directoryId);
 }
 bool OpenRetainedFiles(){
  rendererFile.value=CreateFileW(renderer.c_str(),GENERIC_READ,FILE_SHARE_READ,nullptr,OPEN_EXISTING,FILE_FLAG_OPEN_REPARSE_POINT,nullptr);steamFile.value=CreateFileW(steam.c_str(),GENERIC_READ,FILE_SHARE_READ,nullptr,OPEN_EXISTING,FILE_FLAG_OPEN_REPARSE_POINT,nullptr);
  LARGE_INTEGER size{};return rendererFile.Valid()&&steamFile.Valid()&&Normal(rendererFile.value,false)&&Normal(steamFile.value,false)&&Equal(Final(rendererFile.value),renderer)&&Equal(Final(steamFile.value),steam)&&Identity(rendererFile.value,rendererId)&&Identity(steamFile.value,steamId)&&GetFileSizeEx(rendererFile.value,&size)&&size.QuadPart>=4096&&size.QuadPart<=128*1024*1024;
 }
 bool TrustValveFiles(){return SignedValve(steam,steamFile.value)&&OriginalName(steam,L"steam.exe")&&SignedValve(renderer,rendererFile.value)&&OriginalName(renderer,L"GameOverlayRenderer.dll");}
 bool ReadImageContract(){
  mapping.value=CreateFileMappingW(rendererFile.value,nullptr,PAGE_READONLY|SEC_IMAGE_NO_EXECUTE,0,0,nullptr);if(!mapping.Valid())return false;mapped=static_cast<const BYTE*>(MapViewOfFile(mapping.value,FILE_MAP_READ,0,0,0));return mapped&&disk.Read(mapped)&&ReadContract(disk,contract);
 }
 bool PinDigest(){digest=Sha(rendererFile.value);return digest.size()==64;}
 bool VerifyRetainedIdentity(){return Equal(RegisteredRoot(),root)&&PathStillNames(root,directory.value,directoryId,true)&&PathStillNames(steam,steamFile.value,steamId,false)&&PathStillNames(renderer,rendererFile.value,rendererId,false);}
 bool VerifyDigest(){return digest.size()==64&&Sha(rendererFile.value)==digest;}
 bool BindLoadedIdentity(HMODULE loaded){
  if(!loaded)return false;wchar_t path[32768]{};DWORD n=GetModuleFileNameW(loaded,path,32768);if(!n||n>=32768||!Equal(path,renderer))return false;Handle file(CreateFileW(path,GENERIC_READ,FILE_SHARE_READ,nullptr,OPEN_EXISTING,FILE_FLAG_OPEN_REPARSE_POINT,nullptr));FILE_ID_INFO id{};
  MEMORY_BASIC_INFORMATION memory{};return file.Valid()&&Normal(file.value,false)&&Equal(Final(file.value),renderer)&&Identity(file.value,id)&&SameId(id,rendererId)&&VirtualQuery(loaded,&memory,sizeof(memory))&&memory.AllocationBase==loaded&&memory.Type==MEM_IMAGE&&memory.State==MEM_COMMIT;
 }
 bool MatchLoadedContract(HMODULE loaded){Image memory{};return memory.Read(reinterpret_cast<const BYTE*>(loaded))&&SameContract(disk,contract,memory,loaded);}
};
RendererAttestation::RendererAttestation(std::unique_ptr<State> s):state(std::move(s)){}
RendererAttestation::~RendererAttestation()=default;
std::shared_ptr<const RendererAttestation> RendererAttestation::OpenFile(const wchar_t* path){if(!path||!*path)return {};auto s=std::make_unique<State>();s->requested=path;if(!admission::File(*s))return {};return std::shared_ptr<const RendererAttestation>(new RendererAttestation(std::move(s)));}
const char* RendererAttestation::Digest()const noexcept{return state->digest.c_str();}
bool RendererAttestation::VerifyLoaded(HMODULE module)const{
 std::lock_guard<std::mutex> lock(state->recheckGate);
 struct LoadedOps{State& s;HMODULE module;
  bool BindLoadedIdentity(){return s.BindLoadedIdentity(module);}bool MatchLoadedContract(){return s.MatchLoadedContract(module);}
  bool VerifyRetainedIdentity(){return s.VerifyRetainedIdentity();}bool VerifyDigest(){return s.VerifyDigest();}
 } ops{*state,module};return admission::Loaded(ops);
}
bool RendererAttestation::VerifyUnchanged()const{std::lock_guard<std::mutex> lock(state->recheckGate);return state->VerifyRetainedIdentity()&&state->VerifyDigest();}
}
