using System.Threading.Tasks;

namespace BanglaHost.App.Services;

/// <summary>Native folder picker for an unpackaged WinUI app (needs the window handle).</summary>
public static class Picker
{
    public static async Task<string?> FolderAsync()
    {
        if (BanglaHost.App.App.Window is null) return null;
        var picker = new Windows.Storage.Pickers.FolderPicker();
        picker.FileTypeFilter.Add("*");
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(BanglaHost.App.App.Window);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }
}
