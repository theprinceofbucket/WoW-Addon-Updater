using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WowUpdater {
    sealed class DarkDialog : Form {
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr window, string app, string ids);
        readonly DialogResult closeResult;
        internal DarkDialog(string text, string caption, MessageBoxButtons kind, MessageBoxIcon icon, MessageBoxDefaultButton defaultButton) {
            Text = caption;
            Font = new Font("Segoe UI", 10);
            BackColor = Color.FromArgb(32, 32, 32);
            ForeColor = Color.FromArgb(235, 235, 235);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.Dpi;
            int bodyHeight = Math.Min(340, Math.Max(66, TextRenderer.MeasureText(text, Font, new Size(500, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl).Height + 22));
            ClientSize = new Size(610, bodyHeight + 102);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 2, RowCount = 2 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            Controls.Add(layout);
            bool warning = icon == MessageBoxIcon.Warning || icon == MessageBoxIcon.Error;
            var marker = new Label {
                Text = icon == MessageBoxIcon.None ? "" : icon == MessageBoxIcon.Question ? "?" : warning ? "!" : "i",
                ForeColor = warning ? Color.FromArgb(240, 185, 90) : Color.FromArgb(105, 175, 240),
                Font = new Font("Segoe UI", 20, FontStyle.Bold), Dock = DockStyle.Fill, TextAlign = ContentAlignment.TopCenter
            };
            layout.Controls.Add(marker, 0, 0);
            var body = new TextBox { Text = text, Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None, WordWrap = true,
                BackColor = BackColor, ForeColor = ForeColor, Font = Font, Dock = DockStyle.Fill, ScrollBars = bodyHeight >= 340 ? ScrollBars.Vertical : ScrollBars.None,
                TabStop = false, AccessibleName = caption };
            body.HandleCreated += delegate { try { SetWindowTheme(body.Handle, "DarkMode_Explorer", null); } catch { } };
            layout.Controls.Add(body, 1, 0);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Padding = new Padding(0, 10, 0, 0) };
            layout.Controls.Add(actions, 0, 1); layout.SetColumnSpan(actions, 2);
            DialogResult[] results;
            switch (kind) {
                case MessageBoxButtons.OK: results = new[] { DialogResult.OK }; break;
                case MessageBoxButtons.OKCancel: results = new[] { DialogResult.OK, DialogResult.Cancel }; break;
                case MessageBoxButtons.YesNo: results = new[] { DialogResult.Yes, DialogResult.No }; break;
                case MessageBoxButtons.YesNoCancel: results = new[] { DialogResult.Yes, DialogResult.No, DialogResult.Cancel }; break;
                case MessageBoxButtons.RetryCancel: results = new[] { DialogResult.Retry, DialogResult.Cancel }; break;
                case MessageBoxButtons.AbortRetryIgnore: results = new[] { DialogResult.Abort, DialogResult.Retry, DialogResult.Ignore }; break;
                default: throw new ArgumentOutOfRangeException("kind");
            }
            int index = Math.Min(results.Length - 1, defaultButton == MessageBoxDefaultButton.Button3 ? 2 : defaultButton == MessageBoxDefaultButton.Button2 ? 1 : 0);
            closeResult = results.Contains(DialogResult.Cancel) ? DialogResult.Cancel : results.Contains(DialogResult.No) ? DialogResult.No : results[0];
            for (int i = results.Length - 1; i >= 0; i--) {
                var button = new Button { Text = ButtonText(results[i]), DialogResult = results[i], Width = 108, Height = 34,
                    FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = false, ForeColor = ForeColor,
                    BackColor = i == index ? Color.FromArgb(40, 95, 165) : Color.FromArgb(48, 48, 48), Margin = new Padding(8, 0, 0, 0) };
                button.FlatAppearance.BorderColor = Color.FromArgb(75, 75, 75);
                button.FlatAppearance.MouseOverBackColor = i == index ? Color.FromArgb(50, 110, 185) : Color.FromArgb(60, 60, 60);
                button.FlatAppearance.MouseDownBackColor = Color.FromArgb(70, 70, 70);
                actions.Controls.Add(button);
                if (i == index) AcceptButton = button;
                if (results[i] == closeResult) CancelButton = button;
            }
            Shown += delegate { ((Control)AcceptButton).Focus(); };
            FormClosing += delegate { if (DialogResult == DialogResult.None || DialogResult == DialogResult.Cancel) DialogResult = closeResult; };
        }
        static string ButtonText(DialogResult result) {
            switch (result) {
                case DialogResult.Yes: return L.English ? "Yes" : "Ja";
                case DialogResult.No: return L.English ? "No" : "Nein";
                case DialogResult.Cancel: return L.English ? "Cancel" : "Abbrechen";
                case DialogResult.Retry: return L.English ? "Retry" : "Wiederholen";
                case DialogResult.Abort: return L.English ? "Abort" : "Abbrechen";
                case DialogResult.Ignore: return L.English ? "Ignore" : "Ignorieren";
                default: return "OK";
            }
        }
        protected override void OnHandleCreated(EventArgs e) {
            base.OnHandleCreated(e);
            try { int dark = 1; if (DwmSetWindowAttribute(Handle, 20, ref dark, 4) != 0) DwmSetWindowAttribute(Handle, 19, ref dark, 4); } catch { }
        }
        public static DialogResult ShowMessage(IWin32Window owner, string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton defaultButton) {
            using (var dialog = new DarkDialog(text, caption, buttons, icon, defaultButton)) {
                if (owner == null) { dialog.StartPosition = FormStartPosition.CenterScreen; return dialog.ShowDialog(); }
                return dialog.ShowDialog(owner);
            }
        }
        internal static void Test(Form owner, string imagePath) {
            bool original = L.English;
            try {
                foreach (bool english in new[] { false, true }) {
                    L.English = english;
                    using (var dialog = new DarkDialog(L.T("Noch kein öffentliches Release verfügbar (oder Repository nicht erreichbar)."), L.T("Updater-Update"), MessageBoxButtons.OK, MessageBoxIcon.Information, MessageBoxDefaultButton.Button1)) {
                        dialog.Show(owner); Application.DoEvents();
                        using (var bitmap = new Bitmap(dialog.Width, dialog.Height)) {
                            dialog.DrawToBitmap(bitmap, new Rectangle(0, 0, dialog.Width, dialog.Height));
                            bitmap.Save(Path.ChangeExtension(imagePath, english ? ".dialog-en.png" : ".dialog-de.png"));
                        }
                        dialog.ProcessDialogKey(Keys.Enter);
                        if (dialog.DialogResult != DialogResult.OK) throw new Exception("Dialog Enter did not return OK.");
                    }
                }
                using (var dialog = new DarkDialog("Test", "Test", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2)) {
                    dialog.Show(owner); Application.DoEvents();
                    if (((Button)dialog.AcceptButton).DialogResult != DialogResult.No) throw new Exception("Safe default was not preserved.");
                    dialog.ProcessDialogKey(Keys.Escape);
                    if (dialog.DialogResult != DialogResult.No) throw new Exception("Escape must decline.");
                }
                using (var dialog = new DarkDialog("Test", "Test", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1)) {
                    dialog.Show(owner); Application.DoEvents(); dialog.Close();
                    if (dialog.DialogResult != DialogResult.No) throw new Exception("Window close must decline.");
                }
            } finally { L.English = original; }
        }
    }
}
