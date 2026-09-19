// Instalator / deinstalator programu "Echo Sync"
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MojSyncSetup
{
    static class Setup
    {
        public const string AppName = "Echo Sync";
        public const string ExeName = "EchoSync.exe";
        const string OldExeName = "Synchronizator.exe";              // wcześniejsza wersja ("Mój Synchronizator folderów")
        const string OldLnkName = "Mój Synchronizator folderów.lnk";
        public const string UninstName = "Odinstaluj.exe";
        // klucze rejestru zostają te same – aktualizacja zastępuje poprzednią instalację
        const string UninstKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\MojSynchronizator";
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunName = "MojSynchronizator";

        static string StartMenuLnk { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName + ".lnk"); } }
        static string DesktopLnk { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), AppName + ".lnk"); } }

        static void DeleteOldShortcuts()
        {
            foreach (var f in new[] {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), OldLnkName),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), OldLnkName) })
                try { if (File.Exists(f)) File.Delete(f); } catch { }
        }

        // folder istniejącej instalacji (żeby aktualizacja trafiła w to samo miejsce i zachowała zadania)
        public static string ExistingInstallDir()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(UninstKey))
                {
                    var v = k == null ? null : k.GetValue("InstallLocation") as string;
                    if (!string.IsNullOrEmpty(v) && Directory.Exists(v)) return v;
                }
            }
            catch { }
            return null;
        }

        public static Icon AppIcon()
        {
            try
            {
                using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("app.ico"))
                    if (s != null) return new Icon(s);
            }
            catch { }
            return SystemIcons.Application;
        }

        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args.Any(a => a.Equals("/uninstall", StringComparison.OrdinalIgnoreCase))) Uninstall();
            else Application.Run(new SetupForm());
        }

        static void StopRunning(string dir)
        {
            foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(ExeName))
                                     .Concat(Process.GetProcessesByName(Path.GetFileNameWithoutExtension(OldExeName))))
            {
                try
                {
                    if (string.Equals(Path.GetDirectoryName(p.MainModule.FileName), dir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                    {
                        p.Kill();
                        p.WaitForExit(5000);
                    }
                }
                catch { }
            }
        }

        static void Shortcut(string lnk, string target, string args)
        {
            Type t = Type.GetTypeFromProgID("WScript.Shell");
            dynamic sh = Activator.CreateInstance(t);
            dynamic s = sh.CreateShortcut(lnk);
            s.TargetPath = target;
            s.Arguments = args;
            s.WorkingDirectory = Path.GetDirectoryName(target);
            s.IconLocation = target + ",0";
            s.Description = AppName;
            s.Save();
        }

        public static void Install(string dir, bool desktop, bool autostart, bool launch)
        {
            dir = Path.GetFullPath(dir.Trim());
            Directory.CreateDirectory(dir);
            StopRunning(dir);

            string exe = Path.Combine(dir, ExeName);
            using (var res = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.exe"))
            using (var fs = File.Create(exe))
                res.CopyTo(fs);
            // konfiguracja: obsługa długich ścieżek (> 260 znaków)
            using (var res = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.config"))
                if (res != null)
                    using (var fs = File.Create(exe + ".config"))
                        res.CopyTo(fs);
            try { string old = Path.Combine(dir, OldExeName); if (File.Exists(old)) File.Delete(old); } catch { }
            DeleteOldShortcuts();

            string self = Assembly.GetExecutingAssembly().Location;
            string uninst = Path.Combine(dir, UninstName);
            if (!string.Equals(self, uninst, StringComparison.OrdinalIgnoreCase))
                File.Copy(self, uninst, true);

            // przeniesienie istniejących zadań leżących obok instalatora
            string srcCfg = Path.Combine(Path.GetDirectoryName(self), "zadania.xml");
            string dstCfg = Path.Combine(dir, "zadania.xml");
            if (File.Exists(srcCfg) && !File.Exists(dstCfg)) File.Copy(srcCfg, dstCfg);

            Shortcut(StartMenuLnk, exe, "");
            if (desktop) Shortcut(DesktopLnk, exe, "");
            else if (File.Exists(DesktopLnk)) File.Delete(DesktopLnk);

            using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (autostart) k.SetValue(RunName, "\"" + exe + "\" /tray");
                else if (k.GetValue(RunName) != null) k.DeleteValue(RunName);
            }

            using (var k = Registry.CurrentUser.CreateSubKey(UninstKey))
            {
                k.SetValue("DisplayName", AppName);
                k.SetValue("DisplayIcon", exe + ",0");
                k.SetValue("DisplayVersion", "2.0");
                k.SetValue("Publisher", Environment.UserName);
                k.SetValue("InstallLocation", dir);
                k.SetValue("UninstallString", "\"" + uninst + "\" /uninstall");
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                long kb = new FileInfo(exe).Length / 1024 + new FileInfo(uninst).Length / 1024;
                k.SetValue("EstimatedSize", (int)kb, RegistryValueKind.DWord);
            }

            if (launch) Process.Start(exe);
        }

        static void Uninstall()
        {
            string dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (!File.Exists(Path.Combine(dir, ExeName)) && !File.Exists(Path.Combine(dir, OldExeName)))
            {
                MessageBox.Show("Nie znaleziono zainstalowanego programu w: " + dir, AppName);
                return;
            }
            if (MessageBox.Show("Odinstalować „" + AppName + "”?\n\nSynchronizowane foldery i pliki NIE zostaną ruszone.", AppName,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            bool keepCfg = File.Exists(Path.Combine(dir, "zadania.xml")) &&
                           MessageBox.Show("Zachować listę zadań i dzienniki (zadania.xml, logi)?", AppName,
                               MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

            StopRunning(dir);
            try { if (File.Exists(StartMenuLnk)) File.Delete(StartMenuLnk); } catch { }
            try { if (File.Exists(DesktopLnk)) File.Delete(DesktopLnk); } catch { }
            DeleteOldShortcuts();
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                    if (k != null && k.GetValue(RunName) != null) k.DeleteValue(RunName);
                Registry.CurrentUser.DeleteSubKeyTree(UninstKey, false);
            }
            catch { }

            try { File.Delete(Path.Combine(dir, ExeName)); } catch { }
            try { File.Delete(Path.Combine(dir, OldExeName)); } catch { }
            try { File.Delete(Path.Combine(dir, ExeName + ".config")); } catch { }
            string cmd = keepCfg
                ? "/c timeout /t 2 /nobreak >nul & del /f /q \"" + Path.Combine(dir, UninstName) + "\""
                : "/c timeout /t 2 /nobreak >nul & rmdir /s /q \"" + dir + "\"";
            MessageBox.Show("Program został odinstalowany." + (keepCfg ? "\nUstawienia zostały w: " + dir : ""), AppName);
            // usunięcie folderu po zamknięciu tego procesu
            Process.Start(new ProcessStartInfo("cmd.exe", cmd) { WindowStyle = ProcessWindowStyle.Hidden, CreateNoWindow = true, WorkingDirectory = Path.GetTempPath() });
        }
    }

    class SetupForm : Form
    {
        TextBox txtDir;
        CheckBox chkDesktop, chkAuto, chkLaunch;

        public SetupForm()
        {
            Text = "Instalacja – " + Setup.AppName;
            Icon = Setup.AppIcon();
            Font = new Font("Segoe UI", 9.5f);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(560, 300);

            var pic = new PictureBox { Image = new Icon(Icon, 48, 48).ToBitmap(), Location = new Point(20, 18), Size = new Size(48, 48) };
            var title = new Label { Text = Setup.AppName, Font = new Font("Segoe UI", 14f, FontStyle.Bold), Location = new Point(80, 18), AutoSize = true };
            var sub = new Label { Text = "Synchronizacja i kopie lustrzane folderów (także dyski sieciowe)", Location = new Point(82, 50), AutoSize = true, ForeColor = Color.DimGray };

            var lbl = new Label { Text = "Folder instalacji:", Location = new Point(20, 92), AutoSize = true };
            txtDir = new TextBox
            {
                Location = new Point(20, 114), Width = 430,
                Text = Setup.ExistingInstallDir() ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Echo Sync")
            };
            if (Setup.ExistingInstallDir() != null) Text = "Aktualizacja – " + Setup.AppName;
            var browse = new Button { Text = "Zmień...", Location = new Point(460, 112), Width = 80, Height = 28 };
            browse.Click += delegate
            {
                using (var d = new FolderBrowserDialog { SelectedPath = txtDir.Text })
                    if (d.ShowDialog(this) == DialogResult.OK) txtDir.Text = Path.Combine(d.SelectedPath, "Echo Sync");
            };

            chkDesktop = new CheckBox { Text = "Skrót na pulpicie", Location = new Point(20, 154), AutoSize = true, Checked = true };
            chkAuto = new CheckBox { Text = "Uruchamiaj z Windows (ikona w tray, automatyczne synchronizacje)", Location = new Point(20, 180), AutoSize = true, Checked = true };
            chkLaunch = new CheckBox { Text = "Uruchom program po instalacji", Location = new Point(20, 206), AutoSize = true, Checked = true };

            var btnInstall = new Button { Text = "Instaluj", Location = new Point(350, 252), Width = 95, Height = 32, Font = new Font(Font, FontStyle.Bold) };
            var btnCancel = new Button { Text = "Anuluj", Location = new Point(450, 252), Width = 90, Height = 32 };
            btnCancel.Click += delegate { Close(); };
            btnInstall.Click += delegate
            {
                try
                {
                    Cursor = Cursors.WaitCursor;
                    Setup.Install(txtDir.Text, chkDesktop.Checked, chkAuto.Checked, chkLaunch.Checked);
                    Cursor = Cursors.Default;
                    MessageBox.Show(this, "Instalacja zakończona.\n\nOdinstalujesz program z: Ustawienia → Aplikacje → Zainstalowane aplikacje.",
                        Setup.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    Close();
                }
                catch (Exception ex)
                {
                    Cursor = Cursors.Default;
                    MessageBox.Show(this, "Błąd instalacji:\n" + ex.Message, Setup.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };
            AcceptButton = btnInstall;
            CancelButton = btnCancel;

            Controls.AddRange(new Control[] { pic, title, sub, lbl, txtDir, browse, chkDesktop, chkAuto, chkLaunch, btnInstall, btnCancel });
        }
    }
}
