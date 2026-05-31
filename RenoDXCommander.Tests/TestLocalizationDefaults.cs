using System.Runtime.CompilerServices;
using RenoDXCommander.Services;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace RenoDXCommander.Tests;

internal static class TestLocalizationDefaults
{
    [ModuleInitializer]
    public static void Initialize()
    {
        LocalizationService.SetLanguagePreference(LocalizationService.EnglishLanguage);
    }
}
