using System.Windows.Input;

namespace ONEVO.Agent.TrayApp.Controls;

public partial class GlassConfirmHost : ContentView
{
    public static readonly BindableProperty IsOpenProperty =
        BindableProperty.Create(nameof(IsOpen), typeof(bool), typeof(GlassConfirmHost), false);

    public static readonly BindableProperty TitleProperty =
        BindableProperty.Create(nameof(Title), typeof(string), typeof(GlassConfirmHost), string.Empty);

    public static readonly BindableProperty MessageProperty =
        BindableProperty.Create(nameof(Message), typeof(string), typeof(GlassConfirmHost), string.Empty);

    public static readonly BindableProperty CancelTextProperty =
        BindableProperty.Create(nameof(CancelText), typeof(string), typeof(GlassConfirmHost), "Cancel");

    public static readonly BindableProperty ConfirmTextProperty =
        BindableProperty.Create(nameof(ConfirmText), typeof(string), typeof(GlassConfirmHost), "Confirm");

    public static readonly BindableProperty CancelCommandProperty =
        BindableProperty.Create(nameof(CancelCommand), typeof(ICommand), typeof(GlassConfirmHost));

    public static readonly BindableProperty ConfirmCommandProperty =
        BindableProperty.Create(nameof(ConfirmCommand), typeof(ICommand), typeof(GlassConfirmHost));

    public static readonly BindableProperty BodyProperty =
        BindableProperty.Create(nameof(Body), typeof(View), typeof(GlassConfirmHost),
            propertyChanged: OnBodyChanged);

    public bool IsOpen
    {
        get => (bool)GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Message
    {
        get => (string)GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public string CancelText
    {
        get => (string)GetValue(CancelTextProperty);
        set => SetValue(CancelTextProperty, value);
    }

    public string ConfirmText
    {
        get => (string)GetValue(ConfirmTextProperty);
        set => SetValue(ConfirmTextProperty, value);
    }

    public ICommand? CancelCommand
    {
        get => (ICommand?)GetValue(CancelCommandProperty);
        set => SetValue(CancelCommandProperty, value);
    }

    public ICommand? ConfirmCommand
    {
        get => (ICommand?)GetValue(ConfirmCommandProperty);
        set => SetValue(ConfirmCommandProperty, value);
    }

    public View? Body
    {
        get => (View?)GetValue(BodyProperty);
        set => SetValue(BodyProperty, value);
    }

    public GlassConfirmHost()
    {
        InitializeComponent();
        ApplyBody();
    }

    private static void OnBodyChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is GlassConfirmHost host)
            host.ApplyBody();
    }

    private void ApplyBody()
    {
        if (BodyHost is null)
            return;

        BodyHost.Content = Body;
        BodyHost.IsVisible = Body is not null;
    }
}
