using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Composition;
using System.Numerics;
using System.Text.Json.Nodes;
using Playhub.Integrations;
using Playhub.Services;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Windows.Foundation;
using Windows.System;

namespace Playhub.Importing;

/// <summary>Event-driven image placement. The owning Info save queue handles persistence and debounce.</summary>
public sealed class LaunchCurtainVisualEditor : IDisposable
{
    private readonly Action<JsonObject> _changed;
    private readonly Canvas _scene;
    private readonly Image _background = new() { Stretch = Stretch.Fill, IsHitTestVisible = false };
    private readonly Image _logo = new() { Stretch = Stretch.Fill };
    private readonly Canvas _shadowHost = new() { IsHitTestVisible=false };
    private SpriteVisual? _shadowVisual;
    private DropShadow? _shadow;
    private ImageSource? _shadowSource;
    private bool _logoDecoded;
    private readonly ContentControl _surface;
    private readonly ArtworkEditorGamepadInput _gamepad;
    private readonly Border _frame;
    private readonly TextBlock _empty;
    private readonly double _aspect, _logoWidthFraction, _logoHeightFraction;
    private readonly bool _trimLogoPadding;
    private readonly bool _showEmptyBackgroundPrompt;
    private readonly double _resetLogoX,_resetLogoY,_maximumPreviewHeight;
    private CancellationTokenSource? _backgroundLoad, _logoLoad;
    private readonly JsonObject _settings = new();
    private double _backgroundWidth = 16, _backgroundHeight = 9, _logoWidth = 2, _logoHeight = 1;
    private uint? _pointer;
    private Point _origin;
    private double _initialX, _initialY;
    private string _target = "logo";
    private bool _dragChanged, _disposed;
#if PLAYHUB_UI_REVIEW
    private bool _pointerMoveReported;
#endif
    private const double SceneWidth = 1600;
    private double SceneHeight => SceneWidth / _aspect;
    public FrameworkElement Element { get; }

