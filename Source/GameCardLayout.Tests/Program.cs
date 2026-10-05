using Playhub.Importing;
int passed=0;
void Check(bool value,string text){if(!value)throw new Exception(text);passed++;Console.WriteLine("PASS "+text);}
foreach(var width in new[]{0d,-1,double.NaN,double.NegativeInfinity,double.PositiveInfinity})Check(GameCardLayout.ColumnCount(width)==1,"invalid or initial viewport uses one safe column");
foreach(var (width,count) in new[]{(239d,1),(240d,1),(493.999,1),(494d,2),(747.999,2),(748d,3),(1001.999,3),(1002d,4),(1280d,4),(double.MaxValue,4)})Check(GameCardLayout.ColumnCount(width)==count,$"width {width} chooses {count} columns");
Check(GameCardLayout.ColumnCount(650)==2,"old four-card narrow threshold now retains room for all three actions");
for(double width=240;width<2400;width+=.25){int columns=GameCardLayout.ColumnCount(width);double card=(width-(columns-1)*GameCardLayout.ColumnSpacing)/columns;if(card<GameCardLayout.MinimumWidth-.00001)throw new Exception("Card narrower than minimum");if(columns<4&& (width-columns*GameCardLayout.ColumnSpacing)/(columns+1)>=GameCardLayout.MinimumWidth)throw new Exception("Unnecessarily sparse grid");}
Check(true,"all quarter-DIP widths fit minimum and choose maximal count up to four");
Console.WriteLine($"{passed} production-linked card layout checks PASS; actual WinUI visibility/controller remains root gate.");
