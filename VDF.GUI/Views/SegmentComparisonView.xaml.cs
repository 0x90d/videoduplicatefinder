using Avalonia.Controls;

namespace VDF.GUI.Views
{
    public partial class SegmentComparisonView : Window
    {
        public SegmentComparisonView()
        {
            InitializeComponent();
#if DEBUG
            // Optional: This line is common for enabling Avalonia DevTools for windows.
            // this.AttachDevTools();
#endif
        }
    }
}