    public LaunchCurtainVisualEditor(string? backgroundPath, string? logoPath, JsonObject settings, Action<JsonObject> changed,
        Func<string,string>? text = null, double aspectRatio = 16d/9d, double logoWidthFraction = .42, double logoHeightFraction = .20, bool trimLogoPadding = false,
        double resetLogoX = 50, double resetLogoY = 50, double maxPreviewHeight = 260, bool showEmptyBackgroundPrompt = true)
    {
        _changed = changed;
        _aspect = double.IsFinite(aspectRatio) && aspectRatio is >= 1 and <= 4 ? aspectRatio : 16d/9d;
        _logoWidthFraction = ArtworkEditorGeometry.Clamp(logoWidthFraction,.42,.05,1);
        _logoHeightFraction = ArtworkEditorGeometry.Clamp(logoHeightFraction,.20,.05,1);
        _trimLogoPadding = trimLogoPadding;
        _showEmptyBackgroundPrompt = showEmptyBackgroundPrompt;
        _resetLogoX=ArtworkEditorGeometry.Clamp(resetLogoX,50,0,100);_resetLogoY=ArtworkEditorGeometry.Clamp(resetLogoY,50,0,100);
        _maximumPreviewHeight=ArtworkEditorGeometry.Clamp(maxPreviewHeight,260,100,600);
        string T(string key) => text?.Invoke(key) ?? key;
        _scene = new Canvas { Width = SceneWidth, Height = SceneHeight, Background = new SolidColorBrush(Microsoft.UI.Colors.Black), Clip = new RectangleGeometry { Rect = new(0,0,SceneWidth,SceneHeight) } };
        _scene.Children.Add(_background); _scene.Children.Add(_shadowHost); _scene.Children.Add(_logo);
        var view = new Viewbox { Child = _scene, Stretch = Stretch.Uniform };
        _surface = new ContentControl { Content = view, IsTabStop = true, UseSystemFocusVisuals=true, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
        AutomationProperties.SetName(_surface,T("Anteprima"));
        _frame = new Border { Child = _surface, Height=_maximumPreviewHeight,MaxHeight=_maximumPreviewHeight,MaxWidth=_maximumPreviewHeight*_aspect, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), BorderBrush = Brush("ControlStrokeColorDefaultBrush",Microsoft.UI.Colors.DimGray), Background = new SolidColorBrush(Microsoft.UI.Colors.Black), HorizontalAlignment = HorizontalAlignment.Center };
        _empty = new TextBlock { Text = T("Scegli uno sfondo"), Width=1000,TextAlignment=TextAlignment.Center, Opacity = .55, FontSize = 40, Foreground = new SolidColorBrush(Microsoft.UI.Colors.White), IsHitTestVisible = false };
        Canvas.SetLeft(_empty,(SceneWidth-1000)/2); Canvas.SetTop(_empty,SceneHeight/2-25); _scene.Children.Insert(1,_empty);
        var controls = new Grid { ColumnSpacing = 8,RowSpacing=8 };
        controls.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto }); controls.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
        controls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        controls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1,GridUnitType.Star) });
        controls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var target = new ComboBox { MinWidth = 120, HorizontalAlignment = HorizontalAlignment.Left, Items = { T("Logo"),T("Sfondo") }, SelectedIndex = 0 };
        target.SelectionChanged += (_,_) => { EndDrag(); _target = target.SelectedIndex == 1 ? "background" : "logo"; };
        AutomationProperties.SetName(target,T("Modifica")); controls.Children.Add(target);
        var directions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center };
        directions.Children.Add(Button("\uE76B",T("Sinistra"),()=>Move(-1,0)));
        directions.Children.Add(Button("\uE70E",T("Su"),()=>Move(0,-1)));
        directions.Children.Add(Button("\uE70D",T("Giù"),()=>Move(0,1)));
        directions.Children.Add(Button("\uE76C",T("Destra"),()=>Move(1,0)));
        Grid.SetColumn(directions,1); controls.Children.Add(directions);
        var zoom = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        zoom.Children.Add(Button("\uE738",T("Riduci"),()=>Zoom(-10)));
        zoom.Children.Add(Button("\uE710",T("Ingrandisci"),()=>Zoom(10)));
        zoom.Children.Add(Button("\uE777",T("Ripristina"),Reset));
        Grid.SetColumn(zoom,2); controls.Children.Add(zoom);
        controls.SizeChanged += (_,args) =>
        {
            bool narrow=args.NewSize.Width<520;
            Grid.SetRow(target,0);Grid.SetColumnSpan(target,narrow?3:1);
            Grid.SetRow(directions,narrow?1:0);Grid.SetColumn(directions,narrow?0:1);Grid.SetColumnSpan(directions,narrow?2:1);
            Grid.SetRow(zoom,narrow?1:0);Grid.SetColumn(zoom,2);
        };
        var panel = new StackPanel { Spacing = 12, HorizontalAlignment = HorizontalAlignment.Stretch };
        panel.Children.Add(_frame); panel.Children.Add(controls);
        Element = panel;
        Element.SizeChanged += EditorSizeChanged;
        _scene.PointerPressed += PointerPressed; _scene.PointerMoved += PointerMoved; _scene.PointerReleased += PointerReleased;
        _scene.PointerCanceled += PointerReleased; _scene.PointerCaptureLost += PointerReleased;
#if PLAYHUB_UI_REVIEW
        _surface.AddHandler(UIElement.PointerPressedEvent,new PointerEventHandler(ObservePointerPressed),true);
