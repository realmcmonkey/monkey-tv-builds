using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MonkeyTV;

public partial class ArtworkPickerWindow : Window
{
    public string? SelectedReference { get; private set; }
    public string? SelectedFilePath { get; private set; }

    public ArtworkPickerWindow()
    {
        InitializeComponent();
        RenderArtwork();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => RenderArtwork();

    private void RenderArtwork()
    {
        if (ArtworkGroups is null) return;
        ArtworkGroups.Children.Clear();
        var query = SearchBox?.Text.Trim() ?? "";
        var matches = BuiltInArtworkCatalog.All.Where(item => string.IsNullOrEmpty(query) ||
            item.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
            item.Category.Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToList();

        foreach (var category in matches.GroupBy(item => item.Category))
        {
            ArtworkGroups.Children.Add(new TextBlock
            {
                Text = category.Key,
                FontSize = 17,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(2, 8, 0, 10)
            });
            var panel = new WrapPanel { Margin = new Thickness(0, 0, 0, 16) };
            foreach (var item in category)
            {
                var button = new Button
                {
                    Width = 190,
                    Margin = new Thickness(0, 0, 14, 14),
                    Padding = new Thickness(7),
                    Tag = item,
                    ToolTip = $"Use {item.Name} artwork"
                };
                var stack = new StackPanel();
                stack.Children.Add(new Border
                {
                    Width = 174,
                    Height = 98,
                    CornerRadius = new CornerRadius(9),
                    ClipToBounds = true,
                    Background = Brushes.Black,
                    Child = new Image { Source = BuiltInArtworkCatalog.TryLoad(item.Reference), Stretch = Stretch.UniformToFill }
                });
                stack.Children.Add(new TextBlock
                {
                    Text = item.Name,
                    Margin = new Thickness(2, 8, 2, 1),
                    FontWeight = FontWeights.SemiBold,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
                button.Content = stack;
                button.Click += SelectArtwork_Click;
                panel.Children.Add(button);
            }
            ArtworkGroups.Children.Add(panel);
        }
    }

    private void SelectArtwork_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: BuiltInArtwork item }) return;
        SelectedReference = item.Reference;
        DialogResult = true;
    }

    private void ChooseFile_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg;*.webp;*.bmp|All files|*.*" };
        if (picker.ShowDialog(this) != true) return;
        SelectedFilePath = picker.FileName;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
}
