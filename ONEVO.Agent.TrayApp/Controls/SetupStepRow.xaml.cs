namespace ONEVO.Agent.TrayApp.Controls;

public partial class SetupStepRow : ContentView
{
    public static readonly BindableProperty TitleProperty =
        BindableProperty.Create(nameof(Title), typeof(string), typeof(SetupStepRow), string.Empty);

    public static readonly BindableProperty StatusTextProperty =
        BindableProperty.Create(nameof(StatusText), typeof(string), typeof(SetupStepRow), string.Empty);

    public static readonly BindableProperty IsCompleteProperty =
        BindableProperty.Create(nameof(IsComplete), typeof(bool), typeof(SetupStepRow), false,
            propertyChanged: OnStateChanged);

    public static readonly BindableProperty IsInProgressProperty =
        BindableProperty.Create(nameof(IsInProgress), typeof(bool), typeof(SetupStepRow), false,
            propertyChanged: OnStateChanged);

    public static readonly BindableProperty UseSuccessAccentProperty =
        BindableProperty.Create(nameof(UseSuccessAccent), typeof(bool), typeof(SetupStepRow), false,
            propertyChanged: OnStateChanged);

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string StatusText
    {
        get => (string)GetValue(StatusTextProperty);
        set => SetValue(StatusTextProperty, value);
    }

    public bool IsComplete
    {
        get => (bool)GetValue(IsCompleteProperty);
        set => SetValue(IsCompleteProperty, value);
    }

    public bool IsInProgress
    {
        get => (bool)GetValue(IsInProgressProperty);
        set => SetValue(IsInProgressProperty, value);
    }

    public bool UseSuccessAccent
    {
        get => (bool)GetValue(UseSuccessAccentProperty);
        set => SetValue(UseSuccessAccentProperty, value);
    }

    public bool ShowBrandComplete => IsComplete && !UseSuccessAccent;
    public bool ShowSuccessComplete => IsComplete && UseSuccessAccent;
    public bool ShowPendingRing => !IsComplete && !IsInProgress;
    public bool ShowProgressRing => !IsComplete && IsInProgress;

    public SetupStepRow() => InitializeComponent();

    private static void OnStateChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is not SetupStepRow row)
            return;

        row.OnPropertyChanged(nameof(ShowBrandComplete));
        row.OnPropertyChanged(nameof(ShowSuccessComplete));
        row.OnPropertyChanged(nameof(ShowPendingRing));
        row.OnPropertyChanged(nameof(ShowProgressRing));
    }
}
