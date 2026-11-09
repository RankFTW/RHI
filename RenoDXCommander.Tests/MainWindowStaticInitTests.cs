using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// Background code (launch watchers, the overlay) calls MainWindow's static helpers, which can be the first
/// thing to touch MainWindow. Its static initializer must not create WinUI objects (brushes, cursors), which
/// throw off the UI thread and break every later use of MainWindow (top bar / Tabs chrome missing).
/// </summary>
public class MainWindowStaticInitTests
{
    [Fact]
    public void StaticInitializer_RunsOffTheUiThread()
    {
        System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(MainWindow).TypeHandle);
    }
}