#endif
        _surface.KeyDown += KeyDown; Element.Loaded += Loaded;Element.Unloaded += Unloaded;_logo.ImageOpened+=LogoImageOpened;
        _gamepad=new(Element,_surface,Move,Zoom,Diag.Step);
        SetSettings(settings); SetImages(backgroundPath,logoPath);
    }

    private static Brush Brush(string key,Windows.UI.Color fallback) => Application.Current.Resources.TryGetValue(key,out var resource) && resource is Brush value ? value : new SolidColorBrush(fallback);
    private static Button Button(string glyph,string label,Action action)
    {
        var button = new Button { Content = new FontIcon { Glyph = glyph,FontSize = 16 }, MinWidth = 40,MinHeight = 40 };
        AutomationProperties.SetName(button,label); ToolTipService.SetToolTip(button,label); button.Click += (_,_)=>action(); return button;
    }
    private void EditorSizeChanged(object sender,SizeChangedEventArgs args)
    {
        var bounds = ArtworkEditorGeometry.Preview(args.NewSize.Width,_aspect,_maximumPreviewHeight);
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        if (double.IsNaN(_frame.Width) || Math.Abs(_frame.Width-bounds.Width)>.5) _frame.Width=bounds.Width;
        if (double.IsNaN(_frame.Height) || Math.Abs(_frame.Height-bounds.Height)>.5) _frame.Height=bounds.Height;
    }
    public void SetImages(string? backgroundPath,string? logoPath)
    {
        if (_disposed) return;
        StartImageLoad(backgroundPath,_background,ref _backgroundLoad);
        StartImageLoad(logoPath,_logo,ref _logoLoad);
        _empty.Visibility = _showEmptyBackgroundPrompt && _background.Source is null ? Visibility.Visible : Visibility.Collapsed;
        Paint();
    }
    private void StartImageLoad(string? path,Image destination,ref CancellationTokenSource? current)
    {
        current?.Cancel(); current=null;
        if(string.IsNullOrWhiteSpace(path))
        { if(destination==_logo) { _logoDecoded=false;ReleaseShadow(); } destination.Source=null; return; }
        var load=current=new CancellationTokenSource();
        _=LoadImageAsync(path,destination,load);
    }
    private bool CurrentImageLoad(Image destination,CancellationTokenSource load) =>
        !_disposed && !load.IsCancellationRequested && ReferenceEquals(destination==_background?_backgroundLoad:_logoLoad,load);

    private async Task LoadImageAsync(string path,Image destination,CancellationTokenSource load)
    {
        string role=destination==_background?"background":"logo";
        try
        {
            var image=await ArtworkEditorImageSource.ReadAsync(path,load.Token);
            using var input=await ArtworkEditorImageSource.StreamAsync(image.Bytes,load.Token);
            using var output=new InMemoryRandomAccessStream();
            double width=image.Width,height=image.Height;
            bool trim=destination==_logo && _trimLogoPadding;
            if(trim)
            {
                var decoder=await BitmapDecoder.CreateAsync(input).AsTask(load.Token);
                var data=await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Straight,new BitmapTransform(),ExifOrientationMode.RespectExifOrientation,ColorManagementMode.ColorManageToSRgb).AsTask(load.Token);
                var pixels=new ArtworkPixels((int)image.Width,(int)image.Height,data.DetachPixelData());
                var bounds=await Task.Run(()=>PerfectArtworkPixels.LogoBounds(pixels,load.Token),load.Token);
                var cropped=new byte[bounds.Width*bounds.Height*4];
                for(int y=0;y<bounds.Height;y++)
                { load.Token.ThrowIfCancellationRequested(); System.Buffer.BlockCopy(pixels.Bgra,((y+bounds.Y)*pixels.Width+bounds.X)*4,cropped,y*bounds.Width*4,bounds.Width*4); }
                var encoder=await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId,output).AsTask(load.Token);
                encoder.SetPixelData(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Straight,(uint)bounds.Width,(uint)bounds.Height,96,96,cropped);
                await encoder.FlushAsync().AsTask(load.Token);output.Seek(0);width=bounds.Width;height=bounds.Height;
            }
            var bitmap=new BitmapImage { DecodePixelWidth=1600 };
            bitmap.ImageFailed+=(_,args)=>
            {
                if(CurrentImageLoad(destination,load) || (!_disposed && destination.Source==bitmap))
                    Diag.Step($"Artwork preview ImageFailed role={role} pathLength={path.Length}");
            };
            await bitmap.SetSourceAsync(trim?output:input).AsTask(load.Token);
            if(!CurrentImageLoad(destination,load)) return;
            if(destination==_background) { _backgroundWidth=width;_backgroundHeight=height; }
            else { _logoWidth=width;_logoHeight=height;_logoDecoded=false;ReleaseShadow(); }
            destination.Source=bitmap;
            _empty.Visibility=_showEmptyBackgroundPrompt && _background.Source is null?Visibility.Visible:Visibility.Collapsed;Paint();
            Diag.Step($"Artwork preview loaded role={role} pathLength={path.Length} size={width}x{height}");
        }
        catch(OperationCanceledException) { }
        catch(Exception error) when(error is IOException or InvalidDataException or ArgumentException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            if(CurrentImageLoad(destination,load)) Diag.Step($"Artwork preview failed role={role} pathLength={path.Length} type={error.GetType().Name} hresult={error.HResult:X8}");
        }
        finally
        {
            if(destination==_background && ReferenceEquals(_backgroundLoad,load)) _backgroundLoad=null;
            if(destination==_logo && ReferenceEquals(_logoLoad,load)) _logoLoad=null;
            load.Dispose();
        }
    }
    public void SetSettings(JsonObject settings)
    {
        if (_disposed) return;
        foreach (string key in new[] { "logo_position_x","logo_position_y","logo_scale","background_position_x","background_position_y","background_scale","background_opacity","logo_shadow_opacity","logo_shadow_blur","show_logo" })
            if (settings.ContainsKey(key)) _settings[key] = settings[key]?.DeepClone();
        Paint();
    }
    private double Number(string key,double fallback,double min,double max) => double.TryParse(_settings[key]?.ToString(),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out double value) ? ArtworkEditorGeometry.Clamp(value,fallback,min,max) : fallback;
    private void Paint()
    {
        var bg = ArtworkEditorGeometry.Background(SceneWidth,SceneHeight,_backgroundWidth,_backgroundHeight,
            Number("background_position_x",50,0,100),Number("background_position_y",50,0,100),Number("background_scale",100,100,200));
        Place(_background,bg); _background.Opacity=Number("background_opacity",100,0,100)/100;
        var logo = ArtworkEditorGeometry.Logo(SceneWidth,SceneHeight,_logoWidth,_logoHeight,
            Number("logo_position_x",50,0,100),Number("logo_position_y",50,0,100),Number("logo_scale",100,50,200),_logoWidthFraction,_logoHeightFraction);
        Place(_logo,logo); _logo.Visibility=_settings["show_logo"]?.ToString() == "false" || _logo.Source is null ? Visibility.Collapsed : Visibility.Visible;
        double shadowOpacity=Number("logo_shadow_opacity",0,0,100)/100;
        bool visibleShadow=_logo.Visibility==Visibility.Visible && _logoDecoded && _logo.IsLoaded && Element.IsLoaded && shadowOpacity>0;
#if PLAYHUB_UI_REVIEW
        if(Environment.GetEnvironmentVariable("PLAYHUB_REVIEW_DISABLE_EDITOR_SHADOW")=="1")visibleShadow=false;
#endif
        if(visibleShadow)
        {
            if(_shadowVisual is null)
            {
                var compositor=ElementCompositionPreview.GetElementVisual(_scene).Compositor;
                _shadowVisual=compositor.CreateSpriteVisual();_shadow=compositor.CreateDropShadow();_shadowVisual.Shadow=_shadow;
                ElementCompositionPreview.SetElementChildVisual(_shadowHost,_shadowVisual);
#if PLAYHUB_UI_REVIEW
                Diag.Step($"Artwork preview shadow created loaded={Element.IsLoaded} opacity={shadowOpacity}");
#endif
            }
            _shadowVisual.IsVisible=true;_shadowVisual.Size=new((float)logo.Width,(float)logo.Height);
            _shadowVisual.Offset=new((float)logo.X,(float)logo.Y,0);
            if(!ReferenceEquals(_shadowSource,_logo.Source)) { _shadow!.Mask=_logo.GetAlphaMask();_shadowSource=_logo.Source; }
            _shadow!.Color=Microsoft.UI.Colors.Black;
            _shadow.BlurRadius=(float)(SceneWidth*.006*Number("logo_shadow_blur",40,0,100)/40);
            _shadow.Opacity=(float)shadowOpacity;_shadow.Offset=new Vector3(0,(float)(SceneWidth*.003),0);
        }
        else ReleaseShadow();
    }
    private void LogoImageOpened(object sender,RoutedEventArgs args)
    { if(!_disposed) { _logoDecoded=true;Paint(); } }
    private void Loaded(object sender,RoutedEventArgs args) { if(!_disposed)Paint(); }
    private void ReleaseShadow()
    {
        if(_shadowVisual is null)return;
        ElementCompositionPreview.SetElementChildVisual(_shadowHost,null);
        _shadowVisual.Dispose();_shadow?.Dispose();_shadowVisual=null;_shadow=null;_shadowSource=null;
    }
    private static void Place(Image image,ArtworkEditorRect rect)
    { image.Width=rect.Width; image.Height=rect.Height; Canvas.SetLeft(image,rect.X); Canvas.SetTop(image,rect.Y); }
    private string X => _target=="logo" ? "logo_position_x" : "background_position_x";
    private string Y => _target=="logo" ? "logo_position_y" : "background_position_y";
    private string Scale => _target=="logo" ? "logo_scale" : "background_scale";
