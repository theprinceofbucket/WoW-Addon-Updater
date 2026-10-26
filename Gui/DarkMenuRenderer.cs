using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace WowUpdater {
    // Alle Menüflächen selbst zeichnen, auch Untermenüs und Hakenbereiche.
    sealed class DarkMenuRenderer : ToolStripProfessionalRenderer {
        static readonly Color Background = Color.FromArgb(36, 36, 36);
        static readonly Color Hover = Color.FromArgb(54, 54, 54);
        static readonly Color Foreground = Color.FromArgb(235, 235, 235);
        public DarkMenuRenderer() { RoundedEdges = false; }
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) { e.Graphics.Clear(Background); }
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) {
            using (var pen = new Pen(Color.FromArgb(65, 65, 65)))
                e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        }
        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) {
            using (var brush = new SolidBrush(Background)) e.Graphics.FillRectangle(brush, e.AffectedBounds);
        }
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e) {
            using (var brush = new SolidBrush(e.Item.Selected && e.Item.Enabled ? Hover : Background))
                e.Graphics.FillRectangle(brush, new Rectangle(2, 0, e.Item.Width - 4, e.Item.Height));
        }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e) {
            e.TextColor = e.Item.Enabled ? Foreground : Color.FromArgb(135, 135, 135);
            base.OnRenderItemText(e);
        }
        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e) {
            e.ArrowColor = e.Item.Enabled ? Foreground : Color.Gray;
            base.OnRenderArrow(e);
        }
        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e) {
            using (var pen = new Pen(Color.FromArgb(65, 65, 65)))
                e.Graphics.DrawLine(pen, 10, e.Item.Height / 2, e.Item.Width - 10, e.Item.Height / 2);
        }
        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e) {
            Rectangle r = e.ImageRectangle;
            var saved = e.Graphics.Save();
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(Foreground, 1.8f))
                e.Graphics.DrawLines(pen, new[] { new Point(r.Left + 3, r.Top + r.Height / 2), new Point(r.Left + r.Width / 2 - 1, r.Bottom - 4), new Point(r.Right - 2, r.Top + 3) });
            e.Graphics.Restore(saved);
        }
        public static void Apply(ToolStripDropDown menu) {
            menu.Renderer = new DarkMenuRenderer();
            menu.BackColor = Background;
            menu.ForeColor = Foreground;
            menu.DropShadowEnabled = false;
            menu.Padding = new Padding(4);
            foreach (ToolStripItem item in menu.Items) {
                item.ForeColor = Foreground;
                item.BackColor = Background;
                item.Padding = new Padding(6, 5, 6, 5);
                var entry = item as ToolStripMenuItem;
                if (entry != null && entry.HasDropDownItems) Apply(entry.DropDown);
            }
        }
    }
}
