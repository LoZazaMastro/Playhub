using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Playhub.StoreLibrary;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Text.Json;
using Windows.Graphics.Imaging;
using Windows.Storage;

namespace Playhub;
public partial class CheckApp : Application
{
 [DllImport("user32.dll")]static extern uint GetCurrentThreadId();
 [DllImport("kernel32.dll",EntryPoint="GetCurrentThreadId")]static extern uint ThreadId();
 [DllImport("user32.dll")]static extern IntPtr GetThreadDesktop(uint id);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern bool GetUserObjectInformation(IntPtr h,int index,StringBuilder s,int count,out int needed);
 [DllImport("user32.dll")]static extern IntPtr GetShellWindow();
 private Window? window;
 private static string Desktop(){var s=new StringBuilder(256);return GetUserObjectInformation(GetThreadDesktop(ThreadId()),2,s,512,out _)?s.ToString():"unknown";}
 public CheckApp()
 {
  if(!Desktop().StartsWith("PlayhubStoreReview_",StringComparison.Ordinal)||GetShellWindow()!=IntPtr.Zero)Environment.Exit(73);
  RequestedTheme = ApplicationTheme.Dark; InitializeComponent();
 }
 protected override async void OnLaunched(LaunchActivatedEventArgs args)
 {
  string output=Environment.GetCommandLineArgs()[1];Directory.CreateDirectory(output);var result=new List<object>();
  try
  {
   var service=new Fixture();var controller=new StoreLibraryController(service);
   var words=JsonSerializer.Deserialize<Dictionary<string,Dictionary<string,string>>>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"review-locales.json")))!;
   string lang="en";string T(string key)=>words[lang].GetValueOrDefault(key,key);
   string? coverPath=null;
   var page=new StoreLibraryPage(controller,T,()=>Task.FromResult<string?>(@"C:\Games\Review"),()=>1.5,lang,_=>coverPath,(_,_)=>Task.FromResult(false),installedActionsService:service);
   var body=new StackPanel{Spacing=20,Padding=new Thickness(24),Background=new SolidColorBrush(Windows.UI.Color.FromArgb(255,32,32,32))};body.Children.Add(new TextBlock{Text="Digital stores · isolated UI fixture",FontSize=28});body.Children.Add(page);
   var scroll=new ScrollViewer{Content=body,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
   var root=new Grid{Background=Application.Current.Resources["ApplicationPageBackgroundThemeBrush"]as Brush};root.Children.Add(scroll);
   window=new Window{Content=root,Title="Playhub private review"};window.Activate();page.SetActive(true);await controller.ReadAsync();
   bool focusOnly=Environment.GetEnvironmentVariable("PLAYHUB_STORE_REVIEW_FOCUS_ONLY")=="1";
   foreach(int width in focusOnly?new[]{1280}:new[]{640,1280,356})
   {
    window.AppWindow.Resize(new Windows.Graphics.SizeInt32(width+16,980));body.Width=width;await Task.Delay(500);root.UpdateLayout();
    foreach(string language in focusOnly?new[]{"en"}:new[]{"en","de"})
    {
     lang=language;page.Relocalize(lang);page.SetActive(true);await Task.Delay(350);root.UpdateLayout();
     var path=Path.Combine(output,$"stores-{width}-{lang}.png");await Capture(body,path);
     var overflow=Descendants(body).OfType<FrameworkElement>().Where(e=>e is Button or ComboBox or AutoSuggestBox).Where(e=>
     {try{var point=e.TransformToVisual(body).TransformPoint(new Windows.Foundation.Point());return point.X< -1||point.X+e.ActualWidth>body.ActualWidth+1;}catch{return false;}}).Select(e=>new{type=e.GetType().Name,e.ActualWidth}).ToArray();
     result.Add(new{width,language,png=path,horizontalOverflow=overflow,desktop=Desktop(),physicalDesktopShown=false});
    }
   }
   lang="en";page.Relocalize(lang);body.Width=1280;window.AppWindow.Resize(new Windows.Graphics.SizeInt32(1296,980));
   var tabs=(SelectorBar)page.FindName("LibraryTabs");tabs.SelectedItem=(SelectorBarItem)page.FindName("DownloadsTab");await Task.Delay(250);
   await Capture(body,Path.Combine(output,"downloads-1280-en.png"));
   page.SetActive(false); await controller.ReadAsync();
   Button AccountButton()=>Descendants((FrameworkElement)page.FindName("Accounts")).OfType<Button>().First(b=>b.IsEnabled&&b.Content is TextBlock label&&label.Text==T("StoreLibrary.RefreshLibrary"));
   Border JobCard()=>((Panel)page.FindName("DownloadRows")).Children.OfType<Border>().First(b=>Descendants(b).OfType<TextBlock>().Any(t=>t.Text==service.CancelJob.Title));
   Button CancelButton()=>Descendants(JobCard()).OfType<Button>().First(b=>b.Content is TextBlock label&&label.Text==T("StoreLibrary.Cancel"));
   tabs.SelectedItem=(SelectorBarItem)page.FindName("AccountsTab");await Task.Delay(150);root.UpdateLayout();
   var accountBefore=AccountButton();bool accountFocused=accountBefore.Focus(FocusState.Keyboard);
   service.AdvanceProgress();await controller.ReadAsync();await Task.Delay(80);
   bool accountRetained=ReferenceEquals(accountBefore,AccountButton())&&accountBefore.FocusState==FocusState.Keyboard;
   tabs.SelectedItem=(SelectorBarItem)page.FindName("DownloadsTab");await Task.Delay(150);root.UpdateLayout();
   var rowBefore=CancelButton();bool rowFocused=rowBefore.Focus(FocusState.Keyboard);double progressBefore=Descendants(JobCard()).OfType<ProgressBar>().Single().Value;
   service.AdvanceProgress();await controller.ReadAsync();await Task.Delay(80);
   var rowAfter=CancelButton();double progressAfter=Descendants(JobCard()).OfType<ProgressBar>().Single().Value;
   bool rowRetained=ReferenceEquals(rowBefore,rowAfter)&&rowAfter.FocusState==FocusState.Keyboard;
   page.SetActive(true);int readsBefore=service.Reads;await Task.Delay(2300);
   bool timerPolled=service.Reads>readsBefore, automaticRetained=ReferenceEquals(rowAfter,CancelButton())&&rowAfter.FocusState==FocusState.Keyboard;
   page.SetActive(false);
   await Capture(body,Path.Combine(output,"download-cancel-row-poll-focus-en.png"));
   service.ReplaceJobOwner();await controller.ReadAsync();await Task.Delay(80);
   bool ownerReplaced=!ReferenceEquals(rowAfter,CancelButton());
   result.Add(new{kind="poll-focus",accountFocused,accountRetained,rowFocused,rowRetained,timerPolled,automaticRetained,progressBefore,progressAfter,progressAdvanced=progressAfter>progressBefore,ownerReplaced});
   tabs.SelectedItem=(SelectorBarItem)page.FindName("GamesTab");
   page.SetActive(true);
   body.Width=640;window.AppWindow.Resize(new Windows.Graphics.SizeInt32(656,980));await Task.Delay(250);
   var cards=(Panel)page.FindName("GameCards");var repeaterBefore=cards.Children.Single();
   var installBefore=Descendants(cards).OfType<Button>().First(b=>b.Content is TextBlock label&&label.Text==T("StoreLibrary.Install"));
   bool installFocused=installBefore.Focus(FocusState.Keyboard);await Task.Delay(100);double offsetBefore=scroll.VerticalOffset;
   coverPath=Path.Combine(AppContext.BaseDirectory,"Assets","ServiceLogos","Epic.png");page.RefreshArtwork();await Task.Delay(150);
   result.Add(new{kind="artwork-refresh",installFocused,repeaterRetained=ReferenceEquals(repeaterBefore,cards.Children.Single()),
    focusRetained=installBefore.FocusState==FocusState.Keyboard,imagesUpdated=Descendants(cards).OfType<Image>().Any(),
    scrollRetained=Math.Abs(scroll.VerticalOffset-offsetBefore)<1});
   var removeBefore=Descendants(cards).OfType<Button>().Single(b=>AutomationProperties.GetAutomationId(b)=="StoreGame-Epic-installed-fixture-Uninstall");
   bool blocked=!removeBefore.IsEnabled;int statusReadsBefore=service.StatusReads;
   service.SetWorkerBlocked(false);await controller.ReadAsync();await Task.Delay(150);
   var removeAfter=Descendants(cards).OfType<Button>().Single(b=>AutomationProperties.GetAutomationId(b)=="StoreGame-Epic-installed-fixture-Uninstall");
   result.Add(new{kind="installed-eligibility",blocked,unblocked=removeAfter.IsEnabled,retained=ReferenceEquals(removeBefore,removeAfter),
    statusReread=service.StatusReads>statusReadsBefore});
   service.SetWorkerBlocked(true);await controller.ReadAsync();await Task.Delay(50);
   var pending=page.RequestInstallAsync(service.Game.Key);await Task.Delay(500);
   var popup=VisualTreeHelper.GetOpenPopupsForXamlRoot(page.XamlRoot).Select(p=>p.Child).OfType<FrameworkElement>().FirstOrDefault();
   if(popup is null)throw new Exception("Actual install chooser did not open");
   await Capture(popup,Path.Combine(output,"install-chooser-640-en.png"));
   foreach(var d in Descendants(popup).OfType<ContentDialog>().ToArray())d.Hide();
   if(popup is ContentDialog dialog)dialog.Hide();await Task.Delay(100);
   await pending;
   result.Add(new{actualInstallChooser=true,service.Enqueues,scope="fixture service, no account, provider, browser or download calls"});
   var cancelTask=(Task)typeof(StoreLibraryPage).GetMethod("CancelDownloadAsync",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.Invoke(page,[service.CancelJob])!;
   await Task.Delay(400);
   var cancelDialog=VisualTreeHelper.GetOpenPopupsForXamlRoot(page.XamlRoot).SelectMany(p=>Descendants(p.Child)).OfType<ContentDialog>().First(d=>Equals(d.Title,T("StoreLibrary.ConfirmCancelDownload")));
   var yes=Descendants(cancelDialog).OfType<Button>().First(b=>b.Name=="PrimaryButton");bool focus=yes.Focus(FocusState.Keyboard);await Task.Delay(100);
   await Capture(cancelDialog,Path.Combine(output,"cancel-confirm-focused-en.png"));cancelDialog.Hide();await cancelTask;
   result.Add(new{actualCancelDialog=true,focus,service.Controls,confirmedNoCancels=service.Controls==0});
   File.WriteAllText(Path.Combine(output,"review.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
   page.Dispose();window.Close();Exit();
  }
  catch(Exception e){File.WriteAllText(Path.Combine(output,"error.txt"),e.ToString());window?.Close();Exit();}
 }
 static IEnumerable<DependencyObject> Descendants(DependencyObject d){yield return d;for(int i=0;i<VisualTreeHelper.GetChildrenCount(d);i++)foreach(var x in Descendants(VisualTreeHelper.GetChild(d,i)))yield return x;}
 static async Task Capture(FrameworkElement element,string path)
 {
  var bitmap=new RenderTargetBitmap();await bitmap.RenderAsync(element,(int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight));var pixels=await bitmap.GetPixelsAsync();
  var folder=await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(path)!);var file=await folder.CreateFileAsync(Path.GetFileName(path),CreationCollisionOption.FailIfExists);
  using var stream=await file.OpenAsync(FileAccessMode.ReadWrite);var enc=await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId,stream);
  enc.SetPixelData(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Premultiplied,(uint)bitmap.PixelWidth,(uint)bitmap.PixelHeight,96,96,pixels.ToArray());await enc.FlushAsync();
 }
 sealed class Fixture:IStoreLibraryService,IStoreInstalledGameService
 {
  public int Enqueues,Controls,Reads,StatusReads;
  private long revision=1;
  public StoreDownloadJob CancelJob=>jobs[0];
  public void AdvanceProgress(){revision++;var time=DateTimeOffset.UtcNow;jobs[0]=jobs[0]with{Revision=jobs[0].Revision+1,DownloadedBytes=jobs[0].DownloadedBytes+1024L*1024*1024,UpdatedAt=time};jobs[1]=jobs[1]with{UpdatedAt=time};}
  public void ReplaceJobOwner(){revision++;jobs[0]=jobs[0]with{Key=new(StoreKind.Gog,"replacement-owner"),AccountRevision=2,Revision=jobs[0].Revision+1};}
  public void SetWorkerBlocked(bool value){revision++;for(int index=0;index<jobs.Length;index++)jobs[index]=jobs[index]with{State=value?StoreJobState.Downloading:StoreJobState.Completed,Revision=jobs[index].Revision+1};}
  static readonly StoreCapabilities direct=new(StoreInstallKind.Direct,true,true,true,true,true);
  public StoreLibraryGame Game=new(new(StoreKind.Epic,"review-game"),"Review fixture · ready to install",1,direct,false,false);
  readonly StoreDownloadJob[] jobs=[new(Guid.NewGuid(),new(StoreKind.Epic,"download-fixture"),"Review fixture · known size",StoreJobState.Downloading,1,1,@"C:\Games\Fixture",4L*1024*1024*1024,20L*1024*1024*1024,50*1024*1024,DateTimeOffset.UtcNow,true,false,true),new(Guid.NewGuid(),new(StoreKind.Gog,"unknown-fixture"),"Review fixture · unknown size",StoreJobState.Downloading,1,1,@"D:\Games\Fixture",0,null,null,DateTimeOffset.UtcNow,false,false,true)];
  StoreLibrarySnapshot Snapshot=>new(revision,DateTimeOffset.UtcNow,[new(StoreKind.Epic,StoreConnectionState.Connected,1,"Fixture account",direct),new(StoreKind.Gog,StoreConnectionState.Disconnected,1,null,direct),new(StoreKind.Xbox,StoreConnectionState.Unavailable,1,null,new(StoreInstallKind.Unavailable,false,false,false,false,false))],[Game,Game with{Key=new(StoreKind.Epic,"installed-fixture"),Title="Review fixture · installed",IsInstalled=true,IsLaunchable=true,InstallPath=@"C:\Games\Installed-fixture"}],jobs.ToArray());
  public Task<StoreLibrarySnapshot> GetSnapshotAsync(CancellationToken ct=default){Reads++;return Task.FromResult(Snapshot);}
  public Task<StoreInstallPreparation> PrepareInstallAsync(StoreGameKey k,long rev,string? path=null,CancellationToken ct=default)=>Task.FromResult(new StoreInstallPreparation(k,rev,1,path is null?null:Guid.NewGuid(),DateTimeOffset.UtcNow.AddMinutes(5),20L*1024*1024*1024,@"C:\Games\Review",path,[new(@"C:\","Review drive",100L*1024*1024*1024,true)],StoreInstallKind.Direct));
  public Task<StoreCommandResult> EnqueueAsync(StoreEnqueueRequest r,CancellationToken ct=default){Enqueues++;throw new Exception("Review must never enqueue");}
  public Task<StoreConnectSession> BeginConnectAsync(StoreKind s,CancellationToken ct=default)=>throw new Exception("Review must never authenticate");
  public Task<StoreCommandResult> CompleteConnectAsync(Guid id,string code,CancellationToken ct=default)=>throw new Exception("Review must never authenticate");
  public Task<StoreCommandResult> DisconnectAsync(StoreKind s,long rev,CancellationToken ct=default)=>throw new Exception("Review must never sign out");
  public Task<StoreCommandResult> RefreshAsync(StoreKind s,long rev,CancellationToken ct=default)=>Task.FromResult(new StoreCommandResult(true,null,Snapshot));
  public Task<StoreCommandResult> ControlAsync(StoreJobCommand r,CancellationToken ct=default){Controls++;throw new Exception("Review must never control downloads");}
  public Task<StoreLaunchDescriptor> GetVerifiedLaunchDescriptorAsync(StoreGameKey k,CancellationToken ct=default)=>throw new Exception("Review must never launch");
  public Task<StoreInstalledGameStatus> GetInstalledGameAsync(StoreGameKey key,CancellationToken token=default){StatusReads++;return Task.FromResult(new StoreInstalledGameStatus(key,1,revision,true,true,!jobs.Any(j=>j.State==StoreJobState.Downloading),@"C:\Games\Installed-fixture"));}
  public Task<StoreUninstallPreparation> PrepareUninstallAsync(StoreGameKey key,long account,long library,CancellationToken token=default)=>throw new Exception("Review must never uninstall");
  public Task<StoreCommandResult> UninstallAsync(StoreUninstallPreparation preparation,Guid request,CancellationToken token=default)=>throw new Exception("Review must never uninstall");
 }
}
