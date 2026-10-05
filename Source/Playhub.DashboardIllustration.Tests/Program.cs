using Playhub;

int passed = 0;
void Check(bool value, string description) { if (!value) throw new Exception(description); passed++; Console.WriteLine("PASS " + description); }
var frames = DashboardIllustrationTimeline.FocusFrames.ToArray();
Check(frames.Length > 2 && frames[0].Progress == 0 && frames[^1].Progress == 1, "timeline spans exactly one complete cycle");
Check(frames.Zip(frames.Skip(1)).All(pair => pair.First.Progress < pair.Second.Progress), "keyframe times increase without instantaneous cuts");
Check(frames[0].X == frames[^1].X, "last position meets first position at the loop boundary");
Check(frames[0].X == frames[1].X && frames[^2].X == frames[^1].X, "both sides of the loop seam are stationary");
Check(frames.All(frame => 35 + frame.X >= 11 && 35 + frame.X + 66 <= 269), "focus frame remains inside the fixed dashboard outline");
foreach (float center in new[] { 0f, 78f, 156f })
    Check(frames.Zip(frames.Skip(1)).Any(pair => pair.First.X == center && pair.Second.X == center && (pair.Second.Progress - pair.First.Progress) * DashboardIllustrationTimeline.DurationSeconds >= .9), "each dashboard function has a readable dwell at " + center);
Check(DashboardIllustrationTimeline.DurationSeconds >= 6, "loop avoids rapid distracting movement");
Console.WriteLine($"Dashboard illustration checks: {passed} passed.");
