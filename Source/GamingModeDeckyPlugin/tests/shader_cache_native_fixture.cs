

public sealed class FakeDrs : IShaderDrs {
 public ShaderState Current=new ShaderState {Enabled=true,Limit=16384,DefaultLimit=16384,EnableDefault=true,LimitDefault=true};
 public int Writes; public bool FailBefore,FailAfter,ExternalAfter,RejectWrite,ExternalRollback; public uint Default=16384;
 public ShaderState Read(){return Current;}
 public bool Write(ShaderState expected,ShaderState wanted){
  if(RejectWrite)return false;if(!Current.Same(expected))return false;Writes++;
  if(FailBefore)throw new Exception();Current=wanted;
  if(ExternalRollback && Writes==2)Current=new ShaderState{Enabled=true,Limit=1024,DefaultLimit=Default,EnableDefault=false,LimitDefault=false};
  if(ExternalAfter){Current=new ShaderState{Enabled=true,Limit=10240,DefaultLimit=Default,EnableDefault=false,LimitDefault=false};throw new Exception();}
  if(FailAfter && Writes==1)throw new Exception();return true;
 }
}
public static class Fixture {
 static int count;
 static void Check(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS "+name);count++;}
 public static void Run(){
  var f=new FakeDrs();var old=f.Read();var r=ShaderTransaction.Run(f,null,true,0,true);Check(r.status=="ready"&&f.Writes==0,"probe never writes");
  r=ShaderTransaction.Run(f,old,true,0,true);Check(r.status=="applied"&&f.Writes==0,"unchanged settings do not save");
  r=ShaderTransaction.Run(f,old,false,4096,false);Check(r.status=="applied"&&!r.state.Enabled&&r.state.Limit==4096&&f.Writes==1,"actual desired values read back");
  r=ShaderTransaction.Run(f,old,true,1024,false);Check(r.status=="changed"&&f.Writes==1,"stale expected state rejects write");
  f=new FakeDrs();f.RejectWrite=true;r=ShaderTransaction.Run(f,f.Read(),false,4096,false);Check(r.status=="changed"&&f.Writes==0,"change immediately before save vetoes write");
  f=new FakeDrs();f.FailBefore=true;r=ShaderTransaction.Run(f,f.Read(),false,4096,false);Check(r.status=="failed"&&r.state.Enabled&&f.Writes==1,"permission failure does not claim a changed setting");
  f=new FakeDrs();f.FailAfter=true;r=ShaderTransaction.Run(f,f.Read(),false,4096,false);Check(r.status=="restored"&&r.state.Enabled&&r.state.Limit==16384&&f.Writes==2,"save failure conditionally restores original");
  f=new FakeDrs();f.ExternalAfter=true;r=ShaderTransaction.Run(f,f.Read(),false,4096,false);Check(r.status=="changed"&&r.state.Limit==10240&&f.Writes==1,"new external setting survives rollback");
  f=new FakeDrs();f.FailAfter=true;f.ExternalRollback=true;r=ShaderTransaction.Run(f,f.Read(),false,4096,false);Check(r.status=="changed"&&r.state.Limit==1024,"post-rollback change never falsely reports restored");
  f=new FakeDrs();f.Current=new ShaderState{Enabled=true,Limit=4096,DefaultLimit=32768,EnableDefault=false,LimitDefault=false};r=ShaderTransaction.Run(f,f.Read(),true,0,true);Check(r.status=="applied"&&r.state.Limit==32768&&r.state.LimitDefault,"driver default is read from actual driver not hardcoded");
  Console.WriteLine("Native fixtures: "+count);
 }
}
