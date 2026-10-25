using System.Diagnostics;
using System.Runtime.InteropServices;
using MarkdownViewer.Core.Contracts;
using Microsoft.Win32;
using Windows.ApplicationModel;
using Windows.System;

namespace MarkdownViewer.App.Services;

/// <summary>
/// Checks whether this package is the effective handler for ".md" via IApplicationAssociationRegistration,
/// and opens the Windows "Default apps" page (Windows only lets the user change defaults there).
/// </summary>
public sealed class DefaultAppService : IDefaultAppService
{
    private const int AT_FILEEXTENSION = 0;
    private const int AL_EFFECTIVE = 1;
    private static readonly Guid CLSID_ApplicationAssociationRegistration = new("591209c7-767b-42b2-9fba-44ee4615f2c7");

    public bool IsDefaultForMarkdown()
    {
        try
        {
            var familyName = Package.Current.Id.FamilyName;
            var type = Type.GetTypeFromCLSID(CLSID_ApplicationAssociationRegistration, throwOnError: true)!;
            var registration = (IApplicationAssociationRegistration)Activator.CreateInstance(type)!;
            try
            {
                var hr = registration.QueryCurrentDefault(".md", AT_FILEEXTENSION, AL_EFFECTIVE, out var buffer);
                if (hr < 0 || buffer == IntPtr.Zero)
                {
                    return false;
                }

                string progId;
                try
                {
                    progId = Marshal.PtrToStringUni(buffer) ?? string.Empty;
                }
                finally
                {
                    Marshal.FreeCoTaskMem(buffer);
                }

                return BelongsToPackage(progId, familyName);
            }
            finally
            {
                Marshal.ReleaseComObject(registration);
            }
        }
        catch (Exception ex)
        {
            // Unpackaged runs (Package.Current throws), COM failures, registry access — all mean "unknown", i.e. false.
            Debug.WriteLine($"Default app query failed: {ex.Message}");
            return false;
        }
    }

    public async Task OpenDefaultAppsSettingsAsync()
    {
        var aumid = TryGetAppUserModelId();
        if (aumid is not null)
        {
            var specific = new Uri("ms-settings:defaultapps?registeredAUMID=" + Uri.EscapeDataString(aumid));
            try
            {
                if (await Launcher.LaunchUriAsync(specific))
                {
                    return;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Launching {specific} failed: {ex.Message}");
            }
        }

        await Launcher.LaunchUriAsync(new Uri("ms-settings:defaultapps"));
    }

    private static bool BelongsToPackage(string progId, string familyName)
    {
        if (string.IsNullOrEmpty(progId))
        {
            return false;
        }

        if (progId.StartsWith(familyName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Packaged apps register a hashed ProgID ("AppX...") whose Application key names the AUMID.
        using var key = Registry.ClassesRoot.OpenSubKey(progId + @"\Application");
        var aumid = key?.GetValue("AppUserModelID") as string;
        return aumid is not null && aumid.StartsWith(familyName + "!", StringComparison.OrdinalIgnoreCase);
    }

    private static string? TryGetAppUserModelId()
    {
        try
        {
            return AppInfo.Current.AppUserModelId;
        }
        catch (Exception)
        {
            // AppInfo.Current needs Windows 10 2004+; fall back to the manifest's conventional Application Id.
        }

        try
        {
            return Package.Current.Id.FamilyName + "!App";
        }
        catch (Exception)
        {
            return null;
        }
    }

    [ComImport]
    [Guid("4e530b0a-e611-4c77-a3ac-9031d022281b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IApplicationAssociationRegistration
    {
        // First vtable slot after IUnknown; later methods are not needed and therefore not declared.
        [PreserveSig]
        int QueryCurrentDefault(
            [MarshalAs(UnmanagedType.LPWStr)] string pszQuery,
            int atQueryType,
            int alQueryLevel,
            out IntPtr ppszAssociation);
    }
}
