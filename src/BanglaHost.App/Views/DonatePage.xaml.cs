using System;
using Windows.ApplicationModel.DataTransfer;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace BanglaHost.App.Views;

public sealed partial class DonatePage : Page
{
    public DonatePage()
    {
        this.InitializeComponent();
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        var package = new DataPackage();
        package.SetText("01959678229");
        Clipboard.SetContent(package);
        
        if (sender is Button btn)
        {
            var old = btn.Content;
            btn.Content = "Copied!";
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            timer.Tick += (s, args) => { btn.Content = old; timer.Stop(); };
            timer.Start();
        }
    }
}
