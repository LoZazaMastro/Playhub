// Playhub Xbox Session. Distributed under GPL-3.0-only.
internal static class SteamInputInventoryCompatibilityChecks {
static string Slot(int slot,string vendor="28de",string product="1302",string handle="AA",string type="steam")=>$"[slot {slot}]\nVID=0x{vendor}\nPID=0x{product}\nhandle=0x{handle}\ntype={type}\n";
internal static void Run(Action<bool,string> check){
 void Need(bool value,string name)=>check(value,name);
 var one=Slot(0);var duplicate=one+Slot(1);var many=one+Slot(4,"045e","028e","BB","xbox360")+Slot(5,"045e","028e","CC","xboxone");
 Need(SteamInputEvidence.Parse(one).Source is {Valid:true,Slot:0},"single physical source retained");
 Need(SteamInputEvidence.Parse(duplicate).Source is {Valid:true,Slot:0},"same exact SC handle aliases deduplicated");
 Need(SteamInputEvidence.Parse(Slot(1)+Slot(0)).Source?.Slot==0,"alias choice deterministic across row order");
 Need(SteamInputEvidence.Parse(many).Source is {Valid:true},"ordinary Xbox slots beyond3 do not poison SC");
 Need(SteamInputEvidence.Parse(many+Slot(1)).Source is {Valid:true},"realistic aliases plus extended ordinary slots");
 Need(SteamInputEvidence.Parse(Slot(5)+one).Source is {Valid:true,Slot:0},"unsupported physical alias cannot hide supported exact lane");
 Need(SteamInputEvidence.Parse(Slot(5)).Source is {Valid:false},"parser does not invent support for unattested raw lane");
 Need(SteamInputEvidence.Parse(one+Slot(1,handle:"AB")).Source is null,"distinct SC handles remain ambiguous");
 Need(SteamInputEvidence.Parse(one+Slot(1,product:"1304")).Source is null,"same handle conflicting Steam PID rejected");
 Need(SteamInputEvidence.Parse(one+Slot(1,type:"xbox360")).Source is null,"same handle conflicting controller family rejected");
 Need(SteamInputEvidence.Parse(one+Slot(1,vendor:"045e",product:"028e",type:"xbox360")).Source is null,"same handle conflicting VID rejected");
 Need(SteamInputEvidence.Parse(Slot(0,handle:"0")+Slot(1,handle:"0")).Source is null,"zero handle never deduplicated as controller");
 Need(SteamInputEvidence.Parse(one+Slot(0)).Source is null,"duplicate inventory slot rejected");
 Need(SteamInputEvidence.Parse(one+"[slot -1]\nVID=0x045e\nPID=0x028e\n").Source is null,"negative slot malformed not ignored");
 Need(SteamInputEvidence.Parse(one+Slot(4,"045e","7eb0","BB","xbox360")).Source is {Valid:true},"distinct owned Xbox output metadata can coexist");
 Need(!SteamInputEvidence.Parse(one+Slot(5,"045e","7eb0","CC","xboxone")).Shadow,"distinct stale XboxOne output metadata never vetoes physical input");
 Need(SteamInputEvidence.Parse(one+Slot(4,"045e","7eb0","AA","xbox360")).Shadow,"output handle collision remains veto");
 var observed=SteamInputEvidence.Parse("[slot 0]\nname=Playhub Controller\nVID=0x28de\nPID=0x1302\nhandle=0x0002fa99618037b2\ntype=steam\n[slot 1]\nname=Playhub Controller\nVID=0x28de\nPID=0x1302\nhandle=0x0002fa99618037b2\ntype=steam\n[slot 2]\nname=Xbox 360 Controller\nVID=0x045e\nPID=0x028e\nhandle=0x00545e28ea04d5e7\ntype=xbox360\n[slot 3]\nname=Xbox 360 Controller\nVID=0x045e\nPID=0x028e\nhandle=0x00645e28ea04d5e7\ntype=xbox360\n[slot 4]\nname=0x045e/0x7eb0\nVID=0x045e\nPID=0x7eb0\nhandle=0x0745e7eb0a04d5e7\ntype=xbox360\n[slot 5]\nname=XInput Controller #3\nVID=0x045e\nPID=0x7eb0\nhandle=0x0845e7eb0a04d5e7\ntype=xboxone\n");
 Need(observed.Source is {Valid:true,Slot:0},"actual observed six-slot inventory source parsed");Need(!observed.Shadow,"actual observed stale outputs no longer veto exact source");
 var lease=new SteamControllerSourceLease(SteamInputEvidence.Parse(one).Source!.Value);
 Need(lease.Current(SteamInputEvidence.Parse(duplicate)),"matching alias preserves same source lease");
 Need(!lease.Current(SteamInputEvidence.Parse(one+Slot(1,handle:"AB")))&&!lease.Revoked,"ambiguous inventory pauses without inventing a confirmed replacement");
 Need(lease.Current(SteamInputEvidence.Parse(one)),"the same source can recover after an unconfirmed inventory sample");
 Need(!lease.Current(SteamInputEvidence.Parse(Slot(0,handle:"AB")))&&lease.Revoked,"a positively observed replacement revokes the original lease");
 Need(!lease.Current(SteamInputEvidence.Parse(one)),"a revoked lease never revives from later inventory");
 Need(SteamInputEvidence.Parse(Slot(5,"045e","7eb0","CC","xboxone")).Source is null,"stale XboxOne output alone never becomes physical source");
 Need(SteamInputEvidence.Parse(one+Slot(4,"045e","7eb0","CC","steam")).Shadow,"output masquerading as Steam source remains veto");
 Need(SteamInputEvidence.Parse(one+Slot(4,"045e","7eb0","CC","xboxone").Replace("handle=0xCC\n","")).Shadow,"output without exact handle remains veto");
 Need(SteamInputEvidence.Parse(one+Slot(4,"045e","7eb0","CC","xboxone").Replace("type=xboxone\n","")).Shadow,"incomplete output family remains veto");
 Need(SteamInputEvidence.Parse(new string('x',SteamInputEvidence.MaxBytes+1)).Source is null,"inventory byte bound remains fail closed");
}}
