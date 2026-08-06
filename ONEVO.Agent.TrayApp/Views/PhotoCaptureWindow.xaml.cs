namespace ONEVO.Agent.TrayApp.Views;

using ONEVO.Agent.TrayApp.ViewModels;

public partial class PhotoCaptureWindow : ContentPage
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
        };
    }

    private void StartScanAnimation()
    {
        // ScanLine travels from top (0) to bottom (272px) of the circle frame, then reverses
        const double frameHeight = 272;
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
