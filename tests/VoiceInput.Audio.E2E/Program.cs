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

var peak = audio.Samples.Max(sample => Math.Abs(sample));
var rms = Math.Sqrt(audio.Samples.Average(sample => sample * sample));
Console.WriteLine(
    $"AUDIO_E2E_PASS sample_rate={audio.SampleRate} samples={audio.Samples.Length} duration_ms={audio.Duration.TotalMilliseconds:0} peak={peak:F4} rms={rms:F4} level_events={levels.Count} meter_peak={levels.Max():F3}");
return 0;
