using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace TileKeeper
{
    /// <summary>Shared credit page for the System tab and the tray menu.</summary>
    public sealed class AboutForm : Form
    {
        public AboutForm()
        {
            Text = "About TileKeeper";
            Font = new Font("Segoe UI", 9F);
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(560, 370);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;

            var content = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            var credits = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 2,
                Padding = new Padding(20)
            };
            credits.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100F));
            credits.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            var title = CreateLabel("lblTitle", "TileKeeper");
            title.Font = new Font(Font.FontFamily, 16F, FontStyle.Bold);
            title.Margin = new Padding(0, 0, 0, 16);
            credits.Controls.Add(title, 0, 0);
            credits.SetColumnSpan(title, 2);

            AddRow(credits, 1, "Author", CreateLabel("lblAuthor", "Muhammad Nashiruddin Al-Asqalani"));
            AddRow(credits, 2, "Email", CreateLink("lnkEmail", "madinalani@gmail.com", "mailto:madinalani@gmail.com"));
            AddRow(credits, 3, "LinkedIn", CreateLink("lnkLinkedIn", "https://linkedin.com/in/madinalani", "https://linkedin.com/in/madinalani"));
            AddRow(credits, 4, "GitHub", CreateLink("lnkGitHub", "github.com/ASQAL90", "https://github.com/ASQAL90"));
            AddRow(credits, 5, "Co-author", CreateLabel("lblCoAuthor", "dibantu beberapa mas-mas dan mba-mba"));

            var thanksTitle = CreateLabel("lblThanksTitle", "Ucapan terima kasih");
            thanksTitle.Margin = new Padding(0, 12, 0, 8);
            credits.Controls.Add(thanksTitle, 0, 6);
            credits.SetColumnSpan(thanksTitle, 2);
            var thanks = CreateLabel("lblThanks", "terimakasih pada mas claude, si acar besar, mbak luna & mbak sol yang mengembangkan fitur aplikasi ke tingkat lebih tinggi.");
            credits.Controls.Add(thanks, 0, 7);
            credits.SetColumnSpan(thanks, 2);
            content.Controls.Add(credits);

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 52,
                Padding = new Padding(12),
                FlowDirection = FlowDirection.RightToLeft
            };
            var close = new Button
            {
                Name = "btnClose",
                Text = "Tutup",
                AutoSize = true,
                DialogResult = DialogResult.Cancel
            };
            close.Click += delegate { Close(); };
            buttons.Controls.Add(close);
            Controls.Add(content);
            Controls.Add(buttons);
            AcceptButton = close;
            CancelButton = close;
        }

        private static Label CreateLabel(string name, string text)
        {
            return new Label
            {
                Name = name,
                Text = text,
                AutoSize = true,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 0, 10)
            };
        }

        private static void AddRow(TableLayoutPanel credits, int row, string heading, Control value)
        {
            credits.Controls.Add(CreateLabel("lbl" + heading + "Heading", heading + " :"), 0, row);
            credits.Controls.Add(value, 1, row);
        }

        private LinkLabel CreateLink(string name, string text, string destination)
        {
            var link = new LinkLabel
            {
                Name = name,
                Text = text,
                AutoSize = true,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 0, 10)
            };
            link.Links.Add(0, text.Length, destination);
            link.LinkClicked += CreditLink_LinkClicked;
            return link;
        }

        private void CreditLink_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo((string)e.Link.LinkData) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Tautan tidak dapat dibuka.\n\n" + ex.Message,
                    "About TileKeeper", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
