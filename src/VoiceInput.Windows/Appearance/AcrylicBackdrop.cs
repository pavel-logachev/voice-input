using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Windows;
using Microsoft.Win32;

namespace VoiceInput.Windows.Appearance;

public static class AcrylicBackdrop
{
    private const string DisableAcrylicEnvironmentVariable = "VOICE_INPUT_DISABLE_ACRYLIC";
    private const string PersonalizeRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string TransparencyRegistryValue = "EnableTransparency";
    private const int WindowCompositionAttributeAccentPolicy = 19;
    private const int AccentEnableAcrylicBlurBehind = 4;
    private const int AccentDrawAllBorders = 2;
    private const uint AcrylicTintAbgr = 0xD91C1714;

    public static OverlayBackdropMode Apply(nint windowHandle)
    {
        var compositionEnabled = NativeMethods.DwmIsCompositionEnabled(out var enabled) >= 0 && enabled;
        var selected = OverlayBackdropPolicy.Select(
            Environment.OSVersion.Version,
            compositionEnabled,
            SystemParameters.HighContrast,
            IsTransparencyEnabled());

        return selected == OverlayBackdropMode.Acrylic && TryApply(windowHandle)
            ? OverlayBackdropMode.Acrylic
            : OverlayBackdropMode.TintOnly;
    }

    private static bool IsTransparencyEnabled()
    {
        if (string.Equals(
                Environment.GetEnvironmentVariable(DisableAcrylicEnvironmentVariable),
                "1",
                StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeRegistryPath);
            return key?.GetValue(TransparencyRegistryValue) is not int value || value != 0;
        }
        catch (Exception exception) when (exception is SecurityException or UnauthorizedAccessException or IOException)
        {
            return true;
        }
    }

    private static bool TryApply(nint windowHandle)
    {
        if (windowHandle == nint.Zero)
        {
            return false;
        }

        var policy = new AccentPolicy
        {
            State = AccentEnableAcrylicBlurBehind,
            Flags = AccentDrawAllBorders,
            GradientColor = AcrylicTintAbgr,
        };
        var policySize = Marshal.SizeOf<AccentPolicy>();
        var policyPointer = Marshal.AllocHGlobal(policySize);
        try
        {
            Marshal.StructureToPtr(policy, policyPointer, fDeleteOld: false);
            var data = new WindowCompositionAttributeData
            {
                Attribute = WindowCompositionAttributeAccentPolicy,
                Data = policyPointer,
                SizeOfData = policySize,
            };
            return NativeMethods.SetWindowCompositionAttribute(windowHandle, ref data) != 0;
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(policyPointer);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int State;
        public int Flags;
        public uint GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public nint Data;
        public int SizeOfData;
    }

    private static class NativeMethods
    {
        [DllImport("dwmapi.dll", PreserveSig = true)]
        public static extern int DwmIsCompositionEnabled([MarshalAs(UnmanagedType.Bool)] out bool enabled);

        [DllImport("user32.dll")]
        public static extern int SetWindowCompositionAttribute(
            nint windowHandle,
            ref WindowCompositionAttributeData data);
    }
}
