namespace SmartGym.App;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		var window = new Window(new MainPage()) { Title = "Smart Gym" };
		window.Created += OnWindowCreated;
		return window;
	}

	private void OnWindowCreated(object? sender, EventArgs e)
	{
#if WINDOWS
		if (sender is Window window &&
			window.Handler?.PlatformView is Microsoft.UI.Xaml.Window native)
		{
			var presenter = native.AppWindow.Presenter as Microsoft.UI.Windowing.OverlappedPresenter;
			presenter?.Maximize();
		}
#endif
	}
}
