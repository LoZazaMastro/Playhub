using Playhub.Importing;
int checks=0;
void Check(bool result,string label) { if(!result)throw new Exception(label);checks++; }
var p=new ArtworkEditorGamepadPolicy();
Check(p.Read(false,ArtworkEditorPadButtons.Right,0)==default,"Hidden input changed position.");
Check(p.Read(true,ArtworkEditorPadButtons.Right,100)==default,"Focus acquisition consumed an already-held button.");
Check(p.Read(true,ArtworkEditorPadButtons.Right,900)==default,"Already-held button repeated after focus acquisition.");
Check(p.Read(true,ArtworkEditorPadButtons.None,1000)==default,"Release changed position.");
Check(p.Read(true,ArtworkEditorPadButtons.Right,1100)==new ArtworkEditorPadAction(1,0,0),"New Right edge was missed.");
Check(p.Read(true,ArtworkEditorPadButtons.Right,1200)==default,"Held button repeated before delay.");
Check(p.Read(true,ArtworkEditorPadButtons.Right,1449)==default,"Hold delay was shortened.");
Check(p.Read(true,ArtworkEditorPadButtons.Right,1450)==new ArtworkEditorPadAction(1,0,0),"Held button did not repeat after delay.");
Check(p.Read(true,ArtworkEditorPadButtons.Right,1549)==default,"Hold repeated faster than100ms.");
Check(p.Read(true,ArtworkEditorPadButtons.Right,1550)==new ArtworkEditorPadAction(1,0,0),"Hold repeat stopped.");
Check(p.Read(true,ArtworkEditorPadButtons.Right,9000)==new ArtworkEditorPadAction(1,0,0),"Delayed timer caught up with multiple edits.");
Check(p.Read(true,ArtworkEditorPadButtons.None,9100)==default,"Neutral did not release repeat.");
Check(p.Read(true,ArtworkEditorPadButtons.Right|ArtworkEditorPadButtons.Up,9200)==new ArtworkEditorPadAction(1,-1,0),"Diagonal movement lost an axis.");
Check(p.Read(true,ArtworkEditorPadButtons.Left|ArtworkEditorPadButtons.Right|ArtworkEditorPadButtons.Up|ArtworkEditorPadButtons.Down,9300)==default,"Opposite buttons moved position.");
Check(p.Read(true,ArtworkEditorPadButtons.Left|ArtworkEditorPadButtons.ZoomIn,9400)==new ArtworkEditorPadAction(-1,0,10),"Move and zoom did not share one snapshot.");
Check(p.Read(true,ArtworkEditorPadButtons.ZoomIn|ArtworkEditorPadButtons.ZoomOut,9500)==default,"Opposite zoom buttons changed scale.");
Check(p.Read(true,ArtworkEditorPadButtons.ZoomOut,9600)==new ArtworkEditorPadAction(0,0,-10),"Left shoulder did not reduce scale.");
Check(p.Read(false,ArtworkEditorPadButtons.ZoomOut,9700)==default,"Deactivated window changed scale.");
Check(p.Read(true,ArtworkEditorPadButtons.ZoomOut,9800)==default,"Activation replayed the held shoulder.");
Check(p.Read(true,ArtworkEditorPadButtons.ZoomOut,10500)==default,"Held shoulder repeated after activation.");
Check(p.Read(false,ArtworkEditorPadButtons.None,10600)==default,"Disposal/hidden state retained an action.");
Check(p.Read(true,ArtworkEditorPadButtons.None,10700)==default,"Neutral activation changed the layout.");
foreach(var (button,expected) in new[]{(ArtworkEditorPadButtons.Left,new ArtworkEditorPadAction(-1,0,0)),(ArtworkEditorPadButtons.Right,new ArtworkEditorPadAction(1,0,0)),(ArtworkEditorPadButtons.Up,new ArtworkEditorPadAction(0,-1,0)),(ArtworkEditorPadButtons.Down,new ArtworkEditorPadAction(0,1,0)),(ArtworkEditorPadButtons.ZoomIn,new ArtworkEditorPadAction(0,0,10)),(ArtworkEditorPadButtons.ZoomOut,new ArtworkEditorPadAction(0,0,-10))})
{
    p.Read(true,ArtworkEditorPadButtons.None,11000);
    Check(p.Read(true,button,11100)==expected,"A mapped press was not delivered exactly once.");
    Check(p.Read(true,button,11200)==default,"A short press repeated.");
    Check(p.Read(true,ArtworkEditorPadButtons.None,11300)==default,"A release edited the layout.");
}
Console.WriteLine($"PASS {checks} production-linked edge, hold, cancellation and focused-session policy checks; WinUI/WGI actual input gate remains root-owned.");
