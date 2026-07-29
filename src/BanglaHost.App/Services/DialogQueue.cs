using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Controls;

namespace BanglaHost.App.Services;

/// <summary>
/// Global dialog queue — WinUI 3 allows only one ContentDialog at a time.
/// Concurrent .ShowAsync() calls throw COMException and crash the app.
/// All dialogs must go through this queue.
/// </summary>
public static class DialogQueue
{
    private static readonly SemaphoreSlim _gate = new(1, 1);
    private static WeakReference<ContentDialog>? _currentDialog;

    /// <summary>Show a ContentDialog safely, queuing behind any already-open dialog.</summary>
    public static async Task<ContentDialogResult> ShowAsync(ContentDialog dialog)
    {
        await _gate.WaitAsync();
        try
        {
            if (_currentDialog != null && _currentDialog.TryGetTarget(out var current) && current == dialog)
            {
                // The exact same dialog is already open (re-entrancy).
                return ContentDialogResult.None;
            }

            _currentDialog = new WeakReference<ContentDialog>(dialog);
            return await dialog.ShowAsync();
        }
        catch (Exception) when (dialog.XamlRoot == null)
        {
            // Page navigated away while queued — swallow safely.
            return ContentDialogResult.None;
        }
        finally
        {
            _currentDialog = null;
            _gate.Release();
        }
    }
}
