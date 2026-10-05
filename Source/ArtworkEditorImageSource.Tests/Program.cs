using Playhub.Importing;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

int checks=0;
void Check(bool condition,string name) { if(!condition) throw new Exception(name); Console.WriteLine("PASS "+name);checks++; }
async Task Reject(Func<Task> action,string name)
{
    bool refused=false;
    try { await action(); }
    catch(Exception error) when(error is IOException or InvalidDataException or ArgumentException or OperationCanceledException or System.Runtime.InteropServices.COMException) { refused=true; }
    Check(refused,name);
}
async Task<byte[]> Encode(Guid format,uint width,uint height)
{
    using var stream=new InMemoryRandomAccessStream();
    var encoder=await BitmapEncoder.CreateAsync(format,stream);
    byte[] pixels=new byte[width*height*4];
    for(int i=0;i<pixels.Length;i+=4) { pixels[i]=17;pixels[i+1]=121;pixels[i+2]=233;pixels[i+3]=255; }
    encoder.SetPixelData(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Straight,width,height,96,96,pixels);
    await encoder.FlushAsync();stream.Seek(0);
    using var reader=new DataReader(stream);await reader.LoadAsync((uint)stream.Size);
    byte[] bytes=new byte[stream.Size];reader.ReadBytes(bytes);return bytes;
}
async Task FullDecode(ArtworkEditorImageData image,string name)
{
    using var stream=await ArtworkEditorImageSource.StreamAsync(image.Bytes,CancellationToken.None);
    var decoder=await BitmapDecoder.CreateAsync(stream);
    var pixels=await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Straight,new BitmapTransform(),ExifOrientationMode.RespectExifOrientation,ColorManagementMode.ColorManageToSRgb);
    Check(pixels.DetachPixelData().Length==image.Width*image.Height*4,name);
}
string root=Path.Combine(Path.GetTempPath(),"Playhub-preview-fixture-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    byte[] jpeg=await Encode(BitmapEncoder.JpegEncoderId,320,180);
    byte[] png=await Encode(BitmapEncoder.PngEncoderId,64,96);
    string longRoot=Path.Combine(root,new string('a',85),new string('b',85),new string('c',85));
    Directory.CreateDirectory(longRoot);
    string background=Path.Combine(longRoot,"pristine-source.jpg"),logo=Path.Combine(longRoot,"separate-logo.png");
    Check(background.Length>327,"fixture background exceeds actual 327-character path");
    await File.WriteAllBytesAsync(background,jpeg);await File.WriteAllBytesAsync(logo,png);
    var image=await ArtworkEditorImageSource.ReadAsync(background,CancellationToken.None);
    Check(image.Width==320 && image.Height==180 && image.Bytes.SequenceEqual(jpeg),"long JPEG file read is lossless with verified dimensions");
    await FullDecode(image,"long JPEG stream fully decodes for background preview");
    var logoImage=await ArtworkEditorImageSource.ReadAsync(logo,CancellationToken.None);
    Check(logoImage.Width==64 && logoImage.Height==96,"long PNG preserves logo aspect ratio");
    await FullDecode(logoImage,"long PNG stream fully decodes for LC and Perfect logo preview");
    string shortPath=Path.Combine(root,"legacy.jpg");await File.WriteAllBytesAsync(shortPath,jpeg);
    Check((await ArtworkEditorImageSource.ReadAsync(shortPath,CancellationToken.None)).Bytes.SequenceEqual(image.Bytes),"short legacy and long pristine path share byte-stream semantics");
    await Reject(async()=>{await ArtworkEditorImageSource.ReadAsync("relative.jpg",CancellationToken.None);},"relative path refused");
    await Reject(async()=>{await ArtworkEditorImageSource.ReadAsync(Path.Combine(root,"missing.jpg"),CancellationToken.None);},"missing replacement refused");
    string invalid=Path.Combine(root,"invalid.jpg");await File.WriteAllBytesAsync(invalid,new byte[48]);
    await Reject(async()=>{await ArtworkEditorImageSource.ReadAsync(invalid,CancellationToken.None);},"invalid image decoder refusal");
    string empty=Path.Combine(root,"empty.png");await File.WriteAllBytesAsync(empty,Array.Empty<byte>());
    await Reject(async()=>{await ArtworkEditorImageSource.ReadAsync(empty,CancellationToken.None);},"empty file refused");
    string large=Path.Combine(root,"too-large.jpg");using(var file=File.Create(large))file.SetLength(ArtworkEditorImageSource.MaximumBytes+1L);
    await Reject(async()=>{await ArtworkEditorImageSource.ReadAsync(large,CancellationToken.None);},"oversize byte payload refused before allocation");
    string tooWide=Path.Combine(root,"too-wide.png");await File.WriteAllBytesAsync(tooWide,await Encode(BitmapEncoder.PngEncoderId,8193,2));
    await Reject(async()=>{await ArtworkEditorImageSource.ReadAsync(tooWide,CancellationToken.None);},"oversize decoded dimensions refused");
    using var canceled=new CancellationTokenSource();canceled.Cancel();
    await Reject(async()=>{await ArtworkEditorImageSource.ReadAsync(background,canceled.Token);},"canceled replacement cannot provide pixels");
    if(args.Length==1 && args[0]=="--junction")
    {
        string outside=Path.Combine(Path.GetTempPath(),"Playhub-preview-outside-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(outside);
        string redirect=Path.Combine(root,"redirect");
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(outside,"outside.jpg"),jpeg);
            var command=new System.Diagnostics.ProcessStartInfo("powershell.exe") { UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true };
            command.ArgumentList.Add("-NoProfile");command.ArgumentList.Add("-Command");
            command.ArgumentList.Add($"New-Item -ItemType Junction -Path '{redirect}' -Target '{outside}' | Out-Null");
            using var process=System.Diagnostics.Process.Start(command)!;await process.WaitForExitAsync();Check(process.ExitCode==0,"isolated actual junction created");
            await Reject(async()=>{await ArtworkEditorImageSource.ReadAsync(Path.Combine(redirect,"outside.jpg"),CancellationToken.None);},"redirected image ancestor refused");
        }
        finally { if(Directory.Exists(redirect))Directory.Delete(redirect);Directory.Delete(outside,true); }
    }
    Console.WriteLine($"{checks} image-source checks PASS; actual WinUI BitmapImage and visual gate pending root.");
}
finally { Directory.Delete(root,true); }
