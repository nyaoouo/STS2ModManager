using System.Drawing;
using System.Windows.Forms;
using MaterialSkin.Controls;
using STS2ModManager.Widgets;

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

    public static LinkButton MakeButton()
        => new LinkButton
        {
            AutoSize = true,
            // Anchor = None centers the button vertically inside a FlowLayoutPanel row,
            // so it lines up with taller siblings such as MaterialTextBox2.
            Anchor = AnchorStyles.None,
            Margin = new Padding(0, 0, 6, 0),
        };
}
