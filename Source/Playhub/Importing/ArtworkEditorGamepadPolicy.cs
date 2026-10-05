namespace Playhub.Importing;

[Flags]
internal enum ArtworkEditorPadButtons { None=0, Left=1, Right=2, Up=4, Down=8, ZoomOut=16, ZoomIn=32 }
internal readonly record struct ArtworkEditorPadAction(int X,int Y,int Zoom);

internal sealed class ArtworkEditorGamepadPolicy
{
    private bool _active;
    private readonly int[] _held=new int[3];
    private readonly long[] _repeat=new long[3];
    public ArtworkEditorPadAction Read(bool active,ArtworkEditorPadButtons buttons,long now)
    {
        int x=Axis(buttons,ArtworkEditorPadButtons.Left,ArtworkEditorPadButtons.Right);
        int y=Axis(buttons,ArtworkEditorPadButtons.Up,ArtworkEditorPadButtons.Down);
        int zoom=Axis(buttons,ArtworkEditorPadButtons.ZoomOut,ArtworkEditorPadButtons.ZoomIn);
        if(!active) { _active=false;Array.Clear(_held);Array.Clear(_repeat);return default; }
        if(!_active)
        {
            _active=true;_held[0]=x;_held[1]=y;_held[2]=zoom;
            Array.Fill(_repeat,long.MaxValue);return default;
        }
        return new(Step(x,0,now),Step(y,1,now),Step(zoom,2,now)*10);
    }
    private int Step(int value,int axis,long now)
    {
        if(value==0) { _held[axis]=0;_repeat[axis]=0;return 0; }
        if(value!=_held[axis]) { _held[axis]=value;_repeat[axis]=now+350;return value; }
        if(now<_repeat[axis])return 0;
        _repeat[axis]=now+100;return value;
    }
    private static int Axis(ArtworkEditorPadButtons buttons,ArtworkEditorPadButtons negative,ArtworkEditorPadButtons positive) =>
        ((buttons&positive)!=0?1:0)-((buttons&negative)!=0?1:0);
}
