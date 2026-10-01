using NAudio.CoreAudioApi;

namespace VoiceInput.Windows.Audio;

/// <summary>An input device the user can pick in the settings. The id is the stable Windows endpoint id.</summary>
public sealed record MicrophoneChoice(string Id, string Name);

public static class AudioDeviceCatalog
{
    /// <summary>Active capture devices, in the order Windows reports them. Never throws: no devices means an empty list.</summary>
    public static IReadOnlyList<MicrophoneChoice> ListMicrophones()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var choices = new List<MicrophoneChoice>();
            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            {
                using (device)
                {
                    choices.Add(new MicrophoneChoice(device.ID, device.FriendlyName));
                }
            }

            return choices;
        }
        catch (Exception exception) when (exception is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            return [];
        }
    }

    /// <summary>Opens the chosen device, or the Windows default when none is chosen or the chosen one is gone.</summary>
    internal static MMDevice OpenCaptureDevice(string? deviceId, out bool usedFallback)
    {
        using var enumerator = new MMDeviceEnumerator();
        if (!string.IsNullOrWhiteSpace(deviceId))
        {
            try
            {
                var device = enumerator.GetDevice(deviceId);
                if (device.State == DeviceState.Active)
                {
                    usedFallback = false;
                    return device;
                }

                device.Dispose();
            }
            catch (Exception exception) when (exception is System.Runtime.InteropServices.COMException or ArgumentException)
            {
                // The saved microphone was unplugged or its id is stale: fall through to the default.
            }

            usedFallback = true;
        }
        else
        {
            usedFallback = false;
        }

        return enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
    }
}
