using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Gaming.Input;

namespace Playhub.Importing;

internal sealed class ArtworkEditorGamepadInput : IDisposable
{
    private readonly FrameworkElement _element,_surface;
    private readonly Action<double,double> _move;
    private readonly Action<double> _zoom;
    private readonly Action<string>? _diagnostic;
    private readonly DispatcherTimer _timer=new() { Interval=TimeSpan.FromMilliseconds(100) };
    private readonly ArtworkEditorGamepadPolicy _policy=new();
    private readonly List<(DependencyObject Element,long Token)> _visibility=new();
    private Gamepad[] _pads=[];
    private InputActivationListener? _activation;
    private bool _loaded,_disposed;

    // WinUI 3 does not translate gamepads into virtual keys; read hardware only in the focused preview.
    // https://learn.microsoft.com/windows/apps/windows-app-sdk/migrate-to-windows-app-sdk/what-is-supported
    // https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.input.inputactivationlistener
    public ArtworkEditorGamepadInput(FrameworkElement element,FrameworkElement surface,Action<double,double> move,Action<double> zoom,Action<string>? diagnostic=null)
    {
        _element=element;_surface=surface;_move=move;_zoom=zoom;_diagnostic=diagnostic;
        _element.Loaded+=Loaded;_element.Unloaded+=Unloaded;
        _element.GotFocus+=FocusChanged;_element.LostFocus+=FocusChanged;_timer.Tick+=Tick;
        if(_element.IsLoaded)Attach();
    }
    private void Loaded(object sender,RoutedEventArgs args)=>Attach();
    private void Unloaded(object sender,RoutedEventArgs args)=>Detach();
    private void Attach()
    {
        if(_disposed||_loaded||_element.XamlRoot is null)return;
        _loaded=true;
        for(DependencyObject? current=_element;current is not null;current=VisualTreeHelper.GetParent(current))
            if(current is UIElement)
                _visibility.Add((current,current.RegisterPropertyChangedCallback(UIElement.VisibilityProperty,VisibilityChanged)));
        try
        {
            _activation=InputActivationListener.GetForWindowId(_element.XamlRoot.ContentIslandEnvironment.AppWindowId);
            if(_activation is not null)_activation.InputActivationChanged+=ActivationChanged;
            Gamepad.GamepadAdded+=PadsChanged;Gamepad.GamepadRemoved+=PadsChanged;RefreshPads();
        }
        catch(System.Runtime.InteropServices.COMException error) { _diagnostic?.Invoke($"Artwork gamepad unavailable hresult={error.HResult:X8}");Stop(); }
    }
    private void Detach()
    {
        Stop();
        if(!_loaded)return;
        _loaded=false;Gamepad.GamepadAdded-=PadsChanged;Gamepad.GamepadRemoved-=PadsChanged;
        if(_activation is not null)_activation.InputActivationChanged-=ActivationChanged;
        _activation=null;_pads=[];
        foreach(var observer in _visibility)observer.Element.UnregisterPropertyChangedCallback(UIElement.VisibilityProperty,observer.Token);
        _visibility.Clear();
    }
    private void PadsChanged(object? sender,Gamepad pad)=>_element.DispatcherQueue.TryEnqueue(()=>{if(_loaded&&!_disposed)RefreshPads();});
    private void RefreshPads() { _pads=Gamepad.Gamepads.ToArray();RefreshActivity(); }
    private void ActivationChanged(InputActivationListener sender,InputActivationListenerActivationChangedEventArgs args)=>RefreshActivity();
    private void VisibilityChanged(DependencyObject sender,DependencyProperty property)=>RefreshActivity();
    private void FocusChanged(object sender,RoutedEventArgs args)=>_element.DispatcherQueue.TryEnqueue(RefreshActivity);
    private bool Active()
    {
        if(_disposed||!_loaded||!_element.IsLoaded||_pads.Length==0||_activation?.State!=InputActivationState.Activated||_element.XamlRoot is null)return false;
        foreach(var observer in _visibility)if(observer.Element is UIElement element&&element.Visibility!=Visibility.Visible)return false;
        for(DependencyObject? focus=FocusManager.GetFocusedElement(_element.XamlRoot) as DependencyObject;focus is not null;focus=VisualTreeHelper.GetParent(focus))
            if(ReferenceEquals(focus,_surface))return true;
        return false;
    }
    private void RefreshActivity()
    {
        if(!Active()) { Stop();return; }
        if(_timer.IsEnabled)return;
        _policy.Read(true,ReadButtons(),Environment.TickCount64);_timer.Start();
#if PLAYHUB_UI_REVIEW
        _diagnostic?.Invoke($"Artwork gamepad active pads={_pads.Length} intervalMs=100");
#endif
    }
    private ArtworkEditorPadButtons ReadButtons()
    {
        var buttons=ArtworkEditorPadButtons.None;
        foreach(var pad in _pads)
        {
            GamepadReading reading;
            try { reading=pad.GetCurrentReading(); }catch(System.Runtime.InteropServices.COMException) { continue; }
            if((reading.Buttons&GamepadButtons.DPadLeft)!=0||reading.LeftThumbstickX<-.5)buttons|=ArtworkEditorPadButtons.Left;
            if((reading.Buttons&GamepadButtons.DPadRight)!=0||reading.LeftThumbstickX>.5)buttons|=ArtworkEditorPadButtons.Right;
            if((reading.Buttons&GamepadButtons.DPadUp)!=0||reading.LeftThumbstickY>.5)buttons|=ArtworkEditorPadButtons.Up;
            if((reading.Buttons&GamepadButtons.DPadDown)!=0||reading.LeftThumbstickY<-.5)buttons|=ArtworkEditorPadButtons.Down;
            if((reading.Buttons&GamepadButtons.LeftShoulder)!=0)buttons|=ArtworkEditorPadButtons.ZoomOut;
            if((reading.Buttons&GamepadButtons.RightShoulder)!=0)buttons|=ArtworkEditorPadButtons.ZoomIn;
        }
        return buttons;
    }
    private void Tick(object? sender,object args)
    {
        if(!Active()) { Stop();return; }
        var action=_policy.Read(true,ReadButtons(),Environment.TickCount64);
#if PLAYHUB_UI_REVIEW
        if(action!=default)_diagnostic?.Invoke($"Artwork gamepad edit x={action.X} y={action.Y} zoom={action.Zoom}");
#endif
        if(action.X!=0||action.Y!=0)_move(action.X,action.Y);
        if(action.Zoom!=0)_zoom(action.Zoom);
    }
    private void Stop()
    {
#if PLAYHUB_UI_REVIEW
        if(_timer.IsEnabled)_diagnostic?.Invoke("Artwork gamepad paused");
#endif
        _timer.Stop();_policy.Read(false,ArtworkEditorPadButtons.None,0);
    }
    public void Dispose()
    {
        if(_disposed)return;
        _disposed=true;Detach();_timer.Tick-=Tick;
        _element.Loaded-=Loaded;_element.Unloaded-=Unloaded;_element.GotFocus-=FocusChanged;_element.LostFocus-=FocusChanged;
    }
}
