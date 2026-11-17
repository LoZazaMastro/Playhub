using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Playhub.StoreLibrary;
using Playhub.Services;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Text.Json;
using Windows.Graphics.Imaging;
using Windows.Storage;

namespace Playhub;
public partial class CheckApp:Application
{
 [DllImport("kernel32.dll")]static extern uint GetCurrentThreadId();
 [DllImport("user32.dll")]static extern IntPtr GetThreadDesktop(uint id);
 [DllImport("user32.dll")]static extern IntPtr GetShellWindow();
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern bool GetUserObjectInformation(IntPtr h,int index,StringBuilder text,int count,out int needed);
 public CheckApp(){var s=new StringBuilder(256);GetUserObjectInformation(GetThreadDesktop(GetCurrentThreadId()),2,s,512,out _);if(!s.ToString().StartsWith("PlayhubStoreReview_",StringComparison.Ordinal)||GetShellWindow()!=IntPtr.Zero)Environment.Exit(73);RequestedTheme=ApplicationTheme.Dark;InitializeComponent();}
 protected override async void OnLaunched(LaunchActivatedEventArgs args)
 {
  string output=Environment.GetCommandLineArgs()[1];Directory.CreateDirectory(output);AppPaths.LocalDataRoot=Path.Combine(output,"private-data");Directory.CreateDirectory(AppPaths.LocalDataRoot);int checks=0;
  void Check(bool value,string name){checks++;if(!value)throw new Exception(name);}
  MainWindow? window=null;
  try
  {
   window=new();window.Activate();window.Page.SetActive(true);await window.Controller.ReadAsync();await Task.Delay(200);
   var buttons=MainWindow.Nodes(window.Page).OfType<Button>().Where(button=>AutomationProperties.GetAutomationId(button).StartsWith("StoreAccountAction.")).ToArray();
   Check(buttons.Length==4,"Four independently actionable account controls");
   Check(buttons.All(button=>button.IsEnabled),"Every account action starts enabled");
   Check(!MainWindow.Nodes(window.Page).Any(node=>node is SelectorBar or AutoSuggestBox or ComboBox),"Desktop does not install games");
   var epic=buttons.Single(button=>AutomationProperties.GetAutomationId(button)=="StoreAccountAction.Epic");
   var gog=buttons.Single(button=>AutomationProperties.GetAutomationId(button)=="StoreAccountAction.Gog");
   epic.Focus(FocusState.Keyboard);StoreLibraryService.Instance.AdvanceAccountName();await window.Controller.ReadAsync();await Task.Delay(70);
   Check(MainWindow.Nodes(window.Page).Contains(epic)&&epic.FocusState==FocusState.Keyboard,"Status refresh retains focused native button");
   Invoke(epic);await Until(()=>StoreLibraryService.Instance.Disconnects==1);await Task.Delay(50);
   Check(StoreLibraryService.Instance.Disconnects==1&&epic.Tag is StoreAccountIntent.SignIn,"Captured sign-out intent disconnected exactly once");
   var pending=new TaskCompletionSource<StoreConnectSession>(TaskCreationOptions.RunContinuationsAsynchronously);
   StoreLibraryService.Instance.PendingStore=StoreKind.Epic;StoreLibraryService.Instance.PendingConnect=pending;
   Invoke(epic);await Until(()=>epic.Content?.ToString()==LocalizationService.Translate("ja","StoreLibrary.Cancel"));
   Check(buttons.All(button=>button.IsEnabled),"Pending Epic auth does not disable other stores");
   Invoke(gog);await Until(()=>StoreLibraryService.Instance.Connects==2);await Task.Delay(80);
   Check(epic.Content?.ToString()==LocalizationService.Translate("ja","StoreLibrary.Cancel"),"GOG ran while Epic remained pending");
   Check(StoreLibraryService.Instance.Disconnects==1,"Cold sign-in never changes into another sign-out");
   Check(MainWindow.Nodes(window.Page).OfType<InfoBar>().Any(bar=>bar.IsOpen),"Unconfirmed login has a contextual warning");
   Invoke(epic);await Until(()=>epic.Content?.ToString()!=LocalizationService.Translate("ja","StoreLibrary.Cancel"));
   Check(buttons.All(button=>button.IsEnabled),"Cancel leaves every account immediately retryable");
   pending.TrySetResult(new(Guid.NewGuid(),StoreKind.Epic,new("https://epicgames.com/auth"),DateTimeOffset.UtcNow.AddMinutes(5),true));
   StoreLibraryService.Instance.PendingStore=null;await Task.Delay(50);
   Check(window.Controller.Snapshot.Accounts.Single(account=>account.Store==StoreKind.Epic).State==StoreConnectionState.Disconnected,"Late canceled login cannot publish connection");
   StoreLibraryService.Instance.AllowConnect=true;Invoke(gog);
   await Until(()=>window.Controller.Snapshot.Accounts.Single(account=>account.Store==StoreKind.Gog).State==StoreConnectionState.Connected);
   Check(window.Controller.Snapshot.Accounts.Single(account=>account.Store==StoreKind.Gog).State==StoreConnectionState.Connected,"Confirmed login appears after authoritative read");
   foreach(string language in LocalizationService.Languages.Select(item=>item.Key))
   {
    window.Language=language;window.Page.Relocalize(language);window.Update();
    Check(!MainWindow.Nodes(window.Page).OfType<TextBlock>().Any(block=>block.Text.StartsWith("StoreLibrary.")),"No untranslated UI key "+language);
    Check(buttons.All(button=>button.IsEnabled),"All account actions remain enabled "+language);
    Check(StoreDesktopCopy.Get(language,"checkingAccount")!=StoreDesktopCopy.Get(language,"signingOut"),"Distinct operation states "+language);
   }
   window.Language="ja";window.Page.Relocalize("ja");
   foreach(int width in new[]{356,640,1280})
   {
    window.Body.Width=width;window.AppWindow.Resize(new Windows.Graphics.SizeInt32(width+64,1020));await Task.Delay(100);window.Update();
    var bounds=buttons.Select(button=>new{label=button.Content?.ToString(),x=button.TransformToVisual(window.Page).TransformPoint(new()).X,y=button.TransformToVisual(window.Page).TransformPoint(new()).Y,button.ActualWidth}).ToArray();
    Check(bounds.All(bound=>bound.x>=-1&&bound.x+bound.ActualWidth<=window.Page.ActualWidth+1),"Buttons stay inside responsive cards "+width);
    if(width==1280)Check(bounds.Max(bound=>bound.y)-bounds.Min(bound=>bound.y)<1,"Empty GOG account-name row keeps all four actions aligned");
    File.WriteAllText(Path.Combine(output,"bounds-"+width+".json"),JsonSerializer.Serialize(bounds));
    await Capture(window.Body,Path.Combine(output,"accounts-"+width+"-ja.png"));
   }
   Check(StoreLibraryService.Instance.GameMutations==0,"No game management command executed");
   File.WriteAllText(Path.Combine(output,"result.json"),JsonSerializer.Serialize(new{passed=true,checks,gameMutations=StoreLibraryService.Instance.GameMutations}));
   window.Page.Dispose();window.Close();window=null;Exit();
  }
  catch(Exception error){File.WriteAllText(Path.Combine(output,"error.txt"),error.ToString());window?.Page.Dispose();window?.Close();Exit();}
 }
 static async Task Until(Func<bool> ready){var until=DateTime.UtcNow.AddSeconds(3);while(!ready()&&DateTime.UtcNow<until)await Task.Delay(30);if(!ready())throw new TimeoutException("Fixture UI condition did not settle");}
 static void Invoke(Button button){var peer=new ButtonAutomationPeer(button);((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();}
 static async Task Capture(FrameworkElement element,string path)
 {
  element.UpdateLayout();var bitmap=new RenderTargetBitmap();await bitmap.RenderAsync(element);var pixels=await bitmap.GetPixelsAsync();
  var folder=await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(path)!);var image=await folder.CreateFileAsync(Path.GetFileName(path),CreationCollisionOption.ReplaceExisting);
  using var stream=await image.OpenAsync(FileAccessMode.ReadWrite);var encoder=await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId,stream);
  encoder.SetPixelData(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Premultiplied,(uint)bitmap.PixelWidth,(uint)bitmap.PixelHeight,96,96,pixels.ToArray());await encoder.FlushAsync();
 }
}
public sealed class MainWindow:Window
{
 internal readonly StoreLibraryController Controller;
 internal readonly StoreAccountsPage Page;
 public readonly StackPanel Body=new(){Width=1280,Background=new SolidColorBrush(Windows.UI.Color.FromArgb(255,32,32,32)),Padding=new Thickness(24),Spacing=24};
 public string Language="ja";
 public MainWindow(){Controller=new(StoreLibraryService.Instance);Page=new(Controller,null,key=>LocalizationService.Translate(Language,key),Language);Body.Children.Add(Page);Content=new ScrollViewer{Content=Body,RequestedTheme=ElementTheme.Dark};AppWindow.Resize(new Windows.Graphics.SizeInt32(1344,1020));Closed+=(_,_)=>Page.Dispose();}
 public void Update()=>Body.UpdateLayout();
 public static IEnumerable<DependencyObject> Nodes(DependencyObject root){for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);yield return child;foreach(var node in Nodes(child))yield return node;}}
}

