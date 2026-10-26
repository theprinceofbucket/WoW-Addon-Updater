using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

namespace WowUpdater {
    static class GameLauncher {
        // Microsofts Execute-in-Explorer-Verfahren: Start im Prozess des Desktops.
        [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IServiceProvider {
            void QueryService(ref Guid service, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object result);
        }
        [ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellBrowser {
            void GetWindow(out IntPtr window); void ContextSensitiveHelp(bool enter);
            void InsertMenusSB(); void SetMenuSB(); void RemoveMenusSB();
            void SetStatusTextSB(); void EnableModelessSB(); void TranslateAcceleratorSB();
            void BrowseObject(); void GetViewStateStream(); void GetControlWindow(); void SendControlMsg();
            void QueryActiveShellView(out IShellView view);
        }
        [ComImport, Guid("000214E3-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellView {
            void GetWindow(out IntPtr window); void ContextSensitiveHelp(bool enter);
            void TranslateAccelerator(); void EnableModeless(); void UIActivate(); void Refresh();
            void CreateViewWindow(); void DestroyViewWindow(); void GetCurrentInfo();
            void AddPropertySheetPages(); void SaveViewState(); void SelectItem();
            void GetItemObject(uint item, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object result);
        }
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
        [DllImport("advapi32.dll", SetLastError = true)] static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
        [DllImport("advapi32.dll", SetLastError = true)] static extern bool GetTokenInformation(IntPtr token, int kind, out int value, int size, out int length);
        static bool Elevated() {
            IntPtr token;
            using (var process = Process.GetCurrentProcess()) {
                if (!OpenProcessToken(process.Handle, 8, out token)) throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            try {
                int value, length;
                if (!GetTokenInformation(token, 20, out value, sizeof(int), out length)) throw new Win32Exception(Marshal.GetLastWin32Error());
                return value != 0;
            } finally { CloseHandle(token); }
        }
        static object Call(object target, string name, BindingFlags flags, params object[] args) {
            return target.GetType().InvokeMember(name, flags, null, target, args);
        }
        internal static void StartViaDesktop(string executable, string directory) {
            object windows = null, desktop = null, browser = null, viewObject = null, background = null, application = null;
            string step = "Desktop suchen";
            try {
                windows = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39")));
                // SWC_DESKTOP = 8, SWFO_NEEDDISPATCH = 1.
                object[] args = { 0, 0, 8, 0, 1 };
                desktop = Call(windows, "FindWindowSW", BindingFlags.InvokeMethod, args);
                if (desktop == null) throw new InvalidOperationException("Der Windows-Desktop ist nicht verfügbar.");
                step = "Desktop-Ansicht öffnen";
                Guid service = new Guid("4C96BE40-915C-11CF-99D3-00AA004AE837");
                Guid browserId = typeof(IShellBrowser).GUID;
                ((IServiceProvider)desktop).QueryService(ref service, ref browserId, out browser);
                IShellView view;
                ((IShellBrowser)browser).QueryActiveShellView(out view);
                viewObject = view;
                Guid dispatch = new Guid("00020400-0000-0000-C000-000000000046");
                view.GetItemObject(0, ref dispatch, out background);
                application = Call(background, "Application", BindingFlags.GetProperty);
                step = "WoW-Start an Windows-Desktop übergeben";
                Call(application, "ShellExecute", BindingFlags.InvokeMethod, executable, "", directory, "open", 1);
            } catch (Exception ex) {
                Exception cause = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
                throw new InvalidOperationException("WoW-Start über den Windows-Desktop fehlgeschlagen (" + step + ", 0x" + cause.HResult.ToString("X8") + "): " + cause.Message + " Der Updater darf weiterhin als Administrator laufen. Bitte diese Meldung mit dem Protokoll weitergeben.", cause);
            } finally {
                foreach (object value in new[] { application, background, viewObject, browser, desktop, windows })
                    if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value);
            }
        }
        public static void Start(string executable, string directory) {
            if (Elevated()) StartViaDesktop(executable, directory);
            else using (var process = Process.Start(new ProcessStartInfo(executable) { WorkingDirectory = directory, UseShellExecute = false })) { }
        }
    }
}
