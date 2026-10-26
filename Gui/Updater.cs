using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace WowUpdater {
    public class BufferedListView : ListView {
        [DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
        public BufferedListView() {
            // WinForms aktiviert damit auch die native ListView-Doppelpufferung.
            // Hintergrund und Zellen werden zusammen statt sichtbar nacheinander gezeichnet.
            DoubleBuffered = true;
        }
        void ApplyBackground() {
            if (!IsHandleCreated || IsDisposed) return;
            IntPtr color = new IntPtr(ColorTranslator.ToWin32(BackColor));
            SendMessage(Handle, 0x1001, IntPtr.Zero, color); // LVM_SETBKCOLOR
            SendMessage(Handle, 0x1026, IntPtr.Zero, color); // LVM_SETTEXTBKCOLOR
        }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); ApplyBackground(); }
        protected override void OnBackColorChanged(EventArgs e) { base.OnBackColorChanged(e); ApplyBackground(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); ApplyBackground(); Invalidate(); }
        void PaintEmptyArea(Graphics graphics) {
            if (View != View.Details || Items.Count == 0) return;
            int bottom = Items[Items.Count - 1].Bounds.Bottom;
            if (bottom < 0 || bottom >= ClientSize.Height) return;
            // Das native Theme zeichnet hier sonst Spaltentrennlinien weiter.
            using (var brush = new SolidBrush(BackColor))
                graphics.FillRectangle(brush, 0, bottom, ClientSize.Width, ClientSize.Height - bottom);
        }
        protected override void WndProc(ref Message message) {
            if (message.Msg == 0x14 && message.WParam != IntPtr.Zero) { // WM_ERASEBKGND
                using (var graphics = Graphics.FromHdc(message.WParam))
                using (var brush = new SolidBrush(BackColor)) graphics.FillRectangle(brush, ClientRectangle);
                message.Result = new IntPtr(1);
                return;
            }
            base.WndProc(ref message);
            if (message.Msg == 0xF) { // WM_PAINT
                using (var graphics = Graphics.FromHwnd(Handle)) PaintEmptyArea(graphics);
            } else if ((message.Msg == 0x318 || message.Msg == 0x317) && message.WParam != IntPtr.Zero) {
                using (var graphics = Graphics.FromHdc(message.WParam)) PaintEmptyArea(graphics);
            }
            if (message.Msg == 0x31A || message.Msg == 0x15) ApplyBackground(); // Theme-/Systemfarbenwechsel
        }
        internal bool HasExpectedBackground() {
            return SendMessage(Handle, 0x1000, IntPtr.Zero, IntPtr.Zero).ToInt32() == ColorTranslator.ToWin32(BackColor)
                && SendMessage(Handle, 0x1025, IntPtr.Zero, IntPtr.Zero).ToInt32() == ColorTranslator.ToWin32(BackColor);
        }
    }
    public class DarkProgress : Control {
        int maximum = 1;
        int value;
        public int Maximum { get { return maximum; } set { maximum = Math.Max(1, value); Invalidate(); } }
        public int Value { get { return value; } set { this.value = Math.Max(0, Math.Min(maximum, value)); Invalidate(); } }
        public DarkProgress() { DoubleBuffered = true; }
        protected override void OnPaint(PaintEventArgs e) {
            e.Graphics.Clear(Color.FromArgb(48, 48, 48));
            using (var brush = new SolidBrush(Color.FromArgb(70, 155, 230))) e.Graphics.FillRectangle(brush, 0, 0, Width * value / maximum, Height);
        }
    }
    public class Addon {
        public Dictionary<string, object> Source;
        public string Name;
        public string[] Folders;
        public bool Installed;
        public bool Verified;
        public bool NeedsUpdate;
        public bool Selected;
        public ListViewItem Row;
    }

    public partial class UpdaterForm : Form {
        readonly string gameRoot;
        readonly string tempRoot;
        readonly string enginePath;
        string sessionCache;
        readonly JavaScriptSerializer json = new JavaScriptSerializer();
        readonly List<Addon> addons = new List<Addon>();
        readonly object hosts;
        readonly ListView list = new BufferedListView();
        readonly TextBox log = new TextBox();
        readonly Button check = new Button();
        readonly Button install = new Button();
        readonly Button cancel = new Button();
        volatile bool cancelRequested;
        readonly Color surface = Color.FromArgb(32, 32, 32);
        readonly Color textColor = Color.FromArgb(235, 235, 235);
        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        static extern int SetWindowTheme(IntPtr window, string app, string ids);
        readonly Label status = new Label();
        readonly Label selection = new Label();
        readonly DarkProgress progress = new DarkProgress();
        bool busy;
        bool tableLayoutQueued;
        bool lastRunSucceeded;
        readonly Color neutralButton = Color.FromArgb(50, 50, 50);
        readonly Color updateButton = Color.FromArgb(30, 94, 180);
        readonly Color cancelButton = Color.FromArgb(180, 45, 45);
        readonly Color readyButton = Color.FromArgb(35, 125, 65);

        void RefreshButtonColors() {
            cancel.BackColor = cancel.Enabled ? cancelButton : neutralButton;
            bool pending = addons.Any(a => a.NeedsUpdate || (a.Selected && !a.Verified));
            install.BackColor = !busy && !selfUpdating && addons.Any(a => a.Selected && a.NeedsUpdate) ? updateButton : neutralButton;
            play.BackColor = !busy && !selfUpdating && lastRunSucceeded && !pending ? readyButton : neutralButton;
        }

        public static string Resource(string name) {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            using (var reader = new StreamReader(stream, Encoding.UTF8)) return reader.ReadToEnd();
        }
        public UpdaterForm(string root) {
            gameRoot = Path.GetFullPath(root);
            tempRoot = Path.Combine(Path.GetTempPath(), "wow-addon-gui-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            sessionCache = Path.Combine(tempRoot, "packages-" + Guid.NewGuid().ToString("N"));
            enginePath = Path.Combine(tempRoot, "Update-WoWAddons.ps1");
            File.WriteAllText(enginePath, Resource("engine.ps1"), new UTF8Encoding(true));
            var catalog = json.Deserialize<Dictionary<string, object>>(Resource("catalog.json"));
            hosts = catalog["trustedHosts"];
            foreach (Dictionary<string, object> source in (IEnumerable)catalog["sources"]) {
                string name = (string)source["name"];
                if (name.StartsWith("Beispiel:")) continue;
                addons.Add(new Addon { Source = source, Name = name, Folders = FolderNames(source) });
            }
            addons.Sort((left, right) => StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("de-DE"), true).Compare(left.Name, right.Name));
            BuildWindow();
            HandleCreated += delegate {
                try { int dark = 1; if (DwmSetWindowAttribute(Handle, 20, ref dark, 4) != 0) DwmSetWindowAttribute(Handle, 19, ref dark, 4); } catch { }
            };
            ScanInstalled();
            BuildOptions();
            FormClosing += delegate(object sender, FormClosingEventArgs args) {
                if (selfUpdating) {
                    args.Cancel = true;
                    if (selfCancel != null) selfCancel.Cancel();
                    return;
                }
                if (busy) {
                    args.Cancel = true;
                    RequestCancel();
                }
            };
            FormClosed += delegate { try { Directory.Delete(tempRoot, true); } catch { } };
        }

        static string[] FolderNames(Dictionary<string, object> source) {
            object specification = source["addons"];
            if (!(specification is string)) {
                var folders = new List<string>();
                foreach (object item in (IEnumerable)specification) {
                    folders.Add(item is string ? (string)item : (string)((Dictionary<string, object>)item)["target"]);
                }
                return folders.ToArray();
            }
            // Hauptordner der hinterlegten Mehrfachpakete. Ihre Module werden gemeinsam verwaltet.
            switch ((string)source["name"]) {
                case "DBM Warmane": return new[] { "DBM-Core" };
                case "WeakAuras WotLK": return new[] { "WeakAuras", "APIDocumentation" };
                case "Details WotLK": return new[] { "Details" };
                case "ClassicAPI": return new[] { "!!!ClassicAPI", "ClassicAPI" };
                case "CompactRaidFrame": return new[] { "CompactRaidFrame" };
                case "CompactRaidFrame HealEx": return new[] { "CompactRaidFrame_HealEx" };
                case "AtlasLoot": return new[] { "AtlasLoot" };
                case "Bagnon (optional)": return new[] { "Bagnon" };
                default: throw new InvalidDataException("Keine Ordnererkennung für " + source["name"]);
            }
        }

        void BuildWindow() {
            Text = "WoW Addon Updater v" + AppVersion.Current + " · WoW 3.3.5a";
            Font = new Font("Segoe UI", 10);
            BackColor = surface;
            ForeColor = textColor;
            ClientSize = new Size(1120, 850);
            MinimumSize = new Size(1080, 740);
            StartPosition = FormStartPosition.CenterScreen;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 9 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 155));
            Controls.Add(layout);
            layout.Controls.Add(new Label { Text = "Deine Addons. Ein Ort für alle Updates.", Font = new Font("Segoe UI", 20, FontStyle.Bold), Dock = DockStyle.Fill, AutoSize = false }, 0, 0);
            var details = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
            details.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            details.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            details.Controls.Add(new Label { Text = "WoW 3.3.5a  ·  " + gameRoot, Dock = DockStyle.Fill, ForeColor = Color.Silver, AutoEllipsis = true }, 0, 0);
            details.Controls.Add(new Label { Text = "Haken setzen: installieren oder updaten. Haken entfernen: unverändert lassen.", Dock = DockStyle.Fill, ForeColor = Color.Silver }, 0, 1);
            layout.Controls.Add(details, 0, 1);
            list.Dock = DockStyle.Fill;
            list.BackColor = Color.FromArgb(40, 40, 40);
            list.ForeColor = textColor;
            list.BorderStyle = BorderStyle.None;
            list.OwnerDraw = true;
            // Dunkle native Scrollleisten; leere Tabellenflächen zeichnet BufferedListView.
            list.HandleCreated += delegate { try { SetWindowTheme(list.Handle, "DarkMode_Explorer", null); } catch { } };
            list.DrawColumnHeader += delegate(object sender, DrawListViewColumnHeaderEventArgs e) {
                using (var brush = new SolidBrush(Color.FromArgb(48, 48, 48))) e.Graphics.FillRectangle(brush, e.Bounds);
                TextRenderer.DrawText(e.Graphics, e.Header.Text, Font, new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height), textColor, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            };
            list.DrawItem += delegate { };
            list.DrawSubItem += delegate(object sender, DrawListViewSubItemEventArgs e) {
                Color background = e.Item.Selected ? Color.FromArgb(50, 65, 80) : list.BackColor;
                using (var brush = new SolidBrush(background)) e.Graphics.FillRectangle(brush, e.Bounds);
                int offset = e.ColumnIndex == 0 ? 25 : 6;
                if (e.ColumnIndex == 0) {
                    var box = new Rectangle(e.Bounds.X + 5, e.Bounds.Y + (e.Bounds.Height - 14) / 2, 14, 14);
                    using (var brush = new SolidBrush(e.Item.Checked ? Color.FromArgb(70, 155, 230) : Color.FromArgb(48, 48, 48))) e.Graphics.FillRectangle(brush, box);
                    using (var pen = new Pen(Color.Gray)) e.Graphics.DrawRectangle(pen, box);
                    if (e.Item.Checked) using (var pen = new Pen(Color.White, 2)) e.Graphics.DrawLines(pen, new[] { new Point(box.X + 3, box.Y + 7), new Point(box.X + 6, box.Y + 10), new Point(box.X + 12, box.Y + 3) });
                }
                TextRenderer.DrawText(e.Graphics, e.SubItem.Text, Font, new Rectangle(e.Bounds.X + offset, e.Bounds.Y, e.Bounds.Width - offset, e.Bounds.Height), e.Item.ForeColor, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            };
            list.View = View.Details;
            list.CheckBoxes = true;
            list.FullRowSelect = true;
            list.GridLines = false;
            list.ShowGroups = false;
            list.HideSelection = false;
            list.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            list.Columns.Add("Addon / Paket", 225);
            list.Columns.Add("Status", 240);
            list.Columns.Add("Erkannter Ordner", 310);
            list.Columns.Add("Installiert", 140);
            list.Columns.Add("Verfügbar", 140);
            list.ShowItemToolTips = true;
            // ClientSize enthält bereits nur die Fläche OHNE Scrollleisten.
            // Ein weiterer Abzug ließ einen ungezeichneten, weißen Kopfbereich frei.
            // Tatsächliche Spaltenbreiten berücksichtigen außerdem die DPI-Skalierung.
            list.ClientSizeChanged += delegate { QueueTableLayout(); };
            // ItemChecked kann während der nativen Handle-Erstellung auftreten,
            // wenn andere Zeilen noch Index -1 haben. Keine Checked-Getter abfragen.
            list.ItemCheck += delegate(object sender, ItemCheckEventArgs e) {
                if (busy || selfUpdating) { e.NewValue = e.CurrentValue; return; }
                if (e.Index < 0 || e.Index >= list.Items.Count) return;
                var addon = list.Items[e.Index].Tag as Addon;
                if (addon == null) return;
                addon.Selected = e.NewValue == CheckState.Checked;
                selection.Text = addons.Count(a => a.Selected) + " von " + addons.Count + " Paketen ausgewählt";
                RefreshButtonColors();
            };
            foreach (Addon addon in addons) {
                addon.Row = new ListViewItem(new[] { addon.Name, "Noch nicht geprüft", "", "—", "Noch nicht geprüft" });
                addon.Row.Tag = addon;
                list.Items.Add(addon.Row);
            }
            layout.Controls.Add(list, 0, 2);
            selection.Dock = DockStyle.Fill;
            selection.TextAlign = ContentAlignment.MiddleLeft;
            layout.Controls.Add(selection, 0, 3);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            check.Text = "Addons auslesen / prüfen";
            check.Size = new Size(260, 43);
            check.FlatStyle = FlatStyle.Flat;
            check.BackColor = Color.FromArgb(50, 50, 50);
            check.Click += delegate { Start(true); };
            install.Text = "Auswahl installieren / updaten";
            install.Size = new Size(310, 43);
            install.BackColor = neutralButton;
            install.ForeColor = Color.White;
            install.FlatStyle = FlatStyle.Flat;
            install.Click += delegate { Start(false); };
            buttons.Controls.Add(check);
            buttons.Controls.Add(install);
            cancel.Text = "Abbrechen";
            cancel.Size = new Size(145, 43);
            cancel.FlatStyle = FlatStyle.Flat;
            cancel.BackColor = Color.FromArgb(50, 50, 50);
            cancel.Enabled = false;
            cancel.Click += delegate { RequestCancel(); };
            buttons.Controls.Add(cancel);
            play.Text = "WoW starten";
            play.Size = new Size(145, 43);
            play.FlatStyle = FlatStyle.Flat;
            play.BackColor = Color.FromArgb(50, 50, 50);
            play.Click += delegate { LaunchWow(); };
            buttons.Controls.Add(play);
            foreach (Button button in new[] { check, install, cancel, play }) {
                button.FlatAppearance.BorderColor = Color.FromArgb(80, 80, 80);
                button.Paint += delegate(object sender, PaintEventArgs e) {
                    var b = (Button)sender;
                    if (!b.Enabled) {
                        using (var brush = new SolidBrush(Color.FromArgb(42, 42, 42))) e.Graphics.FillRectangle(brush, 1, 1, b.Width - 2, b.Height - 2);
                        TextRenderer.DrawText(e.Graphics, b.Text, b.Font, b.ClientRectangle, Color.FromArgb(150, 150, 150), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                    }
                };
            }
            layout.Controls.Add(buttons, 0, 4);
            BuildMore(layout);
            var progressRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
            progressRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));
            progressRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
            status.Dock = DockStyle.Fill;
            status.Text = File.Exists(Path.Combine(gameRoot, "Wow.exe")) ? "Bereit. Die Prüfung lädt die Pakete zum Vergleich herunter." : "Bitte diese EXE direkt neben Wow.exe ablegen.";
            progress.Dock = DockStyle.Fill;
            progressRow.Controls.Add(status, 0, 0);
            progressRow.Controls.Add(progress, 1, 0);
            layout.Controls.Add(progressRow, 0, 7);
            log.Dock = DockStyle.Fill;
            log.Multiline = true;
            log.ReadOnly = true;
            log.ScrollBars = ScrollBars.Vertical;
            log.Font = new Font("Consolas", 9);
            log.BackColor = Color.FromArgb(24, 24, 24);
            log.ForeColor = textColor;
            log.BorderStyle = BorderStyle.None;
            log.HandleCreated += delegate { try { SetWindowTheme(log.Handle, "DarkMode_Explorer", null); } catch { } };
            layout.Controls.Add(log, 0, 8);
        }

        void QueueTableLayout() {
            if (tableLayoutQueued || !IsHandleCreated || IsDisposed) return;
            tableLayoutQueued = true;
            BeginInvoke(new Action(delegate {
                tableLayoutQueued = false;
                if (!IsDisposed && !list.IsDisposed) {
                    FitTableColumns();
                    list.Invalidate(true);
                }
            }));
        }

        void FitTableColumns() {
            if (list.Columns.Count != 5) return;
            int fixedWidth = list.Columns[0].Width + list.Columns[1].Width + list.Columns[3].Width + list.Columns[4].Width;
            int width = Math.Max(120, list.ClientSize.Width - fixedWidth);
            if (list.Columns[2].Width != width) list.Columns[2].Width = width;
        }

        public void ScanInstalled() {
            lastRunSucceeded = false;
            foreach (Addon addon in addons) {
                addon.Verified = false;
                addon.NeedsUpdate = false;
                var found = addon.Folders.Where(folder => {
                    string path = Path.Combine(gameRoot, "Interface", "AddOns", folder);
                    return Directory.Exists(path) && Directory.GetFiles(path, "*.toc").Length > 0;
                }).ToArray();
                addon.Installed = found.Length > 0;
                addon.Selected = addon.Installed;
                addon.Row.Checked = addon.Installed;
                addon.Row.SubItems[1].Text = addon.Installed ? "Installiert · noch nicht geprüft" : "Nicht installiert";
                addon.Row.SubItems[2].Text = string.Join(", ", found);
                addon.Row.SubItems[3].Text = InstalledVersion(addon);
                addon.Row.SubItems[4].Text = "Noch nicht geprüft";
                addon.Row.ForeColor = textColor;
            }
            RefreshButtonColors();
            if (captions.Count > 0) TranslateSurface();
        }

        void OnUI(Action action) { if (!IsDisposed) BeginInvoke(new Action(delegate { action(); TranslateSurface(); })); }
        void AppendLog(string line) {
            fullLog.AppendLine(line);
            if (log.TextLength > 120000) log.Text = log.Text.Substring(log.TextLength - 60000);
            log.AppendText(L.T(line) + Environment.NewLine);
        }
        void RequestCancel() {
            if (selfUpdating) { if (selfCancel != null) selfCancel.Cancel(); cancel.Enabled = false; RefreshButtonColors(); return; }
            if (!busy || cancelRequested) return;
            try { File.WriteAllText(Path.Combine(tempRoot, "cancel.signal"), "cancel"); }
            catch (Exception ex) { AppendLog("Abbruch konnte nicht angefordert werden: " + ex.Message); return; }
            cancelRequested = true;
            cancel.Enabled = false;
            RefreshButtonColors();
            status.Text = "Abbruch angefordert. Laufender Dateiaustausch wird sicher beendet …";
            AppendLog("INFO | Abbruch angefordert. Eine Serverantwort kann noch bis zu 60 Sekunden dauern.");
        }
        void Start(bool dryRun) {
            if (busy || selfUpdating) return;
            if (!File.Exists(Path.Combine(gameRoot, "Wow.exe"))) {
                L.Show(this, "Kopiere WoW-Addon-Updater.exe direkt in deinen WoW-Ordner neben Wow.exe.", "WoW nicht gefunden");
                return;
            }
            if (!dryRun && Process.GetProcessesByName("Wow").Length > 0) {
                L.Show(this, "Bitte WoW vor der Installation schließen.", "WoW läuft noch");
                return;
            }
            if (dryRun) ScanInstalled();
            Addon[] selected = addons.Where(a => a.Selected && (dryRun || !a.Verified || a.NeedsUpdate)).ToArray();
            if (selected.Length == 0) {
                status.Text = dryRun ? "Keine bekannten Addons gefunden. Zum Installieren Haken setzen." : "Keine Installation nötig: Auswahl ist aktuell oder leer.";
                return;
            }
            busy = true;
            lastRunSucceeded = false;
            cancelRequested = false;
            File.Delete(Path.Combine(tempRoot, "cancel.signal"));
            cancel.Enabled = true;
            // Native ListViews zeichnen deaktivierte Leerflächen je nach Windows-Version hell.
            // Nur die Auswahl sperren (ItemCheck), Scrollen und dunkles Rendering bleiben aktiv.
            check.Enabled = install.Enabled = play.Enabled = moreTools.Enabled = false;
            RefreshButtonColors();
            progress.Maximum = selected.Length;
            progress.Value = 0;
            log.Clear();
            AppendLog("--- " + DateTime.Now + (dryRun ? " · Prüfung" : " · Installation") + " ---");
            var worker = new BackgroundWorker();
            int failures = 0;
            worker.DoWork += delegate {
                // Ein neuer Prueflauf bekommt einen neuen Sitzungscache. Installation nutzt den letzten.
                if (dryRun) sessionCache = Path.Combine(tempRoot, "packages-" + Guid.NewGuid().ToString("N"));
                foreach (Addon addon in selected) {
                    if (cancelRequested) break;
                    OnUI(delegate { status.Text = (dryRun ? "Prüfe: " : "Aktualisiere: ") + addon.Name; addon.Row.SubItems[1].Text = "In Bearbeitung …"; });
                    try {
                        string result = RunSource(addon, dryRun);
                        OnUI(delegate {
                            addon.Row.SubItems[1].Text = result;
                            addon.Row.ForeColor = Color.FromArgb(125, 215, 155);
                            addon.Row.SubItems[2].Text = string.Join(", ", addon.Folders.Where(f => Directory.Exists(Path.Combine(gameRoot, "Interface", "AddOns", f))));
                            addon.Row.SubItems[3].Text = InstalledVersion(addon);
                        });
                    } catch (OperationCanceledException) {
                        cancelRequested = true;
                        OnUI(delegate { addon.Row.SubItems[1].Text = "Abgebrochen"; addon.Row.ForeColor = Color.Khaki; });
                        break;
                    } catch (Exception ex) {
                        failures++;
                        string message = ex.Message;
                        OnUI(delegate { addon.Row.SubItems[1].Text = "Fehler · siehe Protokoll"; addon.Row.ForeColor = Color.Salmon; AppendLog("FEHLER | " + addon.Name + ": " + message); });
                    }
                    OnUI(delegate { progress.Value++; });
                }
            };
            worker.RunWorkerCompleted += delegate(object sender, RunWorkerCompletedEventArgs e) {
                busy = false;
                cancel.Enabled = false;
                check.Enabled = install.Enabled = play.Enabled = moreTools.Enabled = true;
                lastRunSucceeded = e.Error == null && !cancelRequested && failures == 0;
                RefreshButtonColors();
                RefreshBackupSize();
                status.Text = e.Error != null ? "Vorgang fehlgeschlagen: " + e.Error.Message : cancelRequested ? "Abgebrochen. Bereits abgeschlossene Pakete bleiben erhalten. Fehler: " + failures : failures > 0 ? "Beendet mit " + failures + " Fehlern. Siehe Protokoll." : dryRun ? "Prüfung abgeschlossen. Auswahl kann jetzt aktualisiert werden." : "Ausgewählte Pakete erfolgreich verarbeitet.";
                worker.Dispose();
            };
            worker.RunWorkerAsync();
        }

        string RunSource(Addon addon, bool dryRun) {
            addon.Verified = false;
            var source = new Dictionary<string, object>(addon.Source);
            source["enabled"] = true; // Die Auswahl im Fenster ist maßgeblich.
            var config = new Dictionary<string, object> { { "trustedHosts", hosts }, { "sources", new[] { source } } };
            string configPath = Path.Combine(tempRoot, "selected.json");
            File.WriteAllText(configPath, json.Serialize(config), new UTF8Encoding(true));
            string powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe");
            var start = new ProcessStartInfo(powershell) {
                Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"" + enginePath + "\" -Config \"" + configPath + "\" -WowPath \"" + Path.Combine(gameRoot, "Wow.exe") + "\"" + (dryRun ? " -DryRun" : ""),
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = gameRoot
            };
            bool changed = false;
            bool cached = false;
            start.Arguments += " -CancelFile \"" + Path.Combine(tempRoot, "cancel.signal") + "\"";
            start.Arguments += " -SessionCacheDirectory \"" + sessionCache + "\"";
            using (var process = new Process { StartInfo = start }) {
                process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e) {
                    if (e.Data == null) return;
                    if (e.Data.Contains("Wuerde installieren:") || e.Data.Contains("OK      | Installiert:")) changed = true;
                    if (e.Data.Contains("CACHE |")) cached = true;
                    string line = e.Data;
                    if (line.StartsWith("VERSION | ")) { ReceiveVersions(addon, line.Substring(10)); return; }
                    OnUI(delegate { AppendLog(line); });
                };
                process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e) {
                    if (e.Data != null) { string line = e.Data; OnUI(delegate { AppendLog(line); }); }
                };
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit();
                if (process.ExitCode == 2) throw new OperationCanceledException();
                if (process.ExitCode != 0) throw new Exception("Download oder Installation fehlgeschlagen.");
            }
            addon.Verified = true;
            addon.NeedsUpdate = dryRun && changed;
            return (cached ? "Cache · " : "") + (dryRun ? (changed ? "Update / Reparatur verfügbar" : "Aktuell") : (changed ? "Installiert / aktualisiert" : "Bereits aktuell"));
        }

        public void TestLayout(string imagePath) {
            Show();
            Application.DoEvents();
            Size originalSize = ClientSize;
            list.EnsureVisible(list.Items.Count - 1);
            Application.DoEvents();
            foreach (Size size in new[] { new Size(1400, 1100), originalSize }) {
                ClientSize = size;
                Application.DoEvents();
                if (size != originalSize && list.Items[0].Bounds.Top > list.Font.Height * 2)
                    throw new Exception("Leerraum oberhalb der ersten Zeile nach Vergrößerung.");
                FitTableColumns();
                Application.DoEvents();
                if (list.Columns.Cast<ColumnHeader>().Sum(c => c.Width) != list.ClientSize.Width)
                    throw new Exception("Tabellenspalten lassen eine freie Randfläche übrig.");
                using (var bitmap = new Bitmap(list.Width, list.Height)) {
                    list.DrawToBitmap(bitmap, list.ClientRectangle);
                    bitmap.Save(Path.ChangeExtension(imagePath, size == originalSize ? ".table-small.png" : ".table-large.png"));
                    // Rechts oben liegt kein Text: hier war der weiße Kopfbereich sichtbar.
                    Color edge = bitmap.GetPixel(list.ClientSize.Width - 3, 5);
                    if (edge.R > 180 && edge.G > 180 && edge.B > 180)
                        throw new Exception("Weißer Restbereich rechts im Tabellenkopf.");
                }
            }
            list.EnsureVisible(list.Items.Count - 1);
            WindowState = FormWindowState.Maximized;
            Application.DoEvents();
            FitTableColumns();
            Application.DoEvents();
            if (list.Items[0].Bounds.Top > list.Font.Height * 2)
                throw new Exception("Leerraum oberhalb der ersten Zeile nach Maximieren.");
            busy = true;
            for (int i = 0; i < 3; i++) {
                string previous = addons[i].Row.SubItems[1].Text;
                addons[i].Row.SubItems[1].Text = "In Bearbeitung …";
                list.Update();
                Application.DoEvents();
                using (var bitmap = new Bitmap(list.Width, list.Height)) {
                    list.DrawToBitmap(bitmap, list.ClientRectangle);
                    int y = list.Items[list.Items.Count - 1].Bounds.Bottom + 5;
                    if (y < list.ClientSize.Height && bitmap.GetPixel(5, y).ToArgb() != list.BackColor.ToArgb())
                        throw new Exception("Leere Tabellenfläche während Statuswechsel nicht dunkel.");
                }
                addons[i].Row.SubItems[1].Text = previous;
            }
            busy = false;
            using (var bitmap = new Bitmap(Width, Height)) {
                DrawToBitmap(bitmap, new Rectangle(0, 0, Width, Height));
                bitmap.Save(Path.ChangeExtension(imagePath, ".maximized.png"));
            }
            WindowState = FormWindowState.Normal;
            Application.DoEvents();
            // Den nativen Listenaufbau erneut durchlaufen (z.B. nach Windows-Designwechsel).
            typeof(Control).GetMethod("RecreateHandle", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(list, null);
            Application.DoEvents();
            if (!(bool)typeof(Control).GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(list, null)) throw new Exception("Tabellen-Doppelpufferung ist deaktiviert.");
            foreach (bool enabled in new[] { false, true }) {
                list.Enabled = enabled;
                Application.DoEvents();
                if (!((BufferedListView)list).HasExpectedBackground()) throw new Exception("Nativer Tabellenhintergrund ist nicht dunkel; Enabled=" + enabled);
                using (var bitmap = new Bitmap(Width, Height)) {
                    DrawToBitmap(bitmap, new Rectangle(0, 0, Width, Height));
                    bitmap.Save(Path.ChangeExtension(imagePath, enabled ? ".enabled.png" : ".disabled.png"));
                }
            }
            if (addons.Any(a => a.Selected != a.Row.Checked)) throw new Exception("Hakenauswahl nach Handle-Neuaufbau inkonsistent.");
            foreach (Addon addon in addons) {
                string previous = addon.Row.SubItems[1].Text;
                addon.Row.SubItems[1].Text = "In Bearbeitung …";
                list.Update();
                addon.Row.SubItems[1].Text = previous;
                list.Update();
            }
            using (var bitmap = new Bitmap(Width, Height)) { DrawToBitmap(bitmap, new Rectangle(0, 0, Width, Height)); bitmap.Save(imagePath); }
            var testLayout = (TableLayoutPanel)Controls[0];
            ((Button)testLayout.GetControlFromPosition(0, 5)).PerformClick();
            Application.DoEvents();
            using (var bitmap = new Bitmap(Width, Height)) { DrawToBitmap(bitmap, new Rectangle(0, 0, Width, Height)); bitmap.Save(Path.ChangeExtension(imagePath, ".more.png")); }
            if (addons.Count != 21) throw new Exception("Unerwartete Anzahl Addon-Pakete: " + addons.Count);
            var alphabetical = addons.Select(a => a.Name).OrderBy(name => name, StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("de-DE"), true));
            if (!list.Items.Cast<ListViewItem>().Select(row => row.Text).SequenceEqual(alphabetical))
                throw new Exception("Addon-Liste ist nicht alphabetisch sortiert.");
            var mapTooltip = addons.Single(a => a.Name == "WorldMapTooltipScale");
            if (mapTooltip.Folders.Length != 1 || mapTooltip.Folders[0] != "WorldMapTooltipScale" || mapTooltip.Row.Checked)
                throw new Exception("WorldMapTooltipScale: Ordnerzuordnung oder Vorauswahl fehlerhaft.");
            var tomtom = addons.Single(a => a.Name == "TomTom");
            if (!tomtom.Installed || !tomtom.Row.Checked) throw new Exception("TomTom wurde nicht erkannt.");
            if (addons.Single(a => a.Name == "NPCScan").Row.Checked) throw new Exception("Nicht installiertes NPCScan wurde ausgewählt.");
            TestExtras();
            TestButtonColors(tomtom);
            TestLocalization(imagePath);
            SelfUpdate.TestMetadata();
            // Prozessanbindung separat mit einem lokalen Test-Backend prüfen.
            string original = File.ReadAllText(enginePath);
            try {
                File.WriteAllText(enginePath, "param([string]$Config,[string]$WowPath,[switch]$DryRun,[string]$CancelFile,[string]$SessionCacheDirectory)\r\n" +
                    "$c = Get-Content -LiteralPath $Config -Raw | ConvertFrom-Json\r\n" +
                    "if (!(Test-Path -LiteralPath $WowPath) -or $c.sources.Count -ne 1 -or !$c.sources[0].enabled) { exit 4 }\r\n" +
                    "if ($DryRun) { Write-Output '[Prueflauf] Wuerde installieren: TomTom' } else { Write-Output 'OK      | Installiert: TomTom' }\r\n", new UTF8Encoding(true));
                if (RunSource(tomtom, true) != "Update / Reparatur verfügbar") throw new Exception("Update-Status nicht erkannt.");
                if (!tomtom.Verified || !tomtom.NeedsUpdate) throw new Exception("Update nicht zur Installation vorgemerkt.");
                if (RunSource(tomtom, false) != "Installiert / aktualisiert") throw new Exception("Installationsstatus nicht erkannt.");
                File.WriteAllText(enginePath, "Write-Output 'INFO | bereits aktuell'\r\nexit 0\r\n");
                if (RunSource(tomtom, true) != "Aktuell") throw new Exception("Aktuell-Status nicht erkannt.");
                if (!tomtom.Verified || tomtom.NeedsUpdate) throw new Exception("Aktuelles Addon wird erneut verarbeitet.");
                File.WriteAllText(enginePath, "Write-Output 'FEHLER | Testfehler'\r\nexit 1\r\n");
                bool failed = false;
                try { RunSource(tomtom, true); } catch { failed = true; }
                if (!failed) throw new Exception("Backend-Fehler wurde als Erfolg behandelt.");
                File.WriteAllText(enginePath, "Write-Output 'INFO | CACHE | Test'\r\nexit 0\r\n");
                if (RunSource(tomtom, true) != "Cache · Aktuell") throw new Exception("Cache-Status nicht erkannt.");
                File.WriteAllText(enginePath, "exit 2\r\n");
                bool cancelled = false;
                try { RunSource(tomtom, true); } catch (OperationCanceledException) { cancelled = true; }
                if (!cancelled) throw new Exception("Abbruch nicht erkannt.");
            } finally { File.WriteAllText(enginePath, original, new UTF8Encoding(true)); }
            Application.DoEvents();
            Close();
        }

        void TestButtonColors(Addon addon) {
            // Auch eine gerade entfernte/nicht eingefügte Zeile darf Farbupdates nicht stören.
            int rowIndex = addon.Row.Index;
            list.Items.Remove(addon.Row);
            RefreshButtonColors();
            list.Items.Insert(rowIndex, addon.Row);
            addon.Row.Checked = true;
            if (!addon.Selected) throw new Exception("Hakenauswahl wurde nicht ins Modell übernommen.");
            RefreshButtonColors();
            if (play.BackColor != neutralButton) throw new Exception("Ungeprüfter Zustand darf nicht grün sein.");
            busy = true;
            bool wasChecked = addon.Row.Checked;
            addon.Row.Checked = !wasChecked;
            if (addon.Row.Checked != wasChecked || addon.Selected != wasChecked || !list.Enabled)
                throw new Exception("Auswahlsperre während des Vorgangs fehlerhaft.");
            cancel.Enabled = true;
            RefreshButtonColors();
            if (cancel.BackColor != cancelButton || play.BackColor != neutralButton) throw new Exception("Laufender Vorgang: falsche Buttonfarben.");
            cancel.Enabled = false;
            RefreshButtonColors();
            if (cancel.BackColor != neutralButton) throw new Exception("Deaktivierter Abbruch bleibt rot.");
            busy = false;
            lastRunSucceeded = true;
            addon.Verified = true;
            addon.NeedsUpdate = false;
            RefreshButtonColors();
            if (play.BackColor != readyButton || install.BackColor != neutralButton) throw new Exception("Prüfung ohne Updates: falsche Buttonfarben.");
            addon.NeedsUpdate = true;
            RefreshButtonColors();
            if (install.BackColor != updateButton || play.BackColor != neutralButton) throw new Exception("Verfügbares Update: falsche Buttonfarben.");
            addon.Row.Checked = false;
            if (addon.Selected) throw new Exception("Abwahl wurde nicht ins Modell übernommen.");
            RefreshButtonColors();
            if (play.BackColor != neutralButton) throw new Exception("Abgewähltes Update darf keinen grünen Abschluss anzeigen.");
            addon.Row.Checked = true;
            addon.NeedsUpdate = false;
            RefreshButtonColors();
            if (play.BackColor != readyButton) throw new Exception("Erfolgreiche Installation muss grün sein.");
            lastRunSucceeded = false;
            RefreshButtonColors();
            if (play.BackColor != neutralButton) throw new Exception("Fehler oder Abbruch darf nicht grün sein.");
            ScanInstalled();
        }
    }

    static class Program {
        [STAThread]
        static int Main(string[] args) {
            if (args.Length > 0 && args[0].StartsWith("--test-")) Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            L.Load();
            try {
                if (args.Length > 0 && args[0] == "--apply-update") return SelfUpdate.Apply(args);
                if (args.Length == 3 && args[0] == "--test-layout") {
                    L.English = false;
                    using (var form = new UpdaterForm(args[1])) form.TestLayout(args[2]);
                    return 0;
                }
                using (var mutex = new Mutex(false, "Local\\WoWAddonUpdaterGui")) {
                    bool acquired;
                    try { acquired = mutex.WaitOne(0); } catch (AbandonedMutexException) { acquired = true; }
                    if (!acquired) { L.Show("Der Updater ist bereits geöffnet."); return 1; }
                    try {
                        var form = new UpdaterForm(AppDomain.CurrentDomain.BaseDirectory);
                        if (args.Length == 3 && args[0] == "--finish-update") form.Shown += async delegate {
                            try { await System.Threading.Tasks.Task.Run(() => SelfUpdate.Cleanup(args[1], Application.ExecutablePath, int.Parse(args[2]))); }
                            catch (Exception ex) { L.Show(form, ex.Message, "Updater-Update"); }
                        };
                        Application.Run(form);
                    }
                    finally { mutex.ReleaseMutex(); }
                }
                return 0;
            } catch (Exception ex) {
                if (args.Length > 0 && args[0].StartsWith("--test-")) { Console.Error.WriteLine(ex); return 1; }
                L.Show("Der Updater konnte nicht gestartet werden:\r\n" + ex.Message, "WoW Addon Updater", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }
    }
}
