int passed = 0;
TrackedCommandParsingChecks.Run((value, name) =>
{
    if (!value) throw new Exception(name);
    passed++;
});
Console.WriteLine($"UWP Steam log checks: {passed} passed.");
