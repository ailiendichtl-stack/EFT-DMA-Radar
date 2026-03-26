/*
 * Lone EFT DMA Radar
 * Brought to you by Lone (Lone DMA)
 * 
MIT License

Copyright (c) 2025 Lone DMA

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
 *
*/

using LoneEftDmaRadar.UI.Loot;
using LoneEftDmaRadar.UI.Radar.ViewModels;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace LoneEftDmaRadar.UI.Radar.Views
{
    /// <summary>
    /// Interaction logic for LootFiltersTab.xaml
    /// </summary>
    public partial class LootFiltersTab : UserControl
    {
        public LootFiltersViewModel ViewModel { get; }

        public LootFiltersTab()
        {
            InitializeComponent();
            DataContext = ViewModel = new LootFiltersViewModel(this);
        }

        private void EntriesGrid_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete)
            {
                if (EntriesGrid.SelectedItem is LootFilterEntry entry)
                {
                    ViewModel.DeleteEntry(entry);
                    e.Handled = true;
                }
            }
        }

        private void DeleteEntry_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (EntriesGrid.SelectedItem is LootFilterEntry entry)
            {
                ViewModel.DeleteEntry(entry);
            }
        }

        private void ColorSwatch_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is System.Windows.Controls.Border border && border.DataContext is LootFilterEntry entry)
            {
                // Parse current color for the dialog
                Color initial = Colors.White;
                var currentColor = entry.ExplicitColor ?? entry.Color;
                if (!string.IsNullOrEmpty(currentColor))
                {
                    try { initial = (Color)ColorConverter.ConvertFromString(currentColor); }
                    catch { }
                }

                // Open a modal window with the Xceed ColorCanvas
                var picker = new Xceed.Wpf.Toolkit.ColorPicker
                {
                    SelectedColor = initial,
                    ColorMode = Xceed.Wpf.Toolkit.ColorMode.ColorCanvas,
                    DisplayColorAndName = true,
                    ShowDropDownButton = false,
                    Width = 280
                };

                var win = new Window
                {
                    Title = "Pick Color",
                    SizeToContent = SizeToContent.WidthAndHeight,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = Window.GetWindow(this),
                    ResizeMode = ResizeMode.NoResize,
                    Content = new System.Windows.Controls.StackPanel
                    {
                        Margin = new Thickness(10),
                        Children =
                        {
                            picker,
                            new System.Windows.Controls.Button
                            {
                                Content = "OK",
                                Margin = new Thickness(0, 10, 0, 0),
                                Padding = new Thickness(20, 4, 20, 4),
                                HorizontalAlignment = HorizontalAlignment.Center,
                                IsDefault = true
                            }
                        }
                    }
                };

                // Wire up OK button
                if (((System.Windows.Controls.StackPanel)win.Content).Children[1] is System.Windows.Controls.Button okBtn)
                    okBtn.Click += (_, _) => { win.DialogResult = true; win.Close(); };

                if (win.ShowDialog() == true && picker.SelectedColor is Color picked)
                {
                    entry.ExplicitColor = picked.ToString();
                }

                e.Handled = true;
            }
        }
    }
}
