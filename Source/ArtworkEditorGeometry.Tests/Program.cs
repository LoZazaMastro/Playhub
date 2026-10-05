using Playhub.Importing;
using Playhub.Integrations;

int checks=0;
void Check(bool valid,string message) { if(!valid) throw new Exception(message); checks++; }
bool Near(double a,double b) => Math.Abs(a-b)<.0001;
var stationary=ArtworkEditorGeometry.Drag(30,70,0,0,1600,900);
var viewport=ArtworkEditorGeometry.Preview(1000,16d/9d);
Check(viewport.Height==260&&Near(viewport.Width/viewport.Height,16d/9d)&&Near(viewport.X,(1000-viewport.Width)/2),"Wide Info preview exceeds260DIP or crops its whole16:9 scene.");
var narrowViewport=ArtworkEditorGeometry.Preview(320,16d/9d);
Check(Near(narrowViewport.Height,180)&&narrowViewport.Width==320&&Near(narrowViewport.X,0),"Narrow preview grows beyond its available width.");
var heroViewport=ArtworkEditorGeometry.Preview(1000,3840d/1240d);
Check(heroViewport.Height==260&&Near(heroViewport.Width/heroViewport.Height,3840d/1240d)&&heroViewport.Width<1000,"Perfect Hero preview loses its whole target aspect at height cap.");
Check(ArtworkEditorGeometry.Preview(0,16d/9d)==new ArtworkEditorRect(0,0,0,0),"Unmeasured viewport allocates a large preview.");
Check(ArtworkEditorGeometry.Reset("logo",25,50)==(25,50,100),"Perfect reset discarded original left-quarter layout.");
Check(ArtworkEditorGeometry.Reset("logo")== (50,50,100)&&ArtworkEditorGeometry.Reset("background",25,50)==(50,50,100),"LC/background reset inherited unrelated Perfect defaults.");
Check(stationary==(30,70),"Pointer press moved the logo before a drag.");
var dragged=ArtworkEditorGeometry.Drag(30,70,160,-90,1600,900);
Check(dragged==(40,60),"Pointer delta did not normalize against current scene.");
Check(ArtworkEditorGeometry.Drag(30,70,80,-45,800,450)==dragged,"Responsive resize changed normalized drag.");
Check(ArtworkEditorGeometry.Drag(50,50,9000,-9000,1600,900)==(100,0),"Out-of-frame pointer movement was unbounded.");
Check(ArtworkEditorGeometry.Drag(25,40,99,99,0,0)==(25,40),"Unmeasured surface created invalid position.");
var logo=ArtworkEditorGeometry.Logo(1600,900,800,200,50,50,100);
Check(Near(logo.X+logo.Width/2,800)&&Near(logo.Y+logo.Height/2,450),"Logo preview used top-left percentage rather than center position.");
Check(Near(logo.Width,672)&&Near(logo.Height,168),"LC preview does not match its original 42 percent/max20 percent logo fit.");
var zoom=ArtworkEditorGeometry.Logo(1600,900,800,200,50,50,200);
Check(Near(zoom.Width,logo.Width*2)&&Near(zoom.X+zoom.Width/2,800),"Logo zoom jumped position.");
var edge=ArtworkEditorGeometry.Logo(1600,900,800,200,0,100,200);
Check(edge.X<0&&edge.Y+edge.Height>900,"Edge placement incorrectly recentered the logo instead of allowing clipped boundaries.");
var tall=ArtworkEditorGeometry.Logo(1600,900,200,800,50,50,100);
Check(Near(tall.Height,180)&&Near(tall.Width,45),"Tall logo preview stretched its proportions.");
Check(ArtworkEditorGeometry.Logo(1600,900,0,0,50,50,100).Width==0,"Unloaded image generated invalid geometry.");
var cover=ArtworkEditorGeometry.Background(1600,900,900,1600,0,100,100);
Check(cover.Width>=1600&&cover.Height>=900&&cover.X<=0&&cover.Y<=0&&cover.Y+cover.Height>=900,"Background crop exposed a blank edge.");
var right=ArtworkEditorGeometry.Background(1600,900,3200,900,100,50,100);
Check(Near(right.X,-1600)&&Near(right.Y,0),"Background position does not select the far crop edge.");
var back=ArtworkEditorGeometry.Background(1600,900,3200,900,50,50,100);
var backDrag=ArtworkEditorGeometry.DragBackground(50,50,160,90,1600,900,back);
Check(backDrag==(40,50),"Background drag went opposite to the cursor or moved an axis without crop room.");
var shifted=ArtworkEditorGeometry.Background(1600,900,3200,900,backDrag.X,backDrag.Y,100);
Check(Near(shifted.X-back.X,160),"Background pointer delta changed after crop normalization.");
var normalized=new PerfectArtworkLayout(-2,double.NaN,300,200,-3,50,120,-10,double.PositiveInfinity).Normalize();
Check(normalized.LogoX==0&&normalized.LogoY==50&&normalized.LogoScale==200&&normalized.BackgroundX==100&&normalized.BackgroundY==0&&normalized.BackgroundScale==100&&normalized.BackgroundOpacity==100&&normalized.ShadowOpacity==0&&normalized.ShadowBlur==40,"Invalid imported layout escaped bounds.");
ArtworkPixels Solid(int width,int height,byte r)
{
    var bytes=new byte[width*height*4]; for(int i=0;i<bytes.Length;i+=4) { bytes[i+2]=r;bytes[i+3]=255; } return new(width,height,bytes);
}
var source=Solid(40,20,40); var pixels=Solid(8,8,240);
var padded=new byte[100*100*4];for(int y=20;y<80;y++)for(int x=44;x<56;x++)padded[(y*100+x)*4+3]=255;padded[3]=8;
var bounds=PerfectArtworkPixels.LogoBounds(new(100,100,padded));
Check(bounds==new ArtworkPixelBounds(42,18,16,64),"Shared preview/raster alpha trim lost the antialias border or included placeholder alpha.");
var trimmedPreview=ArtworkEditorGeometry.Logo(3840,1240,bounds.Width,bounds.Height,25,50,100,.28,.72);
Check(Near(trimmedPreview.X+trimmedPreview.Width/2,960)&&Near(trimmedPreview.Height,1240*.72),"Trimmed Perfect preview geometry differs from actual raster bounds.");
bool emptyLogo=false;try { PerfectArtworkPixels.LogoBounds(new(100,100,new byte[40000])); }catch(InvalidDataException) { emptyLogo=true; }
Check(emptyLogo,"Blank preview logo was accepted as a valid trimmed source.");
var old=PerfectArtworkPixels.Render(source,pixels,"banner");
var explicitDefault=PerfectArtworkPixels.Render(source,pixels,"banner",layout:new());
Check(old.Bgra.SequenceEqual(explicitDefault.Bgra),"Optional layout changed the existing default raster.");
var moved=PerfectArtworkPixels.Render(source,pixels,"banner",layout:new(LogoX:75,LogoY:50,ShadowOpacity:0));
int Sample(ArtworkPixels image,double x,double y) => image.Bgra[((int)(image.Height*y)*image.Width+(int)(image.Width*x))*4+2];
Check(Sample(moved,.75,.5)>200&&Sample(moved,.25,.5)<80,"Actual raster ignored edited logo center or retained old baked placement.");
var enlarged=PerfectArtworkPixels.Render(source,pixels,"banner",layout:new(LogoX:75,LogoScale:200,ShadowOpacity:0));
Check(Sample(enlarged,.95,.5)>200&&Sample(moved,.95,.5)<80,"Actual raster ignored logo zoom.");
var dim=PerfectArtworkPixels.Render(source,pixels,"banner",layout:new(BackgroundOpacity:0,ShadowOpacity:0));
Check(Sample(dim,.9,.9)==0,"Actual background opacity did not composite against black.");
using var cancelled=new CancellationTokenSource(); cancelled.Cancel(); bool stopped=false;
try { PerfectArtworkPixels.Render(source,pixels,"banner",cancelled.Token,new()); } catch(OperationCanceledException) { stopped=true; }
Check(stopped,"Edited raster ignored cancellation.");
Console.WriteLine($"PASS {checks} geometry/normalized pointer/actual raster layout checks; no UI or installed writes.");
