
namespace RHI.Linux.Core;

public sealed partial class NeuralRenderingSetup
{
    private static void InstallCostScaler(string dir, string staged)
    {
        var proxy = At(dir, NrFiles.CostProxy); var real = At(dir, NrFiles.CostReal);
        if (File.Exists(proxy) && !File.Exists(real)) File.Move(proxy, real);
        Sentinel.Copy(Path.Combine(staged, NrFiles.CostProxy), proxy);
    }

    private static void UninstallCostScaler(string dir, bool recorded)
    {
        var proxy = At(dir, NrFiles.CostProxy); var real = At(dir, NrFiles.CostReal);
        if (File.Exists(real)) File.Move(real, proxy, true);
        else if (recorded && File.Exists(proxy) && !Sentinel.Placed(proxy)) File.Delete(proxy);
    }

}
