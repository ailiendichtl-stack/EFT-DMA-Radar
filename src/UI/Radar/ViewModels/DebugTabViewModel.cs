using LoneEftDmaRadar.UI.Misc;
using System.Windows.Input;

namespace LoneEftDmaRadar.UI.Radar.ViewModels
{
    public sealed class DebugTabViewModel
    {
        public DebugTabViewModel()
        {
            ToggleDebugConsoleCommand = new SimpleCommand(DebugLogger.Toggle);
        }

        public ICommand ToggleDebugConsoleCommand { get; }
    }
}
