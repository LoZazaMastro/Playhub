using System.Buffers.Binary;
using System.Text;
internal static class InputFencePrototypeChecks
{
 internal static void Run(Action<bool,string> assert)
 {
  var id=new FenceProducer.Identity(100,10,200,20,1,900,new string('a',64),0x8123456702000000);
  var renderer=new ExperimentalInputFence.RendererImage(@"C:\Steam\GameOverlayRenderer64.dll",Enumerable.Range(0,32).Select(n=>(byte)n).ToArray(),0x200000,0x12345678);
  byte[] b=ExperimentalInputFence.Configuration(id,@"C:\Test\Game.exe",new IntPtr(0x1234567890),new IntPtr(0x2234567890),renderer);
  assert(b.Length==3728&&BinaryPrimitives.ReadUInt32LittleEndian(b)==3728&&BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(4))==2,"fixed native config length");
  assert(BinaryPrimitives.ReadUInt64LittleEndian(b.AsSpan(56))==id.GameId,"full game ID no JS rounding");
  assert(BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(64))==100&&BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(68))==200,"PID layout");
  assert(BinaryPrimitives.ReadUInt64LittleEndian(b.AsSpan(80))==900,"exact HWND");
  assert(BinaryPrimitives.ReadUInt64LittleEndian(b.AsSpan(88))==0x1234567890&&BinaryPrimitives.ReadUInt64LittleEndian(b.AsSpan(96))==0x2234567890,"handles keep64bits");
  assert(Encoding.Unicode.GetString(b,104,34)==@"C:\Test\Game.exe"+"\0","UTF16 image inline not remote pointer");
  assert(b.AsSpan(8,32).SequenceEqual(Convert.FromHexString(id.AuthoritySession)),"authority nonce copy exact");
  bool longRefused=false;try{ExperimentalInputFence.Configuration(id,new string('x',1023),0,0,renderer);}catch(ArgumentOutOfRangeException){longRefused=true;}
  assert(longRefused,"long image refused rather than truncation");
  byte[] dll=File.ReadAllBytes(ExperimentalInputFence.DllPath);uint rva=ExperimentalInputFence.Export(dll,"FenceAttach");
  assert(rva>0&&rva<0x100000,"fixed DLL attach export via PE RVA");
  assert(ExperimentalInputFence.Export(dll,"LoadLibraryW")==0,"no arbitrary export");
  assert(checked(new IntPtr(0x1200000000)+(nint)rva).ToInt64()==0x1200000000+(long)rva,"remote address above32bits");
  var now=DateTimeOffset.Parse("2026-11-13T01:00:00Z");string permit=ExperimentalInputFence.DllSha+"\n"+id.GameId+"\n"+now.ToString("O")+"\n";
  assert(ExperimentalInputFence.Permit(permit,id.GameId,now.UtcDateTime.ToFileTimeUtc(),now),"explicit fresh Root opt-in");
  assert(!ExperimentalInputFence.Permit(permit,id.GameId+1,now.UtcDateTime.ToFileTimeUtc(),now),"permit cannot select another canonical game");
  assert(!ExperimentalInputFence.Permit(permit,id.GameId,now.AddSeconds(-3).UtcDateTime.ToFileTimeUtc(),now),"existing pre-permit game denied");
  assert(!ExperimentalInputFence.Permit(permit,id.GameId,now.UtcDateTime.ToFileTimeUtc(),now.AddSeconds(121)),"permit timeout");
  assert(!ExperimentalInputFence.Permit(permit,id.GameId,now.UtcDateTime.ToFileTimeUtc(),now.AddSeconds(-1)),"future permit denied");
  assert(!ExperimentalInputFence.Permit(permit.Replace(ExperimentalInputFence.DllSha,new string('0',64)),id.GameId,now.UtcDateTime.ToFileTimeUtc(),now),"library pin mismatch denied");

  assert(Encoding.Unicode.GetString(b,2152,renderer.Path.Length*2)==renderer.Path,"trusted renderer inline path");
  assert(b.AsSpan(3688,32).SequenceEqual(renderer.Sha256),"trusted renderer SHA exact");
  assert(BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(3720))==renderer.ImageSize&&BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(3724))==renderer.Timestamp,"loaded renderer PE identity");
  assert(ExperimentalInputFence.Export(dll,"FenceReadback")>0,"fixed diagnostic data export only");
  byte[] status=new byte[128];void U32(int o,uint v)=>BinaryPrimitives.WriteUInt32LittleEndian(status.AsSpan(o),v);void U64(int o,ulong v)=>BinaryPrimitives.WriteUInt64LittleEndian(status.AsSpan(o),v);
  U32(0,0x52464850);U32(4,2);U32(8,128);U32(12,2);Convert.FromHexString(id.AuthoritySession).CopyTo(status,16);U64(48,10);U64(56,20);U32(64,100);U32(68,200);U32(72,7);U32(76,10);U32(88,5);U32(92,1);U32(96,3);U32(100,2);U64(104,1000);U32(112,17);U32(120,1);
  var observed=ExperimentalInputFence.ParseDiagnostic(status,2,id,1100);
  assert(observed is not null&&observed.StateReads==10&&observed.ValveReads==5&&observed.GuideSeen==1&&observed.GameImageReads==2,"typed diagnostic observations no authority");
  assert(ExperimentalInputFence.ParseDiagnostic(status,4,id,1100)is null,"concurrent sequence change refused");
  assert(ExperimentalInputFence.ParseDiagnostic(status,2,id,2501)is null,"stale observation refused");
  assert(ExperimentalInputFence.ParseDiagnostic(status,2,id,999)is null,"future observation refused");
  var good=(byte[])status.Clone();
  foreach(int offset in new[]{0,4,8,12,16,48,56,64,68,72,112,116,120,124}){status=(byte[])good.Clone();status[offset]^=1;assert(ExperimentalInputFence.ParseDiagnostic(status,2,id,1100)is null,"drifted private diagnostic header/identity rejected "+offset);}
  status=(byte[])good.Clone();U32(72,11);U32(120,2);
  assert(ExperimentalInputFence.ParseDiagnostic(status,2,id,1100) is {Ready:true,RendererMode:2},"exact absent renderer is a typed ready observation, not authority");
  U32(72,27);U32(112,18);U32(116,0x80070005);
  assert(ExperimentalInputFence.ParseDiagnostic(status,2,id,1100) is {Ready:false,AttachStage:18,RendererMode:2},"late renderer revocation visible without new grant");
  U32(72,0);U32(112,10);U32(120,0);
  assert(ExperimentalInputFence.ParseDiagnostic(status,2,id,1100) is {Ready:false,StageName:"renderer-file-hash"},"early completed refusal diagnostic has exact stage, never ready");
  U32(116,0);assert(ExperimentalInputFence.ParseDiagnostic(status,2,id,1100)is null,"failed stage cannot masquerade as successful zero result");
  U32(72,7);U32(112,17);U32(120,2);assert(ExperimentalInputFence.ParseDiagnostic(status,2,id,1100)is null,"present flags cannot claim absent identity");
  status=good;
  bool badRenderer=false;try{ExperimentalInputFence.Configuration(id,@"C:\Game.exe",0,0,renderer with{Path=@"C:\Other.dll"});}catch(ArgumentException){badRenderer=true;}
  assert(badRenderer,"cannot delegate arbitrary renderer module name");

  int reads=0,currentCalls=0;long tick=1100;bool live=true,failRead=false,mismatch=false;
  bool Transport(nint at,byte[] buffer){reads++;if(failRead)return false;assert((at==new IntPtr(0x1200000000L)&&buffer.Length==128)||(at==new IntPtr(0x120000000cL)&&buffer.Length==4),"fixed diagnostic address and sequence offset");if(buffer.Length==128)good.CopyTo(buffer,0);else BinaryPrimitives.WriteUInt32LittleEndian(buffer,mismatch?4u:2u);return true;}
  bool Current(){currentCalls++;return live;}
  var whole=ExperimentalInputFence.ReadDiagnostic(new IntPtr(0x1200000000L),id,Current,Transport,()=>tick);
  assert(whole is not null&&reads==2,"whole reader exactly snapshot+sequence");
  reads=0;failRead=true;assert(ExperimentalInputFence.ReadDiagnostic(new IntPtr(0x1200000000L),id,Current,Transport,()=>tick)is null&&reads==1,"read failure is unknown, not ACK");failRead=false;
  reads=0;mismatch=true;assert(ExperimentalInputFence.ReadDiagnostic(new IntPtr(0x1200000000L),id,Current,Transport,()=>tick)is null&&reads==6,"seqlock contention bounded to3attempts");mismatch=false;
  reads=0;live=false;assert(ExperimentalInputFence.ReadDiagnostic(new IntPtr(0x1200000000L),id,Current,Transport,()=>tick)is null&&reads==0,"lost owner prevents memory read");live=true;
  reads=0;currentCalls=0;tick=1100;
  bool SlowCurrent(){if(++currentCalls==3)tick=2501;return true;}
  assert(ExperimentalInputFence.ReadDiagnostic(new IntPtr(0x1200000000L),id,SlowCurrent,Transport,()=>tick)is null&&reads==2,"last current getter cannot return stale observation");
 }
}
