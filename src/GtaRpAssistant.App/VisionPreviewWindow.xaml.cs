using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace GtaRpAssistant.App;

public partial class VisionPreviewWindow : Window
{
    public VisionPreviewWindow(byte[] png, string endpointDescription)
    {
        InitializeComponent();
        ConsentCard.Destination = endpointDescription;
        using var stream = new MemoryStream(png, writable: false);
        var image = new BitmapImage();
        image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = stream; image.EndInit(); image.Freeze();
        ConsentCard.PreviewSource = image;
    }

    private void ConsentCard_CancelRequested(object sender, EventArgs e) { DialogResult = false; Close(); }
    private void ConsentCard_ConfirmRequested(object sender, EventArgs e) { DialogResult = true; Close(); }
}
