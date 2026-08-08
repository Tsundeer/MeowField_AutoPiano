namespace MeowField.App;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        var application = new App();
        application.InitializeComponent();
        application.Run();
    }
}
