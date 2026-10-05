namespace Playhub.Importing;

public readonly record struct ArtworkEditorRect(double X,double Y,double Width,double Height);

public static class ArtworkEditorGeometry
{
    public static ArtworkEditorRect Preview(double availableWidth,double aspectRatio,double maximumHeight=260)
    {
        double width=double.IsFinite(availableWidth) ? Math.Max(0,availableWidth) : 0;
        double aspect=Clamp(aspectRatio,16d/9d,1,4),limit=Clamp(maximumHeight,260,100,600);
        double height=Math.Min(width/aspect,limit),visibleWidth=height*aspect;
        return new((width-visibleWidth)/2,0,visibleWidth,height);
    }
    public static (double X,double Y,double Scale) Reset(string target,double logoX=50,double logoY=50) => target=="logo"
        ? (Clamp(logoX,50,0,100),Clamp(logoY,50,0,100),100) : (50,50,100);
    public static (double X,double Y) Drag(double initialX,double initialY,double deltaX,double deltaY,double width,double height)
    {
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0) return (Clamp(initialX,50,0,100),Clamp(initialY,50,0,100));
        return (Clamp(initialX+deltaX/width*100,50,0,100),Clamp(initialY+deltaY/height*100,50,0,100));
    }
    public static ArtworkEditorRect Logo(double width,double height,double sourceWidth,double sourceHeight,double x,double y,double zoom,
        double widthFraction=.42,double heightFraction=.20)
    {
        if (!Valid(width,height,sourceWidth,sourceHeight)) return new(0,0,0,0);
        double scale=Math.Min(width*widthFraction/sourceWidth,height*heightFraction/sourceHeight)*Clamp(zoom,100,50,200)/100;
        double w=sourceWidth*scale,h=sourceHeight*scale;
        return new(width*Clamp(x,50,0,100)/100-w/2,height*Clamp(y,50,0,100)/100-h/2,w,h);
    }
    public static ArtworkEditorRect Background(double width,double height,double sourceWidth,double sourceHeight,double x,double y,double zoom)
    {
        if (!Valid(width,height,sourceWidth,sourceHeight)) return new(0,0,0,0);
        double scale=Math.Max(width/sourceWidth,height/sourceHeight)*Clamp(zoom,100,100,200)/100;
        double w=sourceWidth*scale,h=sourceHeight*scale;
        return new((width-w)*Clamp(x,50,0,100)/100,(height-h)*Clamp(y,50,0,100)/100,w,h);
    }
    public static (double X,double Y) DragBackground(double initialX,double initialY,double deltaX,double deltaY,double width,double height,ArtworkEditorRect placement)
    {
        double dx=width-placement.Width,dy=height-placement.Height;
        return (Math.Abs(dx)<.001 ? Clamp(initialX,50,0,100) : Clamp(initialX+deltaX/dx*100,50,0,100),
            Math.Abs(dy)<.001 ? Clamp(initialY,50,0,100) : Clamp(initialY+deltaY/dy*100,50,0,100));
    }
    public static double Clamp(double value,double fallback,double minimum,double maximum) => double.IsFinite(value) ? Math.Clamp(value,minimum,maximum) : fallback;
    private static bool Valid(params double[] sizes) => sizes.All(value=>double.IsFinite(value)&&value>0);
}
