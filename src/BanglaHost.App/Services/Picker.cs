using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace BanglaHost.App.Services;

/// <summary>Native folder picker for an unpackaged WinUI app (needs the window handle).
/// Uses Win32 IFileOpenDialog directly instead of WinRT FolderPicker so it works even when
/// the app is running as Administrator (WinRT pickers silently fail when elevated).</summary>
public static class Picker
{
    public static Task<string?> FolderAsync()
    {
        if (BanglaHost.App.App.Window is null) return Task.FromResult<string?>(null);
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(BanglaHost.App.App.Window);
        
        try
        {
            var dialog = (IFileOpenDialog)new FileOpenDialog();
            dialog.SetOptions(FOS.FOS_PICKFOLDERS | FOS.FOS_FORCEFILESYSTEM);
            
            if (dialog.Show(hwnd) == 0) // S_OK
            {
                dialog.GetResult(out var item);
                item.GetDisplayName(SIGDN.SIGDN_FILESYSPATH, out var path);
                return Task.FromResult<string?>(path);
            }
        }
        catch { }
        return Task.FromResult<string?>(null);
    }

    [ComImport, Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
    private class FileOpenDialog { }

    [ComImport, Guid("d57c7288-d4ad-4768-be02-9d969532d960"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOpenDialog
    {
        [PreserveSig] int Show([In] IntPtr parent);
        void SetFileTypes([In] uint cFileTypes, [In] COMDLG_FILTERSPEC[] rgFilterSpec);
        void SetFileTypeIndex([In] uint iFileType);
        void GetFileTypeIndex(out uint piFileType);
        void Advise([In] IntPtr pfde, out uint pdwCookie);
        void Unadvise([In] uint dwCookie);
        void SetOptions([In] FOS fos);
        void GetOptions(out FOS pfos);
        void SetDefaultFolder([In] IShellItem psi);
        void SetFolder([In] IShellItem psi);
        void GetFolder(out IShellItem ppsi);
        void GetCurrentSelection(out IShellItem ppsi);
        void SetFileName([In, MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
        void SetTitle([In, MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
        void SetOkButtonLabel([In, MarshalAs(UnmanagedType.LPWStr)] string pszText);
        void SetFileNameLabel([In, MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
        void GetResult(out IShellItem ppsi);
        void AddPlace([In] IShellItem psi, int fdap);
        void SetDefaultExtension([In, MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
        void Close([MarshalAs(UnmanagedType.Error)] int hr);
        void SetClientGuid([In] ref Guid guid);
        void ClearClientData();
        void SetFilter([In] IntPtr pFilter);
        void GetResults([In] IntPtr ppenum);
        void GetSelectedItems([In] IntPtr ppenum);
    }

    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(IntPtr pbc, [In] ref Guid bhid, [In] ref Guid riid, out IntPtr ppv);
        void GetParent(out IShellItem ppsi);
        void GetDisplayName([In] SIGDN sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);
        void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
        void Compare(IShellItem psi, uint hint, out int piOrder);
    }

    private enum SIGDN : uint
    {
        SIGDN_FILESYSPATH = 0x80058000
    }

    public static Task<string?> SaveFileAsync(string defaultExtension, string filterName, string filterExt)
    {
        if (BanglaHost.App.App.Window is null) return Task.FromResult<string?>(null);
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(BanglaHost.App.App.Window);
        try
        {
            var dialog = (IFileSaveDialog)new FileSaveDialog();
            dialog.SetOptions(FOS.FOS_FORCEFILESYSTEM | FOS.FOS_OVERWRITEPROMPT);
            dialog.SetDefaultExtension(defaultExtension);
            
            var filters = new COMDLG_FILTERSPEC[] { new COMDLG_FILTERSPEC { pszName = filterName, pszSpec = filterExt } };
            dialog.SetFileTypes(1, filters);

            if (dialog.Show(hwnd) == 0)
            {
                dialog.GetResult(out var item);
                item.GetDisplayName(SIGDN.SIGDN_FILESYSPATH, out var path);
                return Task.FromResult<string?>(path);
            }
        }
        catch { }
        return Task.FromResult<string?>(null);
    }

    public static Task<string?> OpenFileAsync(string filterName, string filterExt)
    {
        if (BanglaHost.App.App.Window is null) return Task.FromResult<string?>(null);
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(BanglaHost.App.App.Window);
        try
        {
            var dialog = (IFileOpenDialog)new FileOpenDialog();
            dialog.SetOptions(FOS.FOS_FORCEFILESYSTEM | FOS.FOS_FILEMUSTEXIST);
            
            var filters = new COMDLG_FILTERSPEC[] { new COMDLG_FILTERSPEC { pszName = filterName, pszSpec = filterExt } };
            dialog.SetFileTypes(1, filters);

            if (dialog.Show(hwnd) == 0)
            {
                dialog.GetResult(out var item);
                item.GetDisplayName(SIGDN.SIGDN_FILESYSPATH, out var path);
                return Task.FromResult<string?>(path);
            }
        }
        catch { }
        return Task.FromResult<string?>(null);
    }

    [ComImport, Guid("C0B4E2F3-BA21-4773-8DBA-335EC946EB8B")]
    private class FileSaveDialog { }

    [ComImport, Guid("84bccd23-5fde-4cdb-aea4-af64b83d78ab"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileSaveDialog : IFileOpenDialog
    {
        void SetSaveAsItem([In] IShellItem psi);
        void SetProperties([In] IntPtr pStore);
        void SetCollectedProperties([In] IntPtr pList, [In] int fAppendDefault);
        void GetProperties(out IntPtr ppStore);
        void ApplyProperties([In] IShellItem psi, [In] IntPtr pStore, [In] ref IntPtr hwnd, [In] IntPtr pSink);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct COMDLG_FILTERSPEC
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string pszName;
        [MarshalAs(UnmanagedType.LPWStr)] public string pszSpec;
    }

    [Flags]
    private enum FOS : uint
    {
        FOS_OVERWRITEPROMPT = 0x00000002,
        FOS_FILEMUSTEXIST = 0x00001000,
        FOS_PICKFOLDERS = 0x00000020,
        FOS_FORCEFILESYSTEM = 0x00000040
    }
}
