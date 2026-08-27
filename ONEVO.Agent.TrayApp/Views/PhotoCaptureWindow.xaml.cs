namespace ONEVO.Agent.TrayApp.Views;

using ONEVO.Agent.TrayApp.ViewModels;

public partial class PhotoCaptureWindow : ContentPage, IQueryAttributable
{
    private readonly PhotoCaptureWindowViewModel _vm;
    private Animation? _scanAnimation;

    public PhotoCaptureWindow(PhotoCaptureWindowViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;

        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PhotoCaptureWindowViewModel.IsScanAnimating))
            {
                if (vm.IsScanAnimating)
                    StartScanAnimation();
                else
                    StopScanAnimation();
            }
            else if (e.PropertyName == nameof(PhotoCaptureWindowViewModel.CapturedPhotoBytes))
            {
                var bytes = vm.CapturedPhotoBytes;
                CapturedPhotoPreview.Source = bytes is { Length: > 0 }
                    ? ImageSource.FromStream(() => new MemoryStream(bytes))
                    : null;
            }
        };
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("context", out var ctx))
            _vm.SetContext(ctx?.ToString());
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.StartPreviewAsync();
    }

    protected override async void OnDisappearing()
    {
        base.OnDisappearing();
        await _vm.StopPreviewAsync();
    }

    private void StartScanAnimation()
    {
        // ScanLine travels from top (0) to bottom of the inner circle frame
        const double frameHeight = 204;
        _scanAnimation = new Animation(v => ScanLine.TranslationY = v,
                                       start: 0,
                                       end: frameHeight,
                                       easing: Easing.Linear);

        _scanAnimation.Commit(
            owner: this,
            name: "ScanLine",
            rate: 16,
            length: 1800,
            repeat: () => _vm.IsScanAnimating,
            finished: (_, _) => ScanLine.TranslationY = 0);
    }

    private void StopScanAnimation()
    {
        this.AbortAnimation("ScanLine");
        ScanLine.TranslationY = 0;
    }
}
