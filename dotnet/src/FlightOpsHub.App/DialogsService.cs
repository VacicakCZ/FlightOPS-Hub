using System.IO;
using Microsoft.Win32;

namespace FlightOpsHub.App;

/// <summary>
/// Port of backend/api.py's dialogs_browse_folder/dialogs_browse_file,
/// backed by WPF's native Microsoft.Win32 dialogs instead of pywebview's
/// create_file_dialog wrapper.
/// </summary>
public static class DialogsService
{
    public static string? BrowseFolder(string? initialDir)
    {
        var dialog = new OpenFolderDialog();
        if (!string.IsNullOrEmpty(initialDir) && Directory.Exists(initialDir))
        {
            dialog.InitialDirectory = initialDir;
        }
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    public static string? BrowseFile(string? initialDir, IReadOnlyList<string>? fileTypes)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = BuildFilter(fileTypes is { Count: > 0 } ? fileTypes : new[] { "Executables (*.exe)" }),
        };
        if (!string.IsNullOrEmpty(initialDir) && Directory.Exists(initialDir))
        {
            dialog.InitialDirectory = initialDir;
        }
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    /// <summary>Backs export_config/export_diagnostics's Save file dialogs.</summary>
    public static string? SaveFile(string defaultFilename, string fileType)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = defaultFilename,
            Filter = BuildFilter(new[] { fileType }),
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    /// <summary>
    /// pywebview's file_types entries read like "Executables (*.exe)" -
    /// WPF's OpenFileDialog.Filter wants "Executables (*.exe)|*.exe".
    /// </summary>
    private static string BuildFilter(IReadOnlyList<string> fileTypes)
    {
        var parts = fileTypes.Select(entry =>
        {
            var openParen = entry.LastIndexOf('(');
            var closeParen = entry.LastIndexOf(')');
            if (openParen < 0 || closeParen <= openParen)
            {
                return $"{entry}|*.*";
            }
            var pattern = entry[(openParen + 1)..closeParen];
            return $"{entry}|{pattern}";
        });
        return string.Join("|", parts);
    }
}
