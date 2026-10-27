using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace WowUpdater {
    sealed class NoPublicReleaseException : Exception {
        public NoPublicReleaseException() : base("Noch kein öffentliches Release verfügbar (oder Repository nicht erreichbar).") { }
    }
    sealed class ReleaseInfo {
        public Version Version;
        public string Tag, Url, Hash;
        public long Size;
    }
    static class SelfUpdate {
        public const string Repository = "theprinceofbucket/WoW-Addon-Updater";
        public const string AssetName = "WoW-Addon-Updater.exe";
        const long MaxSize = 100 * 1024 * 1024;
        public static Version ParseVersion(string text) {
            if (!Regex.IsMatch(text ?? "", @"^v?\d+\.\d+\.\d+$")) throw new InvalidDataException("Ungültige Release-Version.");
            return new Version(text.TrimStart('v'));
        }
        public static ReleaseInfo ParseRelease(string json) {
            var data = new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(json);
            if ((bool)data["draft"] || (bool)data["prerelease"]) return null;
            string tag = (string)data["tag_name"];
            Version version = ParseVersion(tag);
            if (version <= ParseVersion(AppVersion.Current)) return null;
            var assets = ((IEnumerable)data["assets"]).Cast<Dictionary<string,object>>().Where(a => (string)a["name"] == AssetName).ToArray();
            if (assets.Length != 1) throw new InvalidDataException("Das Release enthält keine eindeutige Updater-EXE.");
            var asset = assets[0];
            string url = (string)asset["browser_download_url"];
            var uri = new Uri(url);
            string expected = "/" + Repository + "/releases/download/" + tag + "/" + AssetName;
            if (uri.Scheme != "https" || uri.Host != "github.com" || uri.AbsolutePath != expected || uri.Query != "" || uri.UserInfo != "" || !uri.IsDefaultPort)
                throw new InvalidDataException("Die Updateadresse gehört nicht zum vorgesehenen Repository.");
            string digest = asset.ContainsKey("digest") ? asset["digest"] as string : null;
            if (!Regex.IsMatch(digest ?? "", "^sha256:[0-9a-fA-F]{64}$")) throw new InvalidDataException("GitHub liefert keine gültige SHA-256-Prüfsumme. Das Update wird nicht installiert.");
            long size = Convert.ToInt64(asset["size"]);
            if (size < 1024 || size > MaxSize) throw new InvalidDataException("Ungültige Größe der Updater-EXE.");
            return new ReleaseInfo { Version = version, Tag = tag, Url = url, Hash = digest.Substring(7), Size = size };
        }
        static bool Allowed(Uri uri) {
            return uri.Scheme == "https" && uri.IsDefaultPort && uri.UserInfo == "" &&
                new[] { "api.github.com", "github.com", "release-assets.githubusercontent.com", "objects.githubusercontent.com" }.Contains(uri.Host);
        }
        static byte[] Get(string url, long maximum, CancellationToken cancel) {
            Uri uri = new Uri(url);
            for (int redirect = 0; redirect < 6; redirect++) {
                cancel.ThrowIfCancellationRequested();
                if (!Allowed(uri)) throw new InvalidDataException("Nicht erlaubte Update-Weiterleitung.");
                var request = (HttpWebRequest)WebRequest.Create(uri);
                request.AllowAutoRedirect = false;
                request.UserAgent = "WoW-Addon-Updater/" + AppVersion.Current;
                request.Timeout = request.ReadWriteTimeout = 30000;
                request.Accept = uri.Host == "api.github.com" ? "application/vnd.github+json" : "application/octet-stream";
                using (cancel.Register(request.Abort)) {
                    try {
                        using (var response = (HttpWebResponse)request.GetResponse()) {
                            if ((int)response.StatusCode >= 300 && (int)response.StatusCode < 400) {
                                uri = new Uri(uri, response.Headers["Location"]);
                                continue;
                            }
                            if (response.ContentLength > maximum) throw new InvalidDataException("Update-Download ist zu groß.");
                            using (var source = response.GetResponseStream())
                            using (var output = new MemoryStream()) {
                                byte[] buffer = new byte[65536];
                                int count;
                                while ((count = source.Read(buffer, 0, buffer.Length)) > 0) {
                                    cancel.ThrowIfCancellationRequested();
                                    if (output.Length + count > maximum) throw new InvalidDataException("Update-Download ist zu groß.");
                                    output.Write(buffer, 0, count);
                                }
                                return output.ToArray();
                            }
                        }
                    } catch (WebException) { cancel.ThrowIfCancellationRequested(); throw; }
                }
            }
            throw new InvalidDataException("Zu viele Update-Weiterleitungen.");
        }
        public static ReleaseInfo Check(CancellationToken cancel) {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            try {
                return ParseRelease(Encoding.UTF8.GetString(Get("https://api.github.com/repos/" + Repository + "/releases/latest", 2 * 1024 * 1024, cancel)));
            } catch (WebException ex) {
                var response = ex.Response as HttpWebResponse;
                if (response != null) {
                    var status = response.StatusCode;
                    response.Dispose();
                    if (status == HttpStatusCode.NotFound) throw new NoPublicReleaseException();
                    if (status == HttpStatusCode.Forbidden || (int)status == 429) throw new InvalidOperationException("GitHub-Anfragelimit erreicht. Bitte später erneut versuchen.");
                }
                throw;
            }
        }
        public static string Hash(string file) {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(file)) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
        public static void ValidateCandidate(string file, string hash, Version version) {
            if (!Regex.IsMatch(hash ?? "", "^[0-9a-fA-F]{64}$") || !string.Equals(Hash(file), hash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("SHA-256-Prüfung fehlgeschlagen.");
            var info = FileVersionInfo.GetVersionInfo(file);
            if (info.ProductName != "WoW Addon Updater für WoW 3.3.5a" || ParseVersion(info.ProductVersion) != version)
                throw new InvalidDataException("Die EXE passt nicht zum Updater oder zur Release-Version.");
        }
        public static string Stage(ReleaseInfo release, string executable, CancellationToken cancel) {
            string root = Path.GetDirectoryName(executable);
            string stage = Path.Combine(root, ".wow-updater-self-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stage);
            try {
                LocalFiles.ValidateTree(stage);
                byte[] bytes = Get(release.Url, release.Size, cancel);
                if (bytes.LongLength != release.Size) throw new InvalidDataException("Update-Download ist unvollständig.");
                string candidate = Path.Combine(stage, "new.exe");
                File.WriteAllBytes(candidate, bytes);
                ValidateCandidate(candidate, release.Hash, release.Version);
                File.Copy(executable, Path.Combine(stage, "helper.exe"), false);
                return stage;
            } catch { LocalFiles.DeleteOwned(stage, root, Path.GetFileName(stage)); throw; }
        }
        static string Quote(string value) { return "\"" + value + "\""; }
        public static void ValidateStage(string stage, string target) {
            string full = Path.GetFullPath(stage).TrimEnd('\\');
            if (!Regex.IsMatch(Path.GetFileName(full), @"^\.wow-updater-self-[0-9a-f]{32}$") ||
                !string.Equals(Path.GetDirectoryName(full), Path.GetDirectoryName(Path.GetFullPath(target)), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Ungültiger Self-Update-Ordner.");
            LocalFiles.ValidateTree(full);
            LocalFiles.ValidateTree(target);
            if (Directory.GetDirectories(full).Length != 0 || Directory.GetFiles(full).Any(f => !new[] { "new.exe", "helper.exe", "old.exe", "ready", "abort", "cleanup.ok", "error.txt" }.Contains(Path.GetFileName(f))))
                throw new InvalidDataException("Unerwartete Dateien im Self-Update-Ordner.");
        }
        public static async Task BeginSwap(string stage, string executable, ReleaseInfo release) {
            using (var current = Process.GetCurrentProcess()) {
                string args = "--apply-update " + current.Id + " " + current.StartTime.ToUniversalTime().Ticks + " " +
                    Quote(executable) + " " + release.Hash + " " + release.Version + " " + (L.English ? "en" : "de");
                using (var helper = Process.Start(new ProcessStartInfo(Path.Combine(stage, "helper.exe"), args) { UseShellExecute = false, WorkingDirectory = stage })) {
                    for (int i = 0; i < 100; i++) {
                        if (File.Exists(Path.Combine(stage, "ready"))) return;
                        if (helper.HasExited) { File.WriteAllText(Path.Combine(stage, "abort"), "abort"); throw new IOException("Der Update-Helfer konnte nicht gestartet werden."); }
                        await Task.Delay(100);
                    }
                    // Helper only replaces the EXE after the original process exits.
                    File.WriteAllText(Path.Combine(stage, "abort"), "abort");
                    throw new IOException("Der Update-Helfer antwortet nicht. Das Update wurde abgebrochen.");
                }
            }
        }
        public static int Apply(string[] args) {
            string stage = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
            string target = args.Length == 7 ? Path.GetFullPath(args[3]) : null;
            bool replaced = false;
            bool validStage = false;
            try {
                if (args.Length != 7) throw new ArgumentException("Ungültiger Update-Aufruf.");
                L.English = args[6] == "en";
                ValidateStage(stage, target);
                validStage = true;
                Version version = ParseVersion(args[5]);
                if (version <= ParseVersion(AppVersion.Current)) throw new InvalidDataException("Ein Downgrade wird nicht durchgeführt.");
                if (Hash(target) != Hash(Path.Combine(stage, "helper.exe"))) throw new InvalidDataException("Die bisherige EXE wurde zwischenzeitlich verändert.");
                ValidateCandidate(Path.Combine(stage, "new.exe"), args[4], version);
                using (var parent = Process.GetProcessById(int.Parse(args[1]))) {
                    if (parent.StartTime.ToUniversalTime().Ticks != long.Parse(args[2])) throw new InvalidDataException("Der ursprüngliche Updater-Prozess stimmt nicht überein.");
                    File.WriteAllText(Path.Combine(stage, "ready"), "ready");
                    var timer = Stopwatch.StartNew();
                    while (!parent.WaitForExit(100)) {
                        if (File.Exists(Path.Combine(stage, "abort"))) return 1;
                        if (timer.Elapsed.TotalSeconds > 120) throw new IOException("Der bisherige Updater wurde nicht beendet; kein Austausch durchgeführt.");
                    }
                }
                if (File.Exists(Path.Combine(stage, "abort"))) return 1;
                ValidateCandidate(Path.Combine(stage, "new.exe"), args[4], version);
                File.Replace(Path.Combine(stage, "new.exe"), target, Path.Combine(stage, "old.exe"), true);
                replaced = true;
                File.WriteAllText(Path.Combine(stage, "cleanup.ok"), target);
                using (var current = Process.GetCurrentProcess())
                using (var next = Process.Start(new ProcessStartInfo(target, "--finish-update " + Quote(stage) + " " + current.Id) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(target) })) { }
                return 0;
            } catch (Exception ex) {
                if (replaced) {
                    try { File.Replace(Path.Combine(stage, "old.exe"), target, null, true); }
                    catch (Exception rollback) { ex = new IOException(ex.Message + "\r\nRollback: " + rollback.Message, ex); }
                }
                if (validStage) try { File.WriteAllText(Path.Combine(stage, "error.txt"), ex.ToString()); } catch { }
                L.Show("Self-Update fehlgeschlagen. Die bisherige Version bleibt erhalten, sofern die Wiederherstellung möglich war.\r\n" + ex.Message, "Updater-Update", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }
        public static void Cleanup(string stage, string executable, int helperPid) {
            ValidateStage(stage, executable);
            if (File.ReadAllText(Path.Combine(stage, "cleanup.ok")) != executable) throw new IOException("Ungültige Update-Bereinigung.");
            try { using (var helper = Process.GetProcessById(helperPid)) { if (!helper.WaitForExit(15000)) return; } } catch (ArgumentException) { }
            LocalFiles.DeleteOwned(stage, Path.GetDirectoryName(executable), Path.GetFileName(stage));
        }
        internal static void TestMetadata() {
            var asset = new Dictionary<string,object> {
                { "name", AssetName }, { "browser_download_url", "https://github.com/" + Repository + "/releases/download/v0.5.2/" + AssetName },
                { "size", 50000 }, { "digest", "sha256:" + new string('a', 64) }
            };
            var data = new Dictionary<string,object> { { "tag_name", "v0.5.2" }, { "draft", false }, { "prerelease", false }, { "assets", new[] { asset } } };
            var json = new JavaScriptSerializer();
            if (ParseRelease(json.Serialize(data)).Version != new Version("0.5.2")) throw new Exception("New release not detected.");
            data["tag_name"] = "v" + AppVersion.Current;
            if (ParseRelease(json.Serialize(data)) != null) throw new Exception("Same version offered as update.");
            data["tag_name"] = "v0.4.0";
            if (ParseRelease(json.Serialize(data)) != null) throw new Exception("Downgrade offered.");
            data["tag_name"] = "v0.5.2"; data["prerelease"] = true;
            if (ParseRelease(json.Serialize(data)) != null) throw new Exception("Prerelease offered.");
            data["prerelease"] = false; data["draft"] = true;
            if (ParseRelease(json.Serialize(data)) != null) throw new Exception("Draft offered.");
            data["draft"] = false;
            foreach (string key in new[] { "browser_download_url", "digest", "size", "name" }) {
                object old = asset[key];
                asset[key] = key == "size" ? (object)(MaxSize + 1) : "invalid";
                bool rejected = false;
                try { ParseRelease(json.Serialize(data)); } catch { rejected = true; }
                if (!rejected) throw new Exception("Invalid release field accepted: " + key);
                asset[key] = old;
            }
            ValidateCandidate(Application.ExecutablePath, Hash(Application.ExecutablePath), ParseVersion(AppVersion.Current));
            bool badHash = false;
            try { ValidateCandidate(Application.ExecutablePath, new string('0', 64), ParseVersion(AppVersion.Current)); } catch (InvalidDataException) { badHash = true; }
            if (!badHash) throw new Exception("Invalid executable hash accepted.");
            string testRoot = Path.Combine(Environment.CurrentDirectory, "work", "cleanup-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testRoot);
            string target = Path.Combine(testRoot, AssetName);
            File.WriteAllText(target, "keep");
            string stage = Path.Combine(testRoot, ".wow-updater-self-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stage);
            File.WriteAllText(Path.Combine(stage, "old.exe"), "previous");
            File.WriteAllText(Path.Combine(stage, "cleanup.ok"), target);
            Cleanup(stage, target, int.MaxValue);
            if (Directory.Exists(stage) || File.ReadAllText(target) != "keep") throw new Exception("Self-update cleanup failed.");
            LocalFiles.DeleteOwned(testRoot, Path.GetDirectoryName(testRoot), Path.GetFileName(testRoot));
        }
    }
    public partial class UpdaterForm {
        bool selfUpdating;
        CancellationTokenSource selfCancel;
        async void CheckSelfUpdate() {
            if (busy || selfUpdating) return;
            selfUpdating = true;
            selfCancel = new CancellationTokenSource();
            cancel.Enabled = true;
            check.Enabled = install.Enabled = play.Enabled = moreTools.Enabled = false;
            RefreshButtonColors();
            string stage = null;
            bool helperStarted = false;
            try {
                status.Text = "Suche nach Updater-Updates …";
                var release = await Task.Run(() => SelfUpdate.Check(selfCancel.Token));
                selfCancel.Token.ThrowIfCancellationRequested();
                if (release == null) { status.Text = "Der Updater ist aktuell."; return; }
                if (L.Show(this, "Neue Updater-Version: " + release.Version + "\r\n\r\nJetzt herunterladen und installieren? Der Updater wird dabei neu gestartet.",
                    "Updater-Update", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
                status.Text = "Updater-Update wird heruntergeladen und geprüft …";
                string executable = Application.ExecutablePath;
                stage = await Task.Run(() => SelfUpdate.Stage(release, executable, selfCancel.Token));
                selfCancel.Token.ThrowIfCancellationRequested();
                cancel.Enabled = false;
                RefreshButtonColors();
                status.Text = "Updater wird neu gestartet …";
                helperStarted = true;
                await SelfUpdate.BeginSwap(stage, executable, release);
                selfUpdating = false;
                Close();
            } catch (OperationCanceledException) { status.Text = "Updater-Update abgebrochen."; }
            catch (NoPublicReleaseException ex) {
                status.Text = ex.Message;
                AppendLog("INFO | " + ex.Message);
                L.Show(this, ex.Message, "Updater-Update", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex) {
                status.Text = "Updater-Update fehlgeschlagen.";
                AppendLog("FEHLER | " + ex.Message);
                L.Show(this, ex.Message + "\r\n\r\nFalls Schreibrechte fehlen, den Updater als Administrator starten.", "Updater-Update", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            } finally {
                // Ein gestarteter Helfer besitzt seine Dateien bis zum Abschluss.
                if (stage != null && !helperStarted) try { LocalFiles.DeleteOwned(stage, gameRoot, Path.GetFileName(stage)); } catch { }
                selfUpdating = false;
                selfCancel.Dispose(); selfCancel = null;
                if (!IsDisposed) {
                    cancel.Enabled = false;
                    check.Enabled = install.Enabled = play.Enabled = moreTools.Enabled = true;
                    RefreshButtonColors();
                }
            }
        }
    }
}
