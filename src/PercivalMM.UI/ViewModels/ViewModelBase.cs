using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PercivalMM.UI.ViewModels
{
    // A pristine, native base structure that doesn't rely on bloated external toolkits
    public class ViewModelBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void RaisePropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
