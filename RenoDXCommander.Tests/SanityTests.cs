using Xunit;

namespace RenoDXCommander.Tests;

public class SanityTests
{
    [Fact]
    public void ProjectSetup_ShouldCompileAndRun()
    {
        // Placeholder test to verify the test project is correctly configured
        Assert.True(true);
    }

    [FsCheck.Xunit.Property]
    public bool FsCheck_ShouldBeAvailable(int x)
    {
        // Placeholder property test to verify FsCheck integration works
        return x + 0 == x;
    }
}
