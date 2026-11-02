using Microsoft.UI.Xaml;
using RHI.ManifestEditor.Json;
using RHI.ManifestEditor.Services;

namespace RHI.ManifestEditor.UI;

public interface IEditorHost
{
    JsonDoc Doc { get; }
    string ManifestPath { get; }
    IReadOnlyList<LibraryGame> LibraryGames { get; }
    XamlRoot XamlRoot { get; }

    /// <summary>Runs an edit now (toggles, dropdowns, buttons). Shows an error instead of throwing.</summary>
    void Apply(Action edit);

    /// <summary>Queues a typed edit; it's applied after a short pause, or before save/undo/navigation.</summary>
    void Defer(string id, Action edit);

    /// <summary>Applies queued typed edits now.</summary>
    void Flush();

    void OpenGame(string name);

    /// <summary>Shows a short message in the status bar.</summary>
    void SetStatus(string text);

    Task<bool> Confirm(string title, string text, string yes, bool danger = false);
    Task<string?> Prompt(string title, string text, string initial, string yes = "OK");
}
