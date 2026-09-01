using System.Runtime.ExceptionServices;
using RHI.DropHelper;
using Xunit;

namespace RenoDXCommander.Tests;

public class DropOverlayFormTests
{
    private const int WsExToolWindow = 0x00000080;

    [Fact]
    public void CreateParams_MarksOverlayAsToolWindow()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var form = new TestableDropOverlayForm();
                Assert.Equal(WsExToolWindow, form.ExtendedStyle & WsExToolWindow);
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (error is not null)
            ExceptionDispatchInfo.Capture(error).Throw();
    }

    private sealed class TestableDropOverlayForm : DropOverlayForm
    {
        public TestableDropOverlayForm()
            : base(IntPtr.Zero, Path.GetTempPath())
        {
        }

        public int ExtendedStyle => CreateParams.ExStyle;
    }
}
