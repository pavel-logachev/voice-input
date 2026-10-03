using VoiceInput.Windows.Audio;

using var recorder = new WasapiPushToTalkRecorder();
var levels = new List<float>();
recorder.RecordingLevelChanged += levels.Add;
await recorder.StartAsync(CancellationToken.None);
await Task.Delay(TimeSpan.FromSeconds(1.5));
var audio = await recorder.StopAsync(CancellationToken.None);

if (audio.SampleRate != 16_000 || audio.Samples.Length < 16_000 || levels.Count == 0)
{
    Console.Error.WriteLine(
        $"AUDIO_E2E_FAIL sample_rate={audio.SampleRate} samples={audio.Samples.Length} level_events={levels.Count}");
    return 1;
}

// A very short key press stops the recording while the device is still starting.
// Every such recording must end on its own instead of hanging until the timeout.
const int QuickTaps = 10;
var quickTapWatch = System.Diagnostics.Stopwatch.StartNew();
for (var tap = 0; tap < QuickTaps; tap++)
{
    await recorder.StartAsync(CancellationToken.None);
    using var tapTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
    try
    {
        await recorder.StopAsync(tapTimeout.Token);
    }
    catch (Exception exception) when (exception is OperationCanceledException or TimeoutException)
    {
        Console.Error.WriteLine($"AUDIO_E2E_FAIL quick_tap={tap} error={exception.GetType().Name}");
        return 1;
    }
}

quickTapWatch.Stop();

var peak = audio.Samples.Max(sample => Math.Abs(sample));
var rms = Math.Sqrt(audio.Samples.Average(sample => sample * sample));
Console.WriteLine(
    $"AUDIO_E2E_PASS sample_rate={audio.SampleRate} samples={audio.Samples.Length} duration_ms={audio.Duration.TotalMilliseconds:0} peak={peak:F4} rms={rms:F4} level_events={levels.Count} meter_peak={levels.Max():F3} quick_taps={QuickTaps} quick_taps_ms={quickTapWatch.ElapsedMilliseconds}");
return 0;
