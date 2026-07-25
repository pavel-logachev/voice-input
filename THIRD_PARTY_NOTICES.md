# Third-party notices

Voice Input downloads or depends on the following open-source components.

| Component | Purpose | License | Source |
|---|---|---|---|
| NAudio 2.2.1 | Windows WASAPI capture and in-memory resampling | MIT | https://github.com/naudio/NAudio |
| transcribe.cpp 0.1.3 | Native GGUF speech-recognition runtime | MIT | https://github.com/handy-computer/transcribe.cpp |
| ggml | CPU/Vulkan tensor backend distributed with transcribe.cpp | MIT | https://github.com/ggml-org/ggml |
| GigaAM-v3 E2E RNNT Q4_K_M | Russian ASR model | MIT | https://huggingface.co/handy-computer/gigaam-v3-e2e-rnnt-gguf |
| GigaAM-v3 | Original model and training code | MIT | https://github.com/salute-developers/GigaAM |

The downloaded transcribe.cpp runtime archive includes its own `licenses/` directory. Voice Input preserves that directory when extracting the runtime into `%LOCALAPPDATA%\VoiceInput`.

This notice is informational and does not replace the license text distributed by each upstream project.
