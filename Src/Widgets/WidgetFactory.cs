using System.Drawing;
using System.Windows.Forms;
using MaterialSkin.Controls;

internal static class WidgetFactory
{
    public static Panel CreateSectionHeader(string label, Font baseFont, int cardSpacing)
    {
        var header = new Panel
        {
            Height = 26,
            Margin = new Padding(cardSpacing, cardSpacing + 2, cardSpacing, 2),
        };
        var headerLabel = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            Font = new Font(baseFont, FontStyle.Bold),
            ForeColor = SystemColors.GrayText,
            Text = label,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(2, 0, 0, 0),
        };
        header.Controls.Add(headerLabel);
        return header;
    }

    public static MaterialButton MakeButton(MaterialButton.MaterialButtonType type)
        => new MaterialButton
        {
            AutoSize = true,
            Type = type,
            UseAccentColor = false,
            HighEmphasis = type == MaterialButton.MaterialButtonType.Contained,
            Margin = new Padding(0, 0, 6, 0),
        };
}
