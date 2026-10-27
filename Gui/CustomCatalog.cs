using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace WowUpdater {
    public partial class UpdaterForm {
        readonly List<string> catalogWarnings = new List<string>();

        void LoadCustomCatalog() {
            string path = Path.Combine(gameRoot, "addons.custom.json");
            if (!File.Exists(path)) return;
            try {
                LocalFiles.ValidateTree(path);
                if (new FileInfo(path).Length > 1024 * 1024) throw new InvalidDataException("JSON > 1 MB");
                var document = json.Deserialize<Dictionary<string, object>>(File.ReadAllText(path));
                if (document == null || !document.ContainsKey("sources") || !(document["sources"] is IList))
                    throw new InvalidDataException("sources: JSON-Array erforderlich / JSON array required");
                var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (object host in (IEnumerable)hosts) allowed.Add((string)host);
                if (document.ContainsKey("trustedHosts")) {
                    if (!(document["trustedHosts"] is IList)) throw new InvalidDataException("trustedHosts: JSON array required");
                    foreach (object item in (IEnumerable)document["trustedHosts"]) {
                        string host = item as string;
                        if (string.IsNullOrWhiteSpace(host) || host != host.Trim() || Uri.CheckHostName(host) != UriHostNameType.Dns)
                            throw new InvalidDataException("trustedHosts: invalid hostname / ungültiger Hostname");
                        allowed.Add(host);
                    }
                }
                int index = 0;
                foreach (object item in (IEnumerable)document["sources"]) {
                    index++;
                    try {
                        var source = item as Dictionary<string, object>;
                        if (source == null) throw new InvalidDataException("JSON object required / Objekt erforderlich");
                        if (source.ContainsKey("enabled")) {
                            if (!(source["enabled"] is bool)) throw new InvalidDataException("enabled: true / false required");
                            if (!(bool)source["enabled"]) continue;
                        }
                        string name = RequiredText(source, "name");
                        string type = RequiredText(source, "type").ToLowerInvariant();
                        string address;
                        if (type == "github" || type == "gitlab") {
                            address = RequiredText(source, "repository");
                            RequiredText(source, "branch");
                        } else if (type == "zip" || type == "warperia") address = RequiredText(source, "url");
                        else throw new InvalidDataException("type: github, gitlab, zip, warperia");
                        Uri uri;
                        if (!Uri.TryCreate(address, UriKind.Absolute, out uri) || uri.Scheme != "https" || uri.UserInfo.Length > 0 || uri.Fragment.Length > 0)
                            throw new InvalidDataException("HTTPS URL required / HTTPS-Adresse erforderlich");
                        if (type == "github" && (uri.Host != "github.com" || uri.AbsolutePath.Trim('/').Split('/').Length != 2 || uri.Query.Length > 0))
                            throw new InvalidDataException("repository: https://github.com/owner/repository");
                        if (type == "gitlab" && (uri.Host != "gitlab.com" || uri.AbsolutePath.Trim('/').Split('/').Length < 2 || uri.Query.Length > 0))
                            throw new InvalidDataException("repository: https://gitlab.com/group/project");
                        if (type == "warperia" && (uri.Host != "warperia.com" || !Regex.IsMatch(uri.AbsolutePath, @"^/addon-wotlk/[a-z0-9-]+/download/$") || uri.Query.Length > 0))
                            throw new InvalidDataException("url: https://warperia.com/addon-wotlk/name/download/");
                        if ((type == "zip" || type == "warperia") && !allowed.Contains(uri.Host))
                            throw new InvalidDataException("trustedHosts: " + uri.Host);
                        if (source.ContainsKey("sha256") && !Regex.IsMatch(RequiredText(source, "sha256"), "^[a-fA-F0-9]{64}$"))
                            throw new InvalidDataException("sha256: 64 hexadecimal characters required");
                        object mapping;
                        if (!source.TryGetValue("addons", out mapping) || !(mapping is IList) || ((IList)mapping).Count == 0)
                            throw new InvalidDataException("addons: explicit folder array required / explizite Ordnerliste erforderlich (kein auto)");
                        var folderSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (object component in (IEnumerable)mapping) {
                            string folder = component as string;
                            if (folder == null) {
                                var spec = component as Dictionary<string, object>;
                                if (spec == null) throw new InvalidDataException("addons: folder or target/toc object required");
                                folder = RequiredText(spec, "target");
                                if (spec.ContainsKey("toc")) ValidateFolder(RequiredText(spec, "toc"));
                            }
                            ValidateFolder(folder);
                            if (!folderSet.Add(folder)) throw new InvalidDataException("Duplicate folder / Doppelter Ordner: " + folder);
                        }
                        if (addons.Any(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase) || a.Folders.Any(folderSet.Contains)))
                            throw new InvalidDataException("Name or folder already managed / Name oder Ordner bereits vorhanden: " + name);
                        if (addons.Any(a => a.Source.ContainsKey("repository") && string.Equals(((string)a.Source["repository"]).TrimEnd('/'), address.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)))
                            throw new InvalidDataException("Repository already managed / Repository bereits vorhanden: " + name);
                        addons.Add(new Addon { Source = source, Name = name, Folders = FolderNames(source), TrustedHosts = allowed.ToArray() });
                    } catch (Exception ex) {
                        catalogWarnings.Add("WARNUNG | addons.custom.json [" + index + "]: " + ex.Message);
                    }
                }
            } catch (Exception ex) {
                catalogWarnings.Add("WARNUNG | addons.custom.json: " + ex.Message);
            }
        }
        static string RequiredText(Dictionary<string, object> source, string key) {
            object value;
            if (!source.TryGetValue(key, out value) || !(value is string) || string.IsNullOrWhiteSpace((string)value))
                throw new InvalidDataException(key + ": text required / Text erforderlich");
            return (string)value;
        }
        static void ValidateFolder(string name) {
            if (string.IsNullOrWhiteSpace(name) || name.Contains("..") || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                name.EndsWith(".") || name.EndsWith(" ") || Regex.IsMatch(name, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])($|\.)", RegexOptions.IgnoreCase))
                throw new InvalidDataException("Invalid folder / Ungültiger Ordner: " + name);
        }

        void TestCustomCatalog() {
            string root = Path.Combine(tempRoot, "custom-tests");
            Directory.CreateDirectory(Path.Combine(root, "Interface", "AddOns", "FixtureAddon"));
            File.WriteAllText(Path.Combine(root, "Interface", "AddOns", "FixtureAddon", "FixtureAddon.toc"), "## Interface: 30300\n## Version: 1.2.3");
            string path = Path.Combine(root, "addons.custom.json");
            var valid = new Dictionary<string, object> {
                { "name", "AAA Fixture" }, { "type", "github" }, { "repository", "https://github.com/example/fixture" },
                { "branch", "main" }, { "addons", new[] { "FixtureAddon" } }
            };
            var collision = new Dictionary<string, object>(valid); collision["name"] = "Duplicate folder";
            var traversal = new Dictionary<string, object>(valid); traversal["addons"] = new[] { "../WTF" };
            var builtIn = new Dictionary<string, object>(valid); builtIn["name"] = "Questie 3.3.5a"; builtIn["addons"] = new[] { "OtherFolder" };
            var disabled = new Dictionary<string, object>(valid); disabled["enabled"] = false;
            var zip = new Dictionary<string, object> { { "name", "ZZZ ZIP" }, { "type", "zip" }, { "url", "https://downloads.example.org/addon.zip" }, { "addons", new[] { "ZipFixture" } } };
            var badUrl = new Dictionary<string, object>(zip); badUrl["url"] = "http://downloads.example.org/addon.zip";
            var auto = new Dictionary<string, object>(valid); auto["addons"] = "auto";
            var config = new Dictionary<string, object> {
                { "trustedHosts", new[] { "downloads.example.org" } },
                { "sources", new object[] { valid, collision, traversal, builtIn, disabled, zip, badUrl, auto, 42 } }
            };
            File.WriteAllText(path, json.Serialize(config));
            using (var form = new UpdaterForm(root)) {
                if (form.addons.Count != 24 || form.catalogWarnings.Count != 6) throw new Exception("Custom catalog validation failed.");
                var addon = form.addons.Single(a => a.Name == "AAA Fixture");
                if (!addon.Installed || !addon.Selected || addon.Row.SubItems[3].Text != "1.2.3") throw new Exception("Custom addon detection failed.");
                if (form.addons[0] != addon || form.addons.Last().Name != "ZZZ ZIP") throw new Exception("Custom sort failed.");
                if (!addon.TrustedHosts.Contains("downloads.example.org") || form.addons.Single(a => a.Name == "Questie 3.3.5a").TrustedHosts != null)
                    throw new Exception("Custom hosts leaked into built-in sources.");
            }
            foreach (string invalid in new[] { "{ invalid", "null", "{\"sources\":\"wrong\"}", "{\"trustedHosts\":[\"https://invalid\"],\"sources\":[]}" }) {
                File.WriteAllText(path, invalid);
                using (var form = new UpdaterForm(root)) {
                    if (form.addons.Count != 22 || form.catalogWarnings.Count != 1) throw new Exception("Malformed custom file broke built-in catalog.");
                }
            }
        }
    }
}
