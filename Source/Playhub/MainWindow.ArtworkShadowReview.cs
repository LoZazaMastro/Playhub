#if PLAYHUB_UI_REVIEW
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Playhub.Importing;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using System.Text.Json.Nodes;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Playhub;
public sealed partial class MainWindow
{
    private async Task ReviewArtworkShadowAsync()
    {
        string output=Environment.GetEnvironmentVariable("PLAYHUB_REVIEW_OUTPUT") ?? throw new InvalidOperationException("Explicit private review output required.");
        Directory.CreateDirectory(output);
        var checks=new List<string>(); var failures=new List<string>();
        void Check(bool value,string message) { checks.Add((value?"PASS ":"FAIL ")+message);if(!value)failures.Add(message); }
        async Task WritePng(string path,bool logo)
        {
            const int width=320,height=160;
            var pixels=new byte[width*height*4];
            for(int y=0;y<height;y++)for(int x=0;x<width;x++)
            {
                int at=(y*width+x)*4;
                if(!logo) {pixels[at]=pixels[at+1]=pixels[at+2]=220;pixels[at+3]=255;}
                else if(x is >40 and <280 && y is >30 and <130) {pixels[at+2]=240;pixels[at+3]=255;}
            }
            using var stream=new InMemoryRandomAccessStream();
            var encoder=await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId,stream);
            encoder.SetPixelData(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Straight,width,height,96,96,pixels);
            await encoder.FlushAsync();stream.Seek(0);
            using var reader=new DataReader(stream);await reader.LoadAsync((uint)stream.Size);
            var bytes=new byte[stream.Size];reader.ReadBytes(bytes);await File.WriteAllBytesAsync(path,bytes);
        }
        try
        {
            string background=Path.Combine(output,"background.png"),logo=Path.Combine(output,"logo.png");
            await WritePng(background,false);await WritePng(logo,true);
            Check(!_navigation.MenuItems.OfType<NavigationViewItem>().Any(item=>(item.Tag as string)?.StartsWith("recomp",StringComparison.Ordinal)==true || (item.Tag as string)?.StartsWith("emulation",StringComparison.Ordinal)==true),"removed navigation has no Recomp or Emulation entry");
            var page=new StackPanel { Spacing=20,Padding=new Thickness(24) };
            _pageHost.Children.Clear();_pageHost.Children.Add(page);_welcomeRoot.Visibility=Visibility.Collapsed;
            foreach(var target in new[]{"hero","banner"})
            {
                var settings=new JsonObject{["logo_shadow_opacity"]=0,["logo_shadow_blur"]=80,["logo_position_x"]=25,["logo_position_y"]=50};
                using var editor=new LaunchCurtainVisualEditor(background,logo,settings,_=>{},T,target=="hero"?3840d/1240:1926d/900,.28,.72,true,maxPreviewHeight:420);
                page.Children.Clear();page.Children.Add(editor.Element);
                var deadline=DateTimeOffset.UtcNow.AddSeconds(12);
                async Task Wait(Func<bool> condition)
                {
                    while(!condition()) {if(DateTimeOffset.UtcNow>deadline)throw new TimeoutException("Native artwork preview did not become ready.");await Task.Delay(40);}
                }
                await Wait(()=>editor.Element.IsLoaded && editor.ReviewScene.Children.OfType<Image>().Last().Source is not null);
                await Task.Delay(150);
                async Task<byte[]> Frame(string name)
                {
                    await Task.Delay(100);page.UpdateLayout();
                    var bitmap=new RenderTargetBitmap();await bitmap.RenderAsync(editor.ReviewScene);
                    var data=(await bitmap.GetPixelsAsync()).ToArray();
                    await ReviewCaptureNativeScaleAsync(editor.Element,Path.Combine(output,name+".png"));
                    return data;
                }
                var zero=await Frame(target+"-shadow-zero");
                editor.SetSettings(new JsonObject{["logo_shadow_opacity"]=100});
                await Wait(()=>editor.ReviewShadowReady);
                var full=await Frame(target+"-shadow-full");
                int changed=0,darkened=0;
                for(int i=0;i<Math.Min(zero.Length,full.Length);i+=4)
                {if(zero[i]!=full[i]||zero[i+1]!=full[i+1]||zero[i+2]!=full[i+2])changed++;if(full[i]<zero[i])darkened++;}
                Check(zero.Length==full.Length && changed>100 && darkened>100,target+" shadow slider changes actual rendered preview pixels: "+changed+" changed, "+darkened+" darkened");
                editor.SetSettings(new JsonObject{["logo_shadow_opacity"]=0});
                var reset=await Frame(target+"-shadow-reset");
                Check(zero.SequenceEqual(reset),target+" setting opacity back to zero restores identical preview pixels");
            }
        }
        catch(Exception error){failures.Add(error.ToString());}
        File.WriteAllText(Path.Combine(output,"shadow-native.json"),JsonSerializer.Serialize(new{Passed=failures.Count==0,Checks=checks,Failures=failures,ExportOrSteamMutation=false},new JsonSerializerOptions{WriteIndented=true}));
    }
}
#endif