#if PLAYHUB_UI_REVIEW
    private void ObservePointerPressed(object sender,PointerRoutedEventArgs args)
    {
        var point=args.GetCurrentPoint(_scene);
        Diag.Step($"Artwork pointer surface press id={args.Pointer.PointerId} source={args.OriginalSource?.GetType().Name} handled={args.Handled} left={point.Properties.IsLeftButtonPressed} x={point.Position.X:F1} y={point.Position.Y:F1}");
    }
#endif
    private void PointerPressed(object sender,PointerRoutedEventArgs args)
    {
#if PLAYHUB_UI_REVIEW
        Diag.Step($"Artwork pointer scene press id={args.Pointer.PointerId} target={_target} active={_pointer is not null} disposed={_disposed} logoVisible={_logo.Visibility==Visibility.Visible}");
#endif
        if (_disposed || _pointer is not null || !args.GetCurrentPoint(_scene).Properties.IsLeftButtonPressed) return;
        if (_target=="logo" && _logo.Visibility!=Visibility.Visible) return;
        _origin=args.GetCurrentPoint(_scene).Position; _initialX=Number(X,50,0,100); _initialY=Number(Y,50,0,100);
#if PLAYHUB_UI_REVIEW
        bool captured=_scene.CapturePointer(args.Pointer);
        Diag.Step($"Artwork pointer capture id={args.Pointer.PointerId} captured={captured}");
        _pointerMoveReported=false;
        if (!captured) return;
#else
        if (!_scene.CapturePointer(args.Pointer)) return;
#endif
        _pointer=args.Pointer.PointerId; _dragChanged=false; _surface.Focus(FocusState.Pointer); args.Handled=true;
    }
    private void PointerMoved(object sender,PointerRoutedEventArgs args)
    {
        if (_pointer != args.Pointer.PointerId || _disposed) return;
        Point point=args.GetCurrentPoint(_scene).Position;
#if PLAYHUB_UI_REVIEW
        if(!_pointerMoveReported)
        {
            _pointerMoveReported=true;
            Diag.Step($"Artwork pointer first move id={args.Pointer.PointerId} left={args.GetCurrentPoint(_scene).Properties.IsLeftButtonPressed} dx={point.X-_origin.X:F1} dy={point.Y-_origin.Y:F1}");
        }
#endif
        var next=_target=="logo" ? ArtworkEditorGeometry.Drag(_initialX,_initialY,point.X-_origin.X,point.Y-_origin.Y,SceneWidth,SceneHeight)
            : ArtworkEditorGeometry.DragBackground(_initialX,_initialY,point.X-_origin.X,point.Y-_origin.Y,SceneWidth,SceneHeight,
                ArtworkEditorGeometry.Background(SceneWidth,SceneHeight,_backgroundWidth,_backgroundHeight,_initialX,_initialY,Number("background_scale",100,100,200)));
        _settings[X]=next.X; _settings[Y]=next.Y; _dragChanged=true; Paint(); args.Handled=true;
    }
    private void PointerReleased(object sender,PointerRoutedEventArgs args)
    {
#if PLAYHUB_UI_REVIEW
        Diag.Step($"Artwork pointer terminal id={args.Pointer.PointerId} active={_pointer==args.Pointer.PointerId} changed={_dragChanged}");
#endif
        if (_pointer==args.Pointer.PointerId) { EndDrag(); args.Handled=true; }
    }
    private void EndDrag(bool publish=true)
    {
        if (_pointer is null) return;
#if PLAYHUB_UI_REVIEW
        Diag.Step($"Artwork pointer end id={_pointer} changed={_dragChanged} publish={publish && !_disposed} target={_target}");
#endif
        _pointer=null; _scene.ReleasePointerCaptures();
        if (_dragChanged && !_disposed && publish) Publish(X,Y);
        _dragChanged=false;
    }
    public void Move(double dx,double dy)
    {
        if (_disposed) return;
        EndDrag();
        _settings[X]=ArtworkEditorGeometry.Clamp(Number(X,50,0,100)+dx*(_target=="logo" ? 1 : -1),50,0,100);
        _settings[Y]=ArtworkEditorGeometry.Clamp(Number(Y,50,0,100)+dy*(_target=="logo" ? 1 : -1),50,0,100);
        Paint(); Publish(X,Y);
    }
    public void Zoom(double delta)
    {
        if (_disposed) return;
        EndDrag();
        _settings[Scale]=ArtworkEditorGeometry.Clamp(Number(Scale,100,_target=="logo" ? 50 : 100,200)+delta,100,_target=="logo" ? 50 : 100,200);
        Paint(); Publish(Scale);
    }
    public void Reset()
    {
        if (_disposed) return;
        EndDrag();
        var reset=ArtworkEditorGeometry.Reset(_target,_resetLogoX,_resetLogoY);
        _settings[X]=reset.X;_settings[Y]=reset.Y;_settings[Scale]=reset.Scale;
        Paint(); Publish(X,Y,Scale);
    }
    private void Publish(params string[] keys)
    { var partial=new JsonObject(); foreach(string key in keys) partial[key]=_settings[key]?.DeepClone(); _changed(partial); }
    private void KeyDown(object sender,KeyRoutedEventArgs args)
    {
        switch(args.Key)
        {
            case VirtualKey.Left: Move(-1,0); break;
            case VirtualKey.Right: Move(1,0); break;
            case VirtualKey.Up: Move(0,-1); break;
            case VirtualKey.Down: Move(0,1); break;
            case VirtualKey.Add: Zoom(10); break;
            case VirtualKey.Subtract: Zoom(-10); break;
            default: return;
        }
        args.Handled=true;
    }
    public void CommitPendingChanges() { if(!_disposed) EndDrag(); }
    private void Unloaded(object sender,RoutedEventArgs args) { EndDrag(false);ReleaseShadow(); }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed=true;EndDrag(false);
        _gamepad.Dispose();
        _backgroundLoad?.Cancel();_logoLoad?.Cancel();
        _scene.PointerPressed-=PointerPressed; _scene.PointerMoved-=PointerMoved; _scene.PointerReleased-=PointerReleased;
        _scene.PointerCanceled-=PointerReleased; _scene.PointerCaptureLost-=PointerReleased;
#if PLAYHUB_UI_REVIEW
        _surface.RemoveHandler(UIElement.PointerPressedEvent,new PointerEventHandler(ObservePointerPressed));
#endif
        _surface.KeyDown-=KeyDown; Element.SizeChanged-=EditorSizeChanged; Element.Loaded-=Loaded;Element.Unloaded-=Unloaded;_logo.ImageOpened-=LogoImageOpened;
        ReleaseShadow();
        _background.Source=null; _logo.Source=null;
    }
}
