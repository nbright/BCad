// copyright

using Avalonia;
using Avalonia.Markup.Xaml;

namespace BCad.Avalonia
{
    public class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
