using Playhub.Importing;

int passed = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception(name);
    passed++;
    Console.WriteLine("PASS " + name);
}
var xbox = new[] { new ToolbarItemSize(104, 40), new ToolbarItemSize(158, 40), new ToolbarItemSize(172, 40), new ToolbarItemSize(164, 40) };
var normal = ImportToolbarLayout.Arrange(xbox, 1000);
Check(normal.Items.All(item => item.Y == 0) && normal.Height == 40, "normal viewport retains one natural-width row");
Check(normal.Items.Select(item => item.Width).SequenceEqual(xbox.Select(size => size.Width)), "normal row never spreads button widths");
var narrow = ImportToolbarLayout.Arrange(xbox, 448);
Check(narrow.Height == 90 && narrow.Items[3].Y == 50, "restart remains on a second reachable row at narrow width");
Check(narrow.Items[0].X == 0 && narrow.Items[1].X == 114 && narrow.Items[2].X == 0, "wrap preserves scan import relink restart sequence");
var exact = ImportToolbarLayout.Arrange(xbox, 628);
Check(exact.Height == 40, "exact natural width including three gaps fits one row");
Check(ImportToolbarLayout.Arrange(xbox, 627.75).Height == 90, "quarter-DIP below exact fit wraps final action");
var local = new[] { new ToolbarItemSize(170, 40), new ToolbarItemSize(145, 40), new ToolbarItemSize(104, 40), new ToolbarItemSize(158, 40), new ToolbarItemSize(164, 40) };
Check(ImportToolbarLayout.Arrange(local, 448).Height == 90, "five local-import controls remain in two rows");
Check(ImportToolbarLayout.Arrange(Array.Empty<ToolbarItemSize>(), 300).Height == 0, "empty toolbar adds no phantom row");
Check(ImportToolbarLayout.Arrange(xbox, double.PositiveInfinity).Height == 40, "unconstrained initial measurement retains one row");
var uneven = ImportToolbarLayout.Arrange(new[] { new ToolbarItemSize(100, 40), new ToolbarItemSize(100, 60), new ToolbarItemSize(100, 30) }, 210);
Check(uneven.Height == 100 && uneven.Items[0].Y == 10 && uneven.Items[2].Y == 70, "mixed control heights center within each row without overlap");
var oversized = ImportToolbarLayout.Arrange(new[] { new ToolbarItemSize(500, 80), new ToolbarItemSize(100, 40) }, 200);
Check(oversized.Items[0].Width == 200 && oversized.Items[1].Y == 90, "overwide control remains contained and does not hide following action");
var repeated = ImportToolbarLayout.Arrange(xbox, 448);
Check(repeated.Items.SequenceEqual(narrow.Items), "same parent width is deterministic across repeated layout");
for (double width = 240; width <= 1800; width += .25)
{
    foreach (var sizes in new[] { xbox, local, xbox.Select(size => size with { Width = size.Width * 1.6 }).ToArray() })
    {
        var result = ImportToolbarLayout.Arrange(sizes, width);
        for (int i = 0; i < result.Items.Length; i++)
        {
            var item = result.Items[i];
            if (item.X < 0 || item.Y < 0 || item.X + item.Width > width + .00001 || item.Y + item.Height > result.Height + .00001)
                throw new Exception("Action outside reported toolbar bounds");
            for (int j = 0; j < i; j++)
            {
                var other = result.Items[j];
                if (item.X < other.X + other.Width && other.X < item.X + item.Width && item.Y < other.Y + other.Height && other.Y < item.Y + item.Height)
                    throw new Exception("Overlapping toolbar actions");
            }
        }
    }
}
Check(true, "all quarter-DIP widths and long translated labels retain every action inside nonoverlapping bounds");
Console.WriteLine($"{passed} production-linked toolbar layout checks PASS; actual WinUI/controller remains root gate.");
