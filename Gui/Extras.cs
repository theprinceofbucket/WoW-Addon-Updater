using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace WowUpdater {
    // Gemeinsame Dateifunktionen lehnen Verknüpfungen ab, bevor rekursive Operationen beginnen.
    public static class LocalFiles {
        public static void ValidateTree(string path) {
            string full = Path.GetFullPath(path);
            for (string p = full; !string.IsNullOrEmpty(p); p = Path.GetDirectoryName(p)) {
                if ((Directory.Exists(p) || File.Exists(p)) && (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Verknüpfungen werden nicht verarbeitet: " + p);
            }
            if (!Directory.Exists(full)) return;
            foreach (string child in Directory.GetFileSystemEntries(full)) {
                if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0) throw new IOException("Verknüpfung im Ordner: " + child);
                if (Directory.Exists(child)) ValidateTree(child);
            }
        }
        public static long Size(string path) {
            ValidateTree(path);
            return Directory.Exists(path) ? Directory.GetFiles(path, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length) : 0;
        }
        public static void DeleteOwned(string path, string parent, string expectedName) {
            string full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
            if (!string.Equals(Path.GetDirectoryName(full), Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
                || !string.Equals(Path.GetFileName(full), expectedName, StringComparison.Ordinal)) throw new IOException("Unsicherer Löschpfad.");
            ValidateTree(full);
            if (Directory.Exists(full)) Directory.Delete(full, true);
        }
        public static void CopyTree(string source, string target) {
            ValidateTree(source);
            ValidateTree(target);
            Directory.CreateDirectory(target);
            foreach (string f in Directory.GetFiles(source)) File.Copy(f, Path.Combine(target, Path.GetFileName(f)), false);
            foreach (string d in Directory.GetDirectories(source)) CopyTree(d, Path.Combine(target, Path.GetFileName(d)));
        }
        public static string Version(string folder) {
            if (!Directory.Exists(folder)) return "—";
            string toc = Path.Combine(folder, Path.GetFileName(folder) + ".toc");
            if (!File.Exists(toc)) return "Nicht angegeben";
            var match = Regex.Match(File.ReadAllText(toc), @"(?im)^##\s*Version\s*:\s*([^\r\n]+)");
            return match.Success && !Regex.IsMatch(match.Groups[1].Value, @"@|\$") ? match.Groups[1].Value.Trim() : "Nicht angegeben";
        }
        public static string Restore(string backupRoot, string savedFolder, string addonsRoot) {
            ValidateTree(backupRoot); ValidateTree(savedFolder); ValidateTree(addonsRoot);
            string root = Path.GetFullPath(backupRoot).TrimEnd('\\');
            string saved = Path.GetFullPath(savedFolder).TrimEnd('\\');
            string run = Path.GetDirectoryName(saved);
            if (!string.Equals(Path.GetDirectoryName(run), root, StringComparison.OrdinalIgnoreCase)) throw new IOException("Sicherung liegt nicht im Backup-Ordner.");
            string name = Path.GetFileName(saved);
            if (!File.Exists(Path.Combine(saved, name + ".toc"))) throw new IOException("Sicherung enthält keine passende TOC-Datei.");
            string target = Path.Combine(addonsRoot, name);
            string stage = Path.Combine(Path.GetDirectoryName(addonsRoot), ".wow-restore-" + Guid.NewGuid().ToString("N"));
            string previous = Path.Combine(root, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + "_restore_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            bool moved = false;
            try {
                CopyTree(saved, stage);
                Directory.CreateDirectory(addonsRoot);
                if (Directory.Exists(target)) {
                    Directory.CreateDirectory(previous);
                    try { Directory.Move(target, Path.Combine(previous, name)); }
                    catch (Exception ex) { throw new IOException("Aktueller Stand konnte nicht gesichert werden: " + target + " -> " + Path.Combine(previous, name), ex); }
                    moved = true;
                }
                try { Directory.Move(stage, target); }
                catch {
                    if (moved && !Directory.Exists(target)) Directory.Move(Path.Combine(previous, name), target);
                    throw;
                }
                return moved ? previous : null;
            } finally {
                if (Directory.Exists(stage)) DeleteOwned(stage, Path.GetDirectoryName(addonsRoot), Path.GetFileName(stage));
            }
        }
        public static string SaveLog(string documents, string content) {
            string folder = Path.Combine(documents, "WoWUpdaterLogs");
            ValidateTree(folder);
            Directory.CreateDirectory(folder);
            string file = Path.Combine(folder, "WoWUpdater-" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss-fff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".txt");
            File.WriteAllText(file, content, new UTF8Encoding(true));
            return file;
        }
    }

    public partial class UpdaterForm {
        readonly Button play = new Button();
        readonly FlowLayoutPanel moreTools = new FlowLayoutPanel();
        readonly Label backupSize = new Label();
        readonly StringBuilder fullLog = new StringBuilder();
        string BackupRoot { get { return Path.Combine(gameRoot, "Interface", "AddOns_UpdaterBackups"); } }
        string AddonsRoot { get { return Path.Combine(gameRoot, "Interface", "AddOns"); } }
        Button ExtraButton(string title, int width, Action action) {
            var b = new Button { Text = L.T(title), Width = width, Height = 35, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(50, 50, 50), ForeColor = textColor };
            b.FlatAppearance.BorderColor = Color.FromArgb(80, 80, 80);
            b.Tag = title;
            b.Click += delegate { try { action(); } catch (Exception ex) { AppendLog("FEHLER | " + ex.Message); L.Show(this, ex.Message, "Vorgang fehlgeschlagen", MessageBoxButtons.OK, MessageBoxIcon.Error); } };
            return b;
        }
        void BuildMore(TableLayoutPanel layout) {
            var toggle = ExtraButton("Mehr ▼", 110, delegate { });
            toggle.Height = 28;
            toggle.Anchor = AnchorStyles.Left;
            moreTools.Dock = DockStyle.Fill;
            moreTools.WrapContents = false;
            moreTools.Visible = false;
            toggle.Click += delegate {
                moreTools.Visible = !moreTools.Visible;
                layout.RowStyles[6].Height = moreTools.Visible ? 46 : 0;
                toggle.Text = moreTools.Visible ? "Mehr ▲" : "Mehr ▼";
                if (moreTools.Visible) RefreshBackupSize();
            };
            moreTools.Controls.Add(ExtraButton("Backup wiederherstellen", 235, ShowBackups));
            moreTools.Controls.Add(ExtraButton("Alle Backups löschen", 210, DeleteBackups));
            moreTools.Controls.Add(ExtraButton("Protokoll speichern", 185, delegate {
                string file = LocalFiles.SaveLog(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), L.T("WoW Addon Updater v" + AppVersion.Current + "\r\nSpielordner: " + gameRoot + "\r\nExport: " + DateTime.Now + "\r\n\r\n" + fullLog.ToString()));
                AppendLog("INFO | Protokoll gespeichert: " + file);
                status.Text = "Protokoll gespeichert unter Dokumente\\WoWUpdaterLogs.";
            }));
            backupSize.AutoSize = true;
            backupSize.Margin = new Padding(12, 10, 0, 0);
            moreTools.Controls.Add(backupSize);
            layout.Controls.Add(toggle, 0, 5);
            layout.Controls.Add(moreTools, 0, 6);
            RefreshBackupSize();
        }
        string InstalledVersion(Addon addon) {
            string folder = addon.Folders.FirstOrDefault(f => Directory.Exists(Path.Combine(AddonsRoot, f)));
            return folder == null ? "—" : LocalFiles.Version(Path.Combine(AddonsRoot, folder));
        }
        void ReceiveVersions(Addon addon, string encoded) {
            try {
                var versions = (IEnumerable)json.DeserializeObject(Encoding.UTF8.GetString(Convert.FromBase64String(encoded)));
                var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (Dictionary<string, object> item in versions) values[(string)item["folder"]] = (string)item["version"];
                string main = addon.Folders.FirstOrDefault(f => values.ContainsKey(f));
                string available = main != null ? values[main] : "Nicht angegeben";
                string tooltip = string.Join("\r\n", values.Select(v => v.Key + ": " + v.Value));
                OnUI(delegate { addon.Row.SubItems[4].Text = available; tooltips[addon] = tooltip; addon.Row.ToolTipText = L.T(tooltip); });
            } catch (Exception ex) { OnUI(delegate { AppendLog("WARNUNG | Versionsangabe nicht lesbar: " + ex.Message); }); }
        }
        static string FormatSize(long bytes) { return (bytes / 1048576.0).ToString("N1") + " MB"; }
        void RefreshBackupSize() {
            try { backupSize.Text = "Backups: " + FormatSize(LocalFiles.Size(BackupRoot)); }
            catch { backupSize.Text = "Backups: Zugriff nicht möglich"; }
        }
        bool GameIsRunning() {
            var processes = Process.GetProcessesByName("Wow");
            try { return processes.Length > 0; } finally { foreach (var p in processes) p.Dispose(); }
        }
        void DeleteBackups() {
            if (busy) return;
            long bytes = LocalFiles.Size(BackupRoot);
            if (!Directory.Exists(BackupRoot)) { status.Text = "Keine Backups vorhanden."; return; }
            if (L.Show(this, "Alle Updater-Sicherungen löschen (" + FormatSize(bytes) + ")?\r\n\r\n" + BackupRoot + "\r\n\r\nDas lässt sich nicht rückgängig machen. Installierte Addons bleiben erhalten.", "Backups endgültig löschen", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            LocalFiles.DeleteOwned(BackupRoot, Path.Combine(gameRoot, "Interface"), "AddOns_UpdaterBackups");
            RefreshBackupSize();
            AppendLog("INFO | Alle Updater-Backups endgültig gelöscht: " + FormatSize(bytes));
        }
        void ShowBackups() {
            if (busy) return;
            LocalFiles.ValidateTree(BackupRoot);
            using (var dialog = new Form { Text = L.T("Backup wiederherstellen"), BackColor = surface, ForeColor = textColor, Font = Font, ClientSize = new Size(900, 500), StartPosition = FormStartPosition.CenterParent }) {
                var table = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false, BackColor = Color.FromArgb(40, 40, 40), ForeColor = textColor, HideSelection = false };
                table.Columns.Add(L.T("Sicherung / Zeitpunkt"), 390); table.Columns.Add(L.T("Addon-Ordner"), 200); table.Columns.Add("Version", 150); table.Columns.Add(L.T("Größe"), 110);
                if (Directory.Exists(BackupRoot)) foreach (string run in Directory.GetDirectories(BackupRoot).OrderByDescending(p => p)) {
                    foreach (string folder in Directory.GetDirectories(run)) {
                        string name = Path.GetFileName(folder);
                        if (!File.Exists(Path.Combine(folder, name + ".toc"))) continue;
                        var row = new ListViewItem(new[] { Path.GetFileName(run), name, L.T(LocalFiles.Version(folder)), FormatSize(LocalFiles.Size(folder)) });
                        row.Tag = folder; table.Items.Add(row);
                    }
                }
                var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 60, Padding = new Padding(8) };
                var restore = ExtraButton("Auswahl wiederherstellen", 260, delegate {
                    if (table.SelectedItems.Count != 1) return;
                    if (GameIsRunning()) { L.Show(dialog, "Bitte WoW zuerst schließen."); return; }
                    string saved = (string)table.SelectedItems[0].Tag;
                    if (L.Show(dialog, "Sicherung für " + Path.GetFileName(saved) + " wiederherstellen?\r\nDer aktuelle Addon-Ordner wird vorher gesichert. WTF-Einstellungen bleiben unverändert.", "Wiederherstellen", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                    string previous = LocalFiles.Restore(BackupRoot, saved, AddonsRoot);
                    AppendLog("INFO | Wiederhergestellt: " + saved + (previous == null ? "" : "; vorheriger Stand gesichert: " + previous));
                    ScanInstalled(); RefreshBackupSize(); dialog.Close();
                });
                restore.Enabled = false;
                table.SelectedIndexChanged += delegate { restore.Enabled = table.SelectedItems.Count == 1; };
                bottom.Controls.Add(restore);
                bottom.Controls.Add(new Label { Text = L.T(table.Items.Count + " gesicherte Addon-Stände"), AutoSize = true, Margin = new Padding(12) });
                dialog.Controls.Add(table); dialog.Controls.Add(bottom);
                dialog.ShowDialog(this);
            }
        }
        void LaunchWow() {
            if (busy) return;
            try {
                string exe = Path.Combine(gameRoot, "Wow.exe");
                if (!File.Exists(exe)) throw new FileNotFoundException("Wow.exe wurde nicht neben dem Updater gefunden.");
                if (GameIsRunning()) { L.Show(this, "WoW läuft bereits."); return; }
                foreach (string folder in Directory.GetDirectories(tempRoot, "packages-*")) LocalFiles.DeleteOwned(folder, tempRoot, Path.GetFileName(folder));
                string cacheParent = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WoWAddonUpdater");
                LocalFiles.DeleteOwned(Path.Combine(cacheParent, "Downloads"), cacheParent, "Downloads");
                // Erst nach erfolgreicher Startanforderung schließen, damit Startfehler sichtbar bleiben.
                GameLauncher.Start(exe, gameRoot);
                Close();
            } catch (Exception ex) { ScanInstalled(); AppendLog("FEHLER | WoW-Start: " + ex.Message); L.Show(this, ex.Message, "WoW konnte nicht gestartet werden"); }
        }
        void TestExtras() {
            string test = Path.Combine(Environment.CurrentDirectory, "work", "wut-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            string addonRoot = Path.Combine(test, "Interface", "AddOns");
            string backups = Path.Combine(test, "Interface", "AddOns_UpdaterBackups");
            string saved = Path.Combine(backups, "2026-01-01_12-00-00", "TestAddon");
            string current = Path.Combine(addonRoot, "TestAddon");
            Directory.CreateDirectory(saved); Directory.CreateDirectory(current);
            File.WriteAllText(Path.Combine(saved, "TestAddon.toc"), "## Version: 1.0");
            File.WriteAllText(Path.Combine(current, "TestAddon.toc"), "## Version: 2.0");
            string previous = LocalFiles.Restore(backups, saved, addonRoot);
            if (LocalFiles.Version(current) != "1.0" || LocalFiles.Version(Path.Combine(previous, "TestAddon")) != "2.0") throw new Exception("Wiederherstellung/Sicherung fehlerhaft.");
            string exported = LocalFiles.SaveLog(Path.Combine(test, "Documents"), "Testprotokoll äöü");
            if (File.ReadAllText(exported) != "Testprotokoll äöü") throw new Exception("Protokollexport fehlerhaft.");
            bool rejected = false;
            try { LocalFiles.DeleteOwned(test, test, "falsch"); } catch (IOException) { rejected = true; }
            if (!rejected) throw new Exception("Unsicherer Löschpfad erlaubt.");
            if (LocalFiles.Size(backups) == 0) throw new Exception("Backupgröße fehlt.");
            LocalFiles.DeleteOwned(backups, Path.Combine(test, "Interface"), "AddOns_UpdaterBackups");
            if (Directory.Exists(backups) || !Directory.Exists(current)) throw new Exception("Backuplöschung verändert Addons.");
            var tomtom = addons.Single(a => a.Name == "TomTom");
            ReceiveVersions(tomtom, Convert.ToBase64String(Encoding.UTF8.GetBytes("[{\"folder\":\"TomTom\",\"version\":\"v1.2\"}]")));
            Application.DoEvents();
            if (tomtom.Row.SubItems[4].Text != "v1.2") throw new Exception("Versionsspalte fehlerhaft.");
            LocalFiles.DeleteOwned(test, Path.GetDirectoryName(test), Path.GetFileName(test));
        }
    }
}
