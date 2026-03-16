using System.Collections.Generic;
using System.Windows;

namespace WebViewHub.Controls
{
    public partial class ClearCacheDialog : Window
    {
        public List<string> SelectedOptions { get; private set; } = new();

        public ClearCacheDialog()
        {
            InitializeComponent();
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            SelectedOptions.Clear();

            if (ChkCache.IsChecked == true)
                SelectedOptions.Add("Cache");
            if (ChkCookie.IsChecked == true)
                SelectedOptions.Add("Cookie");
            if (ChkHistory.IsChecked == true)
                SelectedOptions.Add("History");
            if (ChkFormData.IsChecked == true)
                SelectedOptions.Add("FormData");

            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
