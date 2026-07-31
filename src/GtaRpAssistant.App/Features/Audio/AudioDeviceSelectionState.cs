using GtaRpAssistant.Infrastructure.Windows;

namespace GtaRpAssistant.App.Features;

public sealed class AudioDeviceSelectionState : ObservableObject
{
    private MicrophoneDeviceInfo? _microphone;
    private RenderDeviceInfo? _renderDevice;

    public MicrophoneDeviceInfo? Microphone { get => _microphone; set => Set(ref _microphone, value); }
    public RenderDeviceInfo? RenderDevice { get => _renderDevice; set => Set(ref _renderDevice, value); }
}
