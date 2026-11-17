internal static class SteamXboxProfileChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var exact=new SteamXboxProfileWindow.Identity(30,200,20,1,"Microsoft.GamingServices_8wekyb3d8bbwe",
            @"C:\Program Files\WindowsApps\Microsoft.GamingServices_1_x64__8wekyb3d8bbwe\GamingServicesTcui\GamingServicesTcui.exe",
            @"C:\Program Files\WindowsApps\Microsoft.GamingServices_1_x64__8wekyb3d8bbwe");
        check(SteamXboxProfileWindow.Accepts(exact,20,100,1),"Microsoft profile child shares the exact live game input session");
        check(!SteamXboxProfileWindow.Accepts(exact with{Parent=21},20,100,1),"profile belonging to another game cannot borrow this controller");
        check(!SteamXboxProfileWindow.Accepts(exact with{Birth=99},20,100,1),"profile predating the retained game is rejected");
        check(!SteamXboxProfileWindow.Accepts(exact with{Session=2},20,100,1),"profile in another Windows session is rejected");
        check(!SteamXboxProfileWindow.Accepts(exact with{Family="Other_8wekyb3d8bbwe"},20,100,1),"Microsoft publisher alone cannot authorize another package");
        check(!SteamXboxProfileWindow.Accepts(exact with{Image=@"C:\Users\test\GamingServicesTcui.exe"},20,100,1),"copied executable cannot borrow registered Gaming Services identity");
        check(!SteamXboxProfileWindow.Accepts(exact with{Image=exact.Image.Replace("GamingServicesTcui.exe","other.exe")},20,100,1),"another Gaming Services component is not a profile chooser");
        check(!SteamXboxProfileWindow.Accepts(exact with{Pid=20},20,100,1),"game process is distinct from its Microsoft profile child");
        check(!SteamXboxProfileWindow.Accepts(exact with{Pid=0},20,100,1),"missing profile process is rejected");
        check(SteamControllerLayoutTrial.ReaderWindowPause(false,true,false)==RelayExit.FocusLost,"Xbox chooser is same game scope but pauses the old reader window without detaching its pad");
        check(SteamControllerLayoutTrial.ReaderWindowPause(false,true,true) is null,"current Xbox chooser reader keeps forwarding");
        check(SteamControllerLayoutTrial.ReaderWindowPause(true,true,false)==RelayExit.OverlayOpened,"real Steam menu retains priority during a profile transition");
        var command=SteamControllerLayoutTrial.LayoutCommand(@"C:\Program Files (x86)\Steam\steam.exe",1234);
        check(command.ArgumentList.SequenceEqual(new[]{"-silent","steam://forceinputappid/1234"}),"controller layout requests use silent Steam dispatch instead of raising its window");
        check(!command.UseShellExecute&&command.CreateNoWindow,"layout dispatch neither opens a console nor resolves through the browser");
        check(SteamControllerLayoutTrial.LayoutCommand("steam.exe",0).ArgumentList.Last()=="steam://forceinputappid/0","automatic layout restore keeps its real zero context");
    }
}
