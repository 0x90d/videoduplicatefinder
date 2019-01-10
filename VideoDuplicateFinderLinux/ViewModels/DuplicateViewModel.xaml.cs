using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace VideoDuplicateFinderLinux
{
    public class DuplicateViewModel : UserControl
    {
        public DuplicateViewModel()
        {
            this.InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
