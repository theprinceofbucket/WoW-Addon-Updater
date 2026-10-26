using System.Reflection;

[assembly: AssemblyTitle("WoW Addon Updater")]
[assembly: AssemblyProduct("WoW Addon Updater für WoW 3.3.5a")]
[assembly: AssemblyDescription("Addons prüfen, installieren und updaten")]
[assembly: AssemblyVersion("0.5.0.0")]
[assembly: AssemblyFileVersion("0.5.0.0")]
[assembly: AssemblyInformationalVersion("0.5.0")]

namespace WowUpdater {
    static class AppVersion {
        public static string Current {
            get {
                return ((AssemblyInformationalVersionAttribute)Assembly.GetExecutingAssembly()
                    .GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false)[0]).InformationalVersion;
            }
        }
    }
}
