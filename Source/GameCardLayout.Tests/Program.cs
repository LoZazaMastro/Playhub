using Playhub.Importing;
int passed=0;
void Check(bool value,string text){if(!value)throw new Exception(text);passed++;Console.WriteLine("PASS "+text);}
foreach(var width in new[]{0d,-1,double.NaN,double.NegativeInfinity,double.PositiveInfinity})Check(GameCardLayout.ColumnCount(width)==1,"invalid or initial viewport uses one safe column");
foreach(var (width,count) in new[]{(239d,1),(240d,1),(477.999,1),(478d,2),(723.999,2),(724d,3),(969.999,3),(970d,4),(1280d,5),(double.MaxValue,12)})Check(GameCardLayout.ColumnCount(width)==count,$"width {width} chooses {count} columns");
Check(GameCardLayout.ColumnCount(650)==2,"narrow window retains legible card actions");
int changes=0;
GameCardLayout.DensityChanged += (_,_)=>changes++;
Check(GameCardLayout.MaximumSizeStep==2,"density exposes exactly the original three levels");
foreach (var (step, width, title, body) in new[] {(0,184d,14d,12d),(1,208d,15d,12.5),(2,232d,16d,13d)})
{
    Check(GameCardLayout.PreferredWidth(step)==width && GameCardLayout.TitleFontSize(step)==title && GameCardLayout.BodyFontSize(step)==body,
        $"density {step} preserves its original card width and typography");
}
for(int step=0;step<3;step++)
{
    GameCardLayout.SetSizeStep(step);
    int previous=1;
    for(double width=GameCardLayout.PreferredWidth(step);width<=2400;width+=1)
    {
        int columns=GameCardLayout.ColumnCount(width);
        double card=(width-(columns-1)*GameCardLayout.ColumnSpacing)/columns;
        if(card<GameCardLayout.PreferredWidth(step)-.001 || columns>GameCardLayout.MaximumColumns(step) || columns<previous)
            throw new Exception("Invalid density reflow at step "+step);
        previous=columns;
    }
    Check(true,$"density {step} stays bounded and responsive at every width");
    Check(GameCardLayout.ColumnCount(double.MaxValue)==GameCardLayout.MaximumColumns(step),$"density {step} respects maximum columns");
}
GameCardLayout.SetSizeStep(99);
Check(GameCardLayout.SizeStep==2,"high density input clamps to last retained step");
int unchanged=changes;GameCardLayout.SetSizeStep(2);
Check(changes==unchanged,"same density does not trigger redundant reflow");
foreach (int legacyStep in new[] {3,4})
{
    GameCardLayout.SetSizeStep(0);
    int beforeLegacy=changes;
    GameCardLayout.SetSizeStep(legacyStep);
    Check(GameCardLayout.SizeStep==2 && changes==beforeLegacy+1,
        $"legacy preference {legacyStep} becomes level 2 and reflows once");
    Check(GameCardLayout.PreferredWidth(legacyStep)==232 && GameCardLayout.TitleFontSize(legacyStep)==16 && GameCardLayout.BodyFontSize(legacyStep)==13,
        $"legacy preference {legacyStep} cannot produce removed giant card dimensions");
    int normalizedChanges=changes;
    GameCardLayout.SetSizeStep(legacyStep);
    Check(changes==normalizedChanges,$"repeated legacy preference {legacyStep} does not repeat reflow");
}
GameCardLayout.SetSizeStep(-1);
Check(GameCardLayout.SizeStep==0,"negative density input clamps to first step");
GameCardLayout.SetSizeStep(2);
foreach (double viewport in new[] {650d, 960, 1240, 1536})
{
    var widths = new List<double>(); var fonts = new List<double>();
    for (int step = 0; step < 3; step++)
    {
        GameCardLayout.SetSizeStep(step);
        widths.Add(GameCardLayout.CardWidth(viewport)); fonts.Add(GameCardLayout.TitleFontSize(step));
        Check(GameCardLayout.ContentWidth(viewport) <= viewport, $"step {step} fits viewport {viewport}");
    }
    Check(widths.Distinct().Count() == 3, $"all three steps change actual card widths at {viewport}");
    Check(fonts.Distinct().Count() == 3, $"all three steps change title typography at {viewport}");
}
GameCardLayout.SetSizeStep(2);
Check(GameCardLayout.CardWidth(120) == 120 && GameCardLayout.ContentWidth(120) == 120,"one card shrinks safely in an exceptionally narrow viewport");
GameCardLayout.SetSizeStep(2);
Console.WriteLine($"{passed} production-linked card layout checks PASS; actual WinUI visibility/controller remains root gate.");
