using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace WDE.SourceCodeIntegrationEditor.Views
{
    public class CommandsDocumentView : UserControl
    {
        public CommandsDocumentView()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}