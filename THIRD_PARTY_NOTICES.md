# Third-party notices

## Shipped in the Voice Input 1.0 installer

| Component | Purpose | License | Source |
|---|---|---|---|
| .NET 10 runtime (self-contained) | Application runtime, WPF, Windows Forms | MIT | https://github.com/dotnet/runtime |
| NAudio 2.2.1 | Windows WASAPI capture and in-memory resampling | MIT | https://github.com/naudio/NAudio |

## External service

Speech recognition in the installed application is performed by the OpenAI audio transcription API with the user's own API key. OpenAI is a service, not a bundled component; its terms and data-handling policy apply to audio sent to it.

## Optional local engine (source builds only)

Source builds that include the `VoiceInput.Asr.Worker` project can use a local engine. Its files are downloaded on first use and are not part of the installer.

| Component | Purpose | License | Source |
|---|---|---|---|
| transcribe.cpp 0.1.3 | Native GGUF speech-recognition runtime | MIT | https://github.com/handy-computer/transcribe.cpp |
| ggml | CPU/Vulkan tensor backend distributed with transcribe.cpp | MIT | https://github.com/ggml-org/ggml |
| GigaAM-v3 E2E RNNT Q4_K_M | Russian ASR model | MIT | https://huggingface.co/handy-computer/gigaam-v3-e2e-rnnt-gguf |
| GigaAM-v3 | Original model and training code | MIT | https://github.com/salute-developers/GigaAM |

The downloaded transcribe.cpp runtime archive includes its own `licenses/` directory, which Voice Input preserves when extracting the runtime into `%LOCALAPPDATA%\VoiceInput`.

This notice is informational and does not replace the license text distributed by each upstream project.
