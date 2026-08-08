using MeowField.Infrastructure.Windows;
using System.Runtime.Versioning;

namespace MeowField.Infrastructure.Tests;

[SupportedOSPlatform("windows")]
public sealed class WindowsWindowCatalogTests
{
    [Fact]
    public void ListVisibleWindows_ReturnsOnlyLiveHandles()
    {
        var catalog = new WindowsWindowCatalog();

        var windows = catalog.ListVisibleWindows();

        Assert.All(windows, target => Assert.True(catalog.IsWindow(target.Handle)));
        Assert.All(windows, target => Assert.True(target.ProcessId > 0));
    }
}
