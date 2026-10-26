using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace WowUpdater {
    static class L {
        public static bool English;
        static readonly Dictionary<string,string> words = new JavaScriptSerializer().Deserialize<Dictionary<string,string>>(UpdaterForm.Resource("english.json"));
        static readonly KeyValuePair<string,string>[] forward = words.OrderByDescending(p => p.Key.Length).ToArray();
        static readonly KeyValuePair<string,string>[] reverse = words.Where(p => p.Value.Length > 0).OrderByDescending(p => p.Value.Length).ToArray();
        static readonly Regex forwardPattern = new Regex(string.Join("|", forward.Select(p => Regex.Escape(p.Key))), RegexOptions.Compiled);
        static readonly Regex reversePattern = new Regex(string.Join("|", reverse.Select(p => Regex.Escape(p.Value))), RegexOptions.Compiled);
        static string SettingsPath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WoWAddonUpdater", "language.txt"); } }
        public static bool ReadPreference(string path) { try { return File.ReadAllText(path).Trim() == "en"; } catch { return false; } }
        public static void Load() { English = ReadPreference(SettingsPath); }
        public static void Save() { WritePreference(SettingsPath, English); }
        public static void WritePreference(string path, bool english) {
            LocalFiles.ValidateTree(path);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, english ? "en" : "de", Encoding.UTF8);
        }
        public static string T(string text) { return English ? Translate(text, true) : text; }
        public static string Translate(string text, bool english) {
            if (string.IsNullOrEmpty(text)) return text;
            // One-pass alternatives prevent replacements from translating their own output.
            return (english ? forwardPattern : reversePattern).Replace(text, m => {
                if (english) return words[m.Value];
                return reverse.First(p => p.Value == m.Value).Key;
            });
        }
        public static DialogResult Show(string text, string caption = "WoW Addon Updater", MessageBoxButtons buttons = MessageBoxButtons.OK, MessageBoxIcon icon = MessageBoxIcon.None) {
            return DarkDialog.ShowMessage(null, T(text), T(caption), buttons, icon, MessageBoxDefaultButton.Button1);
        }
        public static DialogResult Show(IWin32Window owner, string text, string caption = "WoW Addon Updater", MessageBoxButtons buttons = MessageBoxButtons.OK, MessageBoxIcon icon = MessageBoxIcon.None, MessageBoxDefaultButton defaultButton = MessageBoxDefaultButton.Button1) {
            return DarkDialog.ShowMessage(owner, T(text), T(caption), buttons, icon, defaultButton);
        }
    }
    public partial class UpdaterForm {
        readonly Dictionary<Control,string> captions = new Dictionary<Control,string>();
        readonly Dictionary<ListViewItem.ListViewSubItem,string> cells = new Dictionary<ListViewItem.ListViewSubItem,string>();
        readonly Dictionary<ColumnHeader,string> headings = new Dictionary<ColumnHeader,string>();
        readonly Dictionary<Addon,string> tooltips = new Dictionary<Addon,string>();
        bool translating;
        ToolStripMenuItem languageMenu, germanMenu, englishMenu, updateMenu;
        ContextMenuStrip options;
        void TrackText(Control control) {
            if (control == log) return;
            captions[control] = control is Button && control.Tag is string ? (string)control.Tag : control.Text;
            control.TextChanged += delegate {
                if (translating) return;
                captions[control] = control.Text;
                translating = true;
                try { control.Text = L.T(control.Text); } finally { translating = false; }
            };
            foreach (Control child in control.Controls) TrackText(child);
        }
        void TranslateSurface() {
            translating = true;
            try {
                foreach (var pair in captions) if (!pair.Key.IsDisposed) pair.Key.Text = L.T(pair.Value);
                foreach (ColumnHeader col in list.Columns) {
                    if (!headings.ContainsKey(col)) headings[col] = col.Text;
                    col.Text = L.T(headings[col]);
                }
                foreach (Addon addon in addons) foreach (ListViewItem.ListViewSubItem cell in addon.Row.SubItems) {
                    string original;
                    if (!cells.TryGetValue(cell, out original) || (cell.Text != original && cell.Text != L.Translate(original, true)))
                        cells[cell] = cell.Text;
                    cell.Text = L.T(cells[cell]);
                }
                if (languageMenu != null) {
                    languageMenu.Text = L.T("Sprache");
                    updateMenu.Text = L.T("Nach Updates suchen");
                    germanMenu.Checked = !L.English;
                    englishMenu.Checked = L.English;
                }
                foreach (var pair in tooltips) pair.Key.Row.ToolTipText = L.T(pair.Value);
                list.Invalidate();
            } finally { translating = false; }
        }
        void ChangeLanguage(bool english, bool save) {
            if (busy || selfUpdating) return;
            bool old = L.English;
            L.English = english;
            try { if (save) L.Save(); }
            catch (Exception ex) { L.English = old; L.Show(this, ex.Message, "Sprache konnte nicht gespeichert werden"); return; }
            TranslateSurface();
            log.Text = L.T(fullLog.ToString());
            log.SelectionStart = log.TextLength;
            log.ScrollToCaret();
        }
        void BuildOptions() {
            options = new ContextMenuStrip { BackColor = surface, ForeColor = textColor, Font = Font };
            languageMenu = new ToolStripMenuItem("Sprache");
            germanMenu = new ToolStripMenuItem("Deutsch", null, delegate { ChangeLanguage(false, true); });
            englishMenu = new ToolStripMenuItem("English", null, delegate { ChangeLanguage(true, true); });
            languageMenu.DropDownItems.AddRange(new ToolStripItem[] { germanMenu, englishMenu });
            updateMenu = new ToolStripMenuItem("Nach Updates suchen", null, delegate { CheckSelfUpdate(); });
            options.Items.AddRange(new ToolStripItem[] { languageMenu, new ToolStripSeparator(), updateMenu });
            DarkMenuRenderer.Apply(options);
            options.Opening += delegate { languageMenu.Enabled = updateMenu.Enabled = !busy && !selfUpdating; };
            var layout = (TableLayoutPanel)Controls[0];
            var toolbar = new Panel { Dock = DockStyle.Top, Height = 36, Padding = new Padding(24, 2, 24, 2), BackColor = surface };
            var menu = new Button { Text = "⋯", AccessibleName = "Menü / Menu", Dock = DockStyle.Left, Width = 42, FlatStyle = FlatStyle.Flat, BackColor = surface, ForeColor = textColor, UseVisualStyleBackColor = false, Font = new System.Drawing.Font("Segoe UI", 18) };
            menu.FlatAppearance.BorderSize = 0;
            menu.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(46, 46, 46);
            menu.FlatAppearance.MouseDownBackColor = System.Drawing.Color.FromArgb(58, 58, 58);
            menu.Click += delegate { options.Show(menu, new System.Drawing.Point(0, menu.Height)); };
            toolbar.Controls.Add(menu);
            Controls.Add(toolbar);
            layout.BringToFront(); // Fill unterhalb der oben angedockten Menüleiste.
            TrackText(this);
            TranslateSurface();
            FormClosed += delegate { options.Dispose(); };
        }
        void TestLocalization(string imagePath) {
            DarkDialog.Test(this, imagePath);
            options.Show(this, new System.Drawing.Point(24, 72));
            languageMenu.Select();
            languageMenu.ShowDropDown();
            Application.DoEvents();
            TestMenuImage(options, Path.ChangeExtension(imagePath, ".menu.png"));
            TestMenuImage(languageMenu.DropDown, Path.ChangeExtension(imagePath, ".language-menu.png"));
            languageMenu.HideDropDown();
            updateMenu.Select();
            Application.DoEvents();
            TestMenuImage(options, Path.ChangeExtension(imagePath, ".menu-after-hover.png"));
            if (options.Items.Count != 3 || updateMenu.HasDropDownItems || updateMenu.Text != "Nach Updates suchen")
                throw new Exception("Update action must be directly in the menu.");
            var layout = (TableLayoutPanel)Controls[0];
            var toolbar = Controls.OfType<Panel>().First(p => p.Dock == DockStyle.Top);
            if (toolbar.Bottom > layout.Top || toolbar.Controls[0].Left > 30)
                throw new Exception("Menu toolbar must be above the title at the left.");
            options.Close();
            string preference = Path.Combine(tempRoot, "test-language.txt");
            L.WritePreference(preference, true);
            if (!L.ReadPreference(preference)) throw new Exception("English preference was not saved.");
            L.WritePreference(preference, false);
            if (L.ReadPreference(preference)) throw new Exception("German preference was not saved.");
            ChangeLanguage(true, false);
            if (check.Text != "Find addons / check updates" || play.Text != "Start WoW" || list.Columns[3].Text != "Installed")
                throw new Exception("English controls are incomplete.");
            status.Text = "Prüfe: TomTom";
            if (status.Text != "Checking: TomTom") throw new Exception("Dynamic status translation failed.");
            if (L.T("INFO | TomTom ist bereits aktuell") != "INFO | TomTom is already up to date") throw new Exception("Log translation failed.");
            using (var bitmap = new System.Drawing.Bitmap(Width, Height)) {
                DrawToBitmap(bitmap, new System.Drawing.Rectangle(0, 0, Width, Height));
                bitmap.Save(Path.ChangeExtension(imagePath, ".english.png"));
            }
            busy = true;
            ChangeLanguage(false, false);
            if (!L.English) throw new Exception("Language changed during an active operation.");
            busy = false;
            ChangeLanguage(false, false);
            if (check.Text != "Addons auslesen / prüfen" || moreTools.Controls[0].Text != "Backup wiederherstellen" || list.Columns[3].Text != "Installiert")
                throw new Exception("German language restoration failed.");
            bool originalEnglish = L.English;
            L.English = true;
            using (var englishStartup = new UpdaterForm(gameRoot)) {
                if (englishStartup.check.Text != "Find addons / check updates") throw new Exception("English startup failed.");
                englishStartup.ChangeLanguage(false, false);
                if (englishStartup.moreTools.Controls[0].Text != "Backup wiederherstellen") throw new Exception("English startup to German failed.");
                englishStartup.Close();
                // This form was never shown, so FormClosed is not guaranteed to run.
                if (Directory.Exists(englishStartup.tempRoot)) LocalFiles.DeleteOwned(englishStartup.tempRoot, Path.GetTempPath(), Path.GetFileName(englishStartup.tempRoot));
            }
            L.English = originalEnglish;
        }
        static void TestMenuImage(ToolStripDropDown menu, string file) {
            if (!(menu.Renderer is DarkMenuRenderer)) throw new Exception("Dark renderer missing from menu.");
            using (var bitmap = new System.Drawing.Bitmap(menu.Width, menu.Height)) {
                menu.DrawToBitmap(bitmap, new System.Drawing.Rectangle(0, 0, menu.Width, menu.Height));
                bitmap.Save(file);
                foreach (int x in new[] { 0, 2, menu.Width - 1 }) {
                    var color = bitmap.GetPixel(x, menu.Height / 2);
                    if (color.R > 100 || color.G > 100 || color.B > 100) throw new Exception("Light menu edge/background detected.");
                }
            }
        }
    }
}
