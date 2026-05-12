using NAudio.Wave;

namespace Planner.App.Services;

public static class VoiceMicrophoneService
{
    public static IReadOnlyList<VoiceMicrophoneDevice> GetDevices()
    {
        var devices = new List<VoiceMicrophoneDevice>();
        for (var i = 0; i < WaveIn.DeviceCount; i++)
        {
            var caps = WaveIn.GetCapabilities(i);
            devices.Add(new VoiceMicrophoneDevice(i, caps.ProductName));
        }

        return devices;
    }

    public static int ResolveDeviceNumber(int configuredDeviceNumber)
    {
        var devices = GetDevices();
        if (devices.Count == 0)
            return -1;
        if (devices.Any(x => x.DeviceNumber == configuredDeviceNumber))
            return configuredDeviceNumber;
        return devices[0].DeviceNumber;
    }
}

public sealed record VoiceMicrophoneDevice(int DeviceNumber, string Name)
{
    public string DisplayName => DeviceNumber < 0 ? Name : $"{Name}  (#{DeviceNumber})";
}
