// Echo Sync – synchronizator folderów
// Kompilacja: build.bat (używa csc.exe z .NET Framework 4.x, wbudowanego w Windows)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using System.Xml.Serialization;

namespace MojSync
{
    public partial class MainForm : Form
    {
        Config cfg;
        readonly Engine engine = new Engine();
        bool loading, busy;
        readonly bool autoMode;
        readonly string autoTaskName;
        int exitCode;
        StreamWriter logFile;

        ListBox lstTasks;
        TextBox txtName, txtLeft, txtRight, txtExcl;
        RichTextBox txtLog;
        ComboBox cmbDir, cmbDelete;
        CheckBox chkMirror, chkEnabled, chkInterval, chkWatch, chkAutomation, chkAutostart, chkWersje;
        ComboBox cmbWatchDelay;
        NumericUpDown numInterval;
        Button btnPreview, btnSync, btnRunAll, btnCancel, btnSave, btnDiscard;
        Label lblStatus, lblSummary, lblInfo, lblTarget, lblDirMode, lblStatusBar, lblSpeed;
        Label lblLewyTytul, lblPrawyTytul;   // "Folder LEWY/PRAWY" - dopisek ŹRÓDŁO/CEL zależny od kierunku
        DirectionArrow arrow;
        CheckBox chkSubFolder;
        SyncTask editing;   // zadanie wczytane do edytora
        bool dirty;         // edytor ma niezapisane zmiany
        ShimmerProgressBar progress;
        ListView lvActions;
        TextBox txtSearchTree;
        LinkLabel linkStalaDecyzja;   // "Stała decyzja: ... (kliknij, aby wycofać)" w wierszu Zapisz
        Label lblSearchInfo;
        TextBox txtSearchTasks;
        int taskSearchIndex = -1;
        bool taskSearchDirty = true;
        TabControl tabs;
        GroupBox grpEdit;
        // bajery wizualne: wirujace logo w naglowku podczas pracy, migotanie wiersza po auto-syncu
        GradientPanel headerPanel;
        System.Windows.Forms.Timer headerAnimTimer;
        float headerRot;
        readonly Dictionary<SyncTask, DateTime> flashUntil = new Dictionary<SyncTask, DateTime>();
        System.Windows.Forms.Timer flashTimer;

        // tray i automatyka
        NotifyIcon tray;
        ToolStripMenuItem miPause;
        bool reallyExit, startHidden, trayHintShown;
        readonly System.Windows.Forms.Timer scheduler = new System.Windows.Forms.Timer { Interval = 2000 };
        readonly Dictionary<SyncTask, TaskRuntime> runtimes = new Dictionary<SyncTask, TaskRuntime>();
        List<SyncTask> pending = new List<SyncTask>();
        List<SyncTask> running = new List<SyncTask>();
        // Zadania zgłoszone przez watcher (nie przez harmonogram/reczne uruchomienie) - tylko dla
        // nich wolno probowac szybkiego, przyrostowego skanu zamiast pelnego przeczesania drzewa.
        readonly HashSet<SyncTask> pendingFromWatch = new HashSet<SyncTask>();
        bool watchersDirty = true;
        SyncTask zadanieZDecyzja;      // zadanie z usunieciami czekajacymi na decyzje czlowieka
        Panel panelDecyzji;
        Label lblDecyzji;
        Button btnDecyduj, btnDecyzjeLista;
        DateTime lastEdit = DateTime.MinValue;

        class TaskRuntime
        {
            public SyncTask Task;
            public List<FileSystemWatcher> Watchers = new List<FileSystemWatcher>();
            public string WatchKey = "";
            public DateTime RetryAt = DateTime.MinValue;
            public DateTime NextRun = DateTime.MinValue;
            public DateTime LastEvent = DateTime.MinValue;
            public DateTime DirtySince = DateTime.MinValue;   // kiedy pojawiła się PIERWSZA nieprzesłana zmiana
            public DateTime IgnoreUntil = DateTime.MinValue;
            public DateTime ManualSyncAt = DateTime.MinValue;
            // Gdy inny program bez porządnego mechanizmu obserwacji zmian (np. własny cykliczny
            // zapis) w kółko cofa te same pliki, watcher reagowałby na to natychmiast za każdym
            // razem. Ta blokada czasowo wygasza reakcję na zmiany, żeby nie kopiować bez końca.
            public DateTime PingPongBackoffUntil = DateTime.MinValue;
            public string WatchLeft = "", WatchRight = "";
            // Wynik ostatniego pełnego skanu obu stron - jak w AllwaySync, pozwala przy kolejnej
            // reakcji watchera doczytać TYLKO zmienione foldery zamiast przeczesywać cale drzewo.
            public Dictionary<string, Entry> CachedL, CachedR;
            public readonly HashSet<string> DirtyTop = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public bool FullRescanNeeded = true;   // dopóki nie ma pewnego pełnego obrazu, cache jest niezaufany
            public string WatchFailMsg;            // ostatnia przyczyna nieudanej obserwacji (dziennik bez powtórek)
            public DateTime WatchFailLogAt;        // kiedy ostatnio zalogowano tę przyczynę
            public DateTime NextForcedFullScan = DateTime.MinValue;   // okresowa siatka bezpieczeństwa
            public bool Dirty, Running;
        }

        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunName = "MojSynchronizator";

        public static Icon AppIcon()
        {
            try
            {
                using (var s = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("app.ico"))
                    if (s != null) return new Icon(s);
            }
            catch { }
            return SystemIcons.Application;
        }

        public MainForm(bool auto, string taskName, bool hidden)
        {
            autoMode = auto;
            autoTaskName = taskName;
            startHidden = hidden;
            cfg = Config.Load();

            InitializeComponent();
            Text = "Echo Sync";
            // okno od razu tak duże, żeby wszystko było widać (ale nie większe niż ekran)
            var wa = Screen.PrimaryScreen.WorkingArea;
            Size = new Size(Math.Min(1380, wa.Width), Math.Min(880, wa.Height));
            MinimumSize = new Size(900, 640);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9f);
            var ico = AppIcon();
            Icon = ico;

            WireUi();
            EnsureLogTimer();
            BuildTray(ico);
            RefreshTaskList(0);
            FormClosing += OnClosing;
            Resize += delegate { if (WindowState == FormWindowState.Minimized && !autoMode) HideToTray(); };
            Shown += delegate
            {
                if (autoMode) { RunAuto(); return; }
                if (startHidden) HideToTray();
                scheduler.Tick += delegate { SchedulerTick(); };
                scheduler.Start();
                AppendLog("Program uruchomiony. Automatyka: " + (cfg.AutomationEnabled ? "włączona" : "wstrzymana"));
            };
        }

        // ---------- tray

        void BuildTray(Icon ico)
        {
            if (autoMode) return;
            var menu = new ContextMenuStrip();
            var miShow = new ToolStripMenuItem("Pokaż okno", null, delegate { ShowFromTray(); });
            miShow.Font = new Font(menu.Font, FontStyle.Bold);
            menu.Items.Add(miShow);
            menu.Items.Add(new ToolStripMenuItem("Kopiuj / Przenieś...", null, delegate { ShowFromTray(); OpenRobo(); }));
            menu.Items.Add(new ToolStripMenuItem("Synchronizuj wszystkie teraz", null, delegate
            {
                foreach (var t in cfg.Tasks.Where(x => x.Enabled && !x.Unsaved)) Enqueue(t, "ręcznie z tray");
            }));
            miPause = new ToolStripMenuItem("Wstrzymaj automatykę", null, delegate { chkAutomation.Checked = !chkAutomation.Checked; });
            menu.Items.Add(miPause);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Zamknij program", null, delegate { reallyExit = true; Close(); }));

            trayIdle = new Icon(ico, SystemInformation.SmallIconSize);
            tray = new NotifyIcon
            {
                Icon = trayIdle,
                Text = "Echo Sync",
                ContextMenuStrip = menu,
                Visible = true
            };
            tray.DoubleClick += delegate { ShowFromTray(); };
            tray.BalloonTipClicked += delegate
            {
                ShowFromTray();
                tabs.SelectedIndex = 1;
                // Jeśli czeka decyzja o usunięciach, kliknięcie powiadomienia prowadzi wprost do niej.
                // Migawka wciąż je pamięta, więc ręczny przebieg wykryje je jeszcze raz i zapyta.
                var z = zadanieZDecyzja;
                zadanieZDecyzja = null;
                if (z != null && cfg.Tasks.Contains(z) && !busy) StartWork(new List<SyncTask> { z }, true, true, false);
            };
        }

        // ---------- animowana ikona w tray

        Icon trayIdle;
        Icon[] animFrames;
        int animIndex;
        readonly System.Windows.Forms.Timer animTimer = new System.Windows.Forms.Timer { Interval = 60 };
        DateTime animStarted;
        bool animStopRequested;
        const int AnimMinMs = 3000;   // krótka synchronizacja też pokaże pełny obrót strzałek

        // klatka animacji: logo z obróconymi strzałkami (0..1 = pół obrotu – strzałki są symetryczne)
        static Icon DrawFrame(int sz, float phase)
        {
            using (var bmp = Logo.Draw(sz, phase * 180f, true))
                return Icon.FromHandle(bmp.GetHicon());
        }

        void StartTrayAnimation()
        {
            if (tray == null) return;
            animStopRequested = false;
            if (animTimer.Enabled) return;
            if (animFrames == null)
            {
                int sz = SystemInformation.SmallIconSize.Width;
                animFrames = Enumerable.Range(0, 24).Select(i => DrawFrame(sz, i / 24f)).ToArray();
                animTimer.Tick += delegate
                {
                    if (animStopRequested && (DateTime.Now - animStarted).TotalMilliseconds >= AnimMinMs)
                    {
                        animTimer.Stop();
                        animStopRequested = false;
                        if (trayIdle != null) tray.Icon = trayIdle;
                        return;
                    }
                    animIndex = (animIndex + 1) % animFrames.Length;
                    tray.Icon = animFrames[animIndex];
                };
            }
            animStarted = DateTime.Now;
            animTimer.Start();
        }

        void StopTrayAnimation()
        {
            // zatrzymanie nastąpi w Tick – po upływie minimalnego czasu animacji
            if (animTimer.Enabled) animStopRequested = true;
            else if (tray != null && trayIdle != null) tray.Icon = trayIdle;
        }

        void HideToTray()
        {
            if (tray == null) return;
            Hide();
            ShowInTaskbar = false;
        }

        void ShowFromTray()
        {
            ShowInTaskbar = true;
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }

        void TrayText(string s)
        {
            if (tray == null) return;
            s = "Echo Sync – " + s;
            tray.Text = s.Length > 63 ? s.Substring(0, 60) + "..." : s;
        }

        static bool AutostartEnabled()
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey))
                    return k != null && k.GetValue(RunName) != null;
            }
            catch { return false; }
        }

        static void SetAutostart(bool on)
        {
            using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (on) k.SetValue(RunName, "\"" + Application.ExecutablePath + "\" /tray");
                else if (k.GetValue(RunName) != null) k.DeleteValue(RunName);
            }
        }

        // ---------- automatyka: interwał + wykrywanie zmian

        TaskRuntime Rt(SyncTask t)
        {
            TaskRuntime rt;
            if (!runtimes.TryGetValue(t, out rt)) { rt = new TaskRuntime { Task = t }; runtimes[t] = rt; }
            return rt;
        }

        // Ile ciszy musi minąć po ostatniej zmianie, zanim ruszy synchronizacja.
        // Stare pliki zadań nie mają tego pola – wtedy zostaje dotychczasowe 5 s.
        static readonly int[] CZASY_CISZY = { 5, 30, 60, 180, 300 };
        static readonly string[] NAZWY_CISZY = { "5 sekund", "30 sekund", "1 minuta", "3 minuty", "5 minut" };

        static int CiszaSekund(SyncTask t)
        {
            int s = t.WatchDelaySeconds;
            return s < 5 ? 5 : s > 300 ? 300 : s;
        }

        static string NazwaCiszy(int sekund)
        {
            for (int i = 0; i < CZASY_CISZY.Length; i++) if (CZASY_CISZY[i] == sekund) return NAZWY_CISZY[i];
            return sekund < 60 ? sekund + " s" : (sekund / 60) + " min";
        }

        void Enqueue(SyncTask t, string reason, bool fromWatch = false)
        {
            if (pending.Contains(t) || running.Contains(t)) return;
            pending.Add(t);
            if (fromWatch) pendingFromWatch.Add(t);
            AppendLog("Zaplanowano \"" + t.Name + "\" (" + reason + ")");
            if (!busy) StartPending();
        }

        void StartPending()
        {
            if (busy || pending.Count == 0) return;
            var list = pending;
            pending = new List<SyncTask>();
            var watchOnly = new HashSet<SyncTask>(list.Where(pendingFromWatch.Contains));
            pendingFromWatch.Clear();
            StartWork(list, true, false, false, watchOnly);
        }

        void SchedulerTick()
        {
            DateTime now = DateTime.Now;

            if (watchersDirty && (now - lastEdit).TotalSeconds > 3)
            {
                watchersDirty = false;
                SyncWatchers();
            }

            if (!cfg.AutomationEnabled) { TrayText(busy ? "synchronizacja..." : "automatyka wstrzymana"); return; }

            DateTime? nearest = null;
            foreach (var t in cfg.Tasks.ToList())
            {
                if (!t.Enabled || t.Unsaved || !t.AutoArmed) continue;
                var rt = Rt(t);

                if (t.IntervalMinutes > 0)
                {
                    if (rt.NextRun == DateTime.MinValue) rt.NextRun = now.AddMinutes(t.IntervalMinutes);
                    if (now >= rt.NextRun && !rt.Running)
                    {
                        rt.NextRun = now.AddMinutes(t.IntervalMinutes);
                        Enqueue(t, "co " + t.IntervalMinutes + " min");
                    }
                    if (nearest == null || rt.NextRun < nearest) nearest = rt.NextRun;
                }

                if (t.WatchChanges && rt.WatchKey.Length > 0)
                {
                    if (rt.Watchers.Count == 0 && now >= rt.RetryAt) CreateWatchers(rt);
                    bool fire = false, ponaglenie = false;
                    int cisza = CiszaSekund(t);
                    bool wPetli = now < rt.PingPongBackoffUntil;   // patrz CheckPingPong – celowo nie reagujemy teraz
                    lock (rt)
                    {
                        if (rt.Dirty && !rt.Running && !wPetli)
                        {
                            // Zwykle czekamy, aż w źródle ucichnie. Ale w folderze, w którym cały
                            // czas coś się dzieje, cisza może nie nastać nigdy - i zmiany nie poszłyby
                            // w ogóle. Dlatego jest górny limit: po trzykrotności wybranego czasu
                            // synchronizujemy to, co już jest, nawet jeśli zmiany wciąż napływają.
                            bool ucichlo = (now - rt.LastEvent).TotalSeconds >= cisza;
                            bool czekaZaDlugo = rt.DirtySince != DateTime.MinValue &&
                                                (now - rt.DirtySince).TotalSeconds >= cisza * 3;
                            if (ucichlo || czekaZaDlugo)
                            {
                                rt.Dirty = false;
                                rt.DirtySince = DateTime.MinValue;
                                fire = true;
                                ponaglenie = !ucichlo;
                            }
                        }
                    }
                    if (fire) Enqueue(t, ponaglenie
                        ? "wykryto zmiany, w zrodle wciaz cos sie dzieje - nie czekam dluzej niz " + NazwaCiszy(cisza * 3)
                        : "wykryto zmiany, cisza " + NazwaCiszy(cisza), fromWatch: true);
                }
            }

            UpdateInfo();
            if (busy) TrayText("synchronizacja...");
            else TrayText(nearest.HasValue ? "następna: " + nearest.Value.ToString("HH:mm") : "bezczynny");
            StartPending();
        }

        void SyncWatchers()
        {
            foreach (var rt in runtimes.Values.ToList())
                if (!cfg.Tasks.Contains(rt.Task)) { DisposeWatchers(rt); runtimes.Remove(rt.Task); }

            foreach (var t in cfg.Tasks)
            {
                var rt = Rt(t);
                string key = cfg.AutomationEnabled && t.Enabled && t.WatchChanges && !t.Unsaved && t.AutoArmed
                    ? t.Direction + "|" + t.Left + "|" + t.Right + "|" + t.Excludes + "|" + t.SubFolder : "";
                if (key != rt.WatchKey)
                {
                    DisposeWatchers(rt);
                    rt.WatchKey = key;
                    rt.RetryAt = DateTime.MinValue;
                    // reguly synchronizacji sie zmienily - stary cache szybkiego skanu juz nie pasuje
                    rt.CachedL = null; rt.CachedR = null; rt.DirtyTop.Clear(); rt.FullRescanNeeded = true;
                }
                if (t.IntervalMinutes <= 0) rt.NextRun = DateTime.MinValue;
            }
        }

        void CreateWatchers(TaskRuntime rt)
        {
            var t = rt.Task.Resolved();
            rt.WatchLeft = Engine.Norm(t.Left);
            rt.WatchRight = Engine.Norm(t.Right);
            var paths = new List<string>();
            try
            {
                if (t.Direction != SyncDirection.RightToLeft) paths.Add(Engine.Norm(t.Left));
                if (t.Direction != SyncDirection.LeftToRight) paths.Add(Engine.Norm(t.Right));
                foreach (var p in paths.ToList())
                {
                    if (!Directory.Exists(p))
                    {
                        // brakujący podfolder celu przy dwukierunkowej – obserwuj tylko źródło, powstanie przy synchronizacji
                        if (t.TargetMayBeMissing && t.Direction == SyncDirection.TwoWay && p == Engine.Norm(t.Right)) { paths.Remove(p); continue; }
                        throw new DirectoryNotFoundException("niedostępny: " + p);
                    }
                    var w = new FileSystemWatcher(p)
                    {
                        IncludeSubdirectories = true,
                        InternalBufferSize = 64 * 1024,
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size
                    };
                    FileSystemEventHandler h = (s, e) => OnFsEvent(rt, e.FullPath);
                    w.Changed += h;
                    w.Created += h;
                    w.Deleted += h;
                    w.Renamed += (s, e) => OnFsEvent(rt, e.FullPath);
                    w.Error += (s, e) => Ui(() =>
                    {
                        AppendLog("Obserwator \"" + t.Name + "\" zgłosił błąd (" + e.GetException().Message + ") – ponowne podłączenie za 30 s");
                        DisposeWatchers(rt);
                        rt.RetryAt = DateTime.Now.AddSeconds(30);
                        // przerwa w obserwacji = nie wiadomo, co umknęło - nastepny przebieg musi być pełny
                        lock (rt) { if (!rt.Dirty) rt.DirtySince = DateTime.Now; rt.Dirty = true; rt.LastEvent = DateTime.Now; rt.FullRescanNeeded = true; }
                    });
                    w.EnableRaisingEvents = true;
                    rt.Watchers.Add(w);
                }
                rt.WatchFailMsg = null;   // udało się - następna awaria znowu trafi do dziennika od razu
                AppendLog("Obserwuję zmiany: \"" + t.Name + "\" (" + string.Join(", ", paths) + ")");
                // pełna synchronizacja na start / po ponownym podłączeniu – łapie zmiany z czasu, gdy nikt nie obserwował
                // (pomijane tuż po ręcznej synchronizacji – foldery są już zgodne)
                if ((DateTime.Now - rt.ManualSyncAt).TotalSeconds > 60)
                    lock (rt) { if (!rt.Dirty) rt.DirtySince = DateTime.Now; rt.Dirty = true; rt.LastEvent = DateTime.Now; rt.FullRescanNeeded = true; }
            }
            catch (Exception ex)
            {
                DisposeWatchers(rt);
                rt.RetryAt = DateTime.Now.AddSeconds(60);
                rt.FullRescanNeeded = true;
                // Bez zaśmiecania dziennika: odłączony dysk potrafi wisieć godzinami, a próby idą
                // co 60 s. Ta sama przyczyna trafia do dziennika raz, potem najwyżej co godzinę -
                // ponawianie dzieje się dalej po cichu, a powrót dysku loguje "Obserwuję zmiany".
                if (rt.WatchFailMsg != ex.Message || (DateTime.Now - rt.WatchFailLogAt).TotalMinutes >= 60)
                {
                    rt.WatchFailMsg = ex.Message;
                    rt.WatchFailLogAt = DateTime.Now;
                    AppendLog("Nie można obserwować \"" + t.Name + "\": " + ex.Message +
                              " – ponawiam po cichu co 60 s (następny wpis najwcześniej za godzinę)");
                }
            }
        }

        // Segment najwyższego poziomu ścieżki względem obserwowanego korzenia (np. nazwa folderu gry).
        // Zwraca null, gdy ścieżka nie pasuje do żadnego z korzeni - wtedy nie wiadomo, która podgałąź
        // się zmieniła, więc bezpieczniej wymusić pełny skan zamiast zgadywać.
        static string TopSegment(string root, string path)
        {
            if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(path)) return null;
            if (path.Equals(root, StringComparison.OrdinalIgnoreCase)) return "";
            if (!path.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase)) return null;
            string rel = path.Substring(root.Length + 1);
            int i = rel.IndexOf('\\');
            return i < 0 ? rel : rel.Substring(0, i);
        }

        static void OnFsEvent(TaskRuntime rt, string path)
        {
            if (path.IndexOf("\\" + Engine.ArchiveDir, StringComparison.OrdinalIgnoreCase) >= 0 ||
                path.EndsWith(".~synctmp", StringComparison.OrdinalIgnoreCase)) return;
            string top = TopSegment(rt.WatchLeft, path) ?? TopSegment(rt.WatchRight, path);
            lock (rt)
            {
                // przy dwukierunkowej obserwowane są oba foldery – ignoruj własne zapisy
                if (rt.Task.Direction == SyncDirection.TwoWay && (rt.Running || DateTime.Now < rt.IgnoreUntil)) return;
                if (!rt.Dirty) rt.DirtySince = DateTime.Now;
                rt.Dirty = true;
                rt.LastEvent = DateTime.Now;
                // pusty/nieznany segment (zmiana samego korzenia) - nie wiadomo która podgaląź, wymuś pełny skan
                if (!string.IsNullOrEmpty(top)) rt.DirtyTop.Add(top);
                else rt.FullRescanNeeded = true;
            }
        }

        static void DisposeWatchers(TaskRuntime rt)
        {
            foreach (var w in rt.Watchers)
                try { w.EnableRaisingEvents = false; w.Dispose(); } catch { }
            rt.Watchers.Clear();
        }

        // ---------- budowa interfejsu

        // Zdarzenia, tooltipy i elementy dynamiczne - wszystko, czego Designer nie serializuje.
        // Tworzenie kontrolek i układ mieszka w MainForm.Designer.cs (InitializeComponent).
        void WireUi()
        {
            headerPanel.Paint += (s, e) =>
            {
                var g = e.Graphics;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                try
                {
                    if (busy) using (var bmp = Logo.Draw(48, headerRot, true)) g.DrawImage(bmp, 12, 6, 48, 48);
                    else g.DrawIcon(new Icon(Icon, 48, 48), 12, 6);
                }
                catch { }
                using (var f1 = new Font("Segoe UI", 20f, FontStyle.Bold))
                using (var f2 = new Font("Segoe UI", 9.5f, FontStyle.Italic))
                {
                    g.DrawString("Echo Sync", f1, Brushes.White, 70, 2);
                    g.DrawString("synchronizacja i kopie lustrzane – dyski lokalne i sieciowe", f2, new SolidBrush(Color.FromArgb(210, 230, 255)), 74, 38);
                }
            };
            btnDecyduj.Click += delegate { OtworzDecyzje(); };
            Load += delegate
            {
                try { if (splitMain.Width > 600) splitMain.SplitterDistance = 260; } catch { }
            };

            // lewy panel: lista zadań
            lstTasks.DrawItem += LstTasks_DrawItem;
            lstTasks.SelectedIndexChanged += delegate { TaskSelectionChanged(); };
            btnNoweZadanie.Click += delegate { AddTask(new SyncTask { SubFolder = true }); };
            btnDuplikuj.Click += delegate { DuplicateTask(); };
            btnUsunZadanie.Click += delegate { DeleteTask(); };
            btnTaskUp.Click += delegate { MoveTask(-1); };
            btnTaskDown.Click += delegate { MoveTask(1); };
            btnDeleteFiles.Click += delegate { DeleteSelectedFiles(); };
            new ToolTip().SetToolTip(btnDeleteFiles, "Usuwa pliki zaznaczone na liście „Podgląd zmian” (Ctrl/Shift + klik = wiele, Delete = usuń).\nDziała zgodnie z ustawieniem „Usuwane pliki” zadania.");
            btnDecyzjeLista.Click += delegate { OtworzDecyzje(); };
            new ToolTip().SetToolTip(btnDecyzjeLista, "Po drugiej stronie coś skasowano. Kliknij, żeby zdecydować: przywrócić czy usunąć też u siebie.");
            btnArchiwum.Click += delegate { OtworzArchiwum(); };
            new ToolTip().SetToolTip(btnArchiwum, "Pokazuje, co leży w _SyncArchive i ile zajmuje.\nStąd czyścisz je ręcznie albo ustawiasz, po ilu dniach stare kopie mają znikać same.");
            new ToolTip().SetToolTip(txtSearchTasks, "Szukaj zadania po nazwie. Enter = następne dopasowanie.");
            txtSearchTasks.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.Handled = true; e.SuppressKeyPress = true; FindNextTask(); } };
            txtSearchTasks.TextChanged += delegate
            {
                taskSearchDirty = true;
                // filtrowanie na żywo: przefiltruj listę zachowując bieżące zaznaczenie
                var sel = lstTasks.SelectedItem as SyncTask;
                RefreshTaskList(sel != null ? Math.Max(0, cfg.Tasks.IndexOf(sel)) : 0);
            };

            // podgląd zmian
            lvActions.ColumnClick += (s, e) => SortActions(e.Column);
            miExpandAll.Click += delegate { ExpandAllFolders(); };
            miCollapseAll.Click += delegate { CollapseAllFolders(); };
            btnSearchTree.Click += delegate { FindNextInTree(); };
            new ToolTip().SetToolTip(txtSearchTree, "Szukaj pliku lub folderu po nazwie na liście „Podgląd zmian”.\nEnter = następne dopasowanie (rozwija foldery po drodze).");
            txtSearchTree.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.Handled = true; e.SuppressKeyPress = true; FindNextInTree(); } };
            txtSearchTree.TextChanged += delegate
            {
                searchDirty = true;
                // filtrowanie na żywo: lista zawęża się z każdą literą (jak w Eksploratorze),
                // Enter/„Znajdź” nadal skacze po kolejnych dopasowaniach
                treeFilter = txtSearchTree.Text.Trim();
                RenderForest();
                lblSearchInfo.Text = treeFilter.Length == 0 ? "" :
                    filterVisible == null || filterVisible.Count == 0 ? "Brak wyników" : "";
            };
            chkShowSame.CheckedChanged += delegate { RerenderActions(); };

            // przyciski uruchamiania
            btnPreview.Click += delegate { StartSelected(false); };
            new ToolTip().SetToolTip(btnPreview, "Czysta analiza – skanuje oba foldery i pokazuje, co zostanie skopiowane lub usunięte.\nNiczego nie zmienia na dysku.");
            btnSync.Click += delegate { StartSelected(true); };
            btnRunAll.Click += delegate { StartWork(cfg.Tasks.Where(x => x.Enabled && !x.Unsaved).ToList(), true, true, false); };
            btnCancel.Click += delegate { engine.Cancel = true; SetStatus("Przerywanie..."); };
            btnRobo.Click += delegate { OpenRobo(); };
            new ToolTip().SetToolTip(btnRobo, "Jednorazowe kopiowanie lub przenoszenie folderów (robocopy)");
            chkAutomation.Checked = cfg.AutomationEnabled;
            chkAutomation.CheckedChanged += delegate
            {
                cfg.AutomationEnabled = chkAutomation.Checked;
                if (miPause != null) miPause.Text = cfg.AutomationEnabled ? "Wstrzymaj automatykę" : "Wznów automatykę";
                SaveConfig();
                watchersDirty = true;
                lastEdit = DateTime.MinValue;
                AppendLog("Automatyka " + (cfg.AutomationEnabled ? "włączona" : "wstrzymana"));
            };
            chkAutostart.Checked = AutostartEnabled();
            chkAutostart.CheckedChanged += delegate
            {
                try { SetAutostart(chkAutostart.Checked); }
                catch (Exception ex) { MessageBox.Show("Nie udało się zmienić autostartu: " + ex.Message); }
            };

            // pasek folderów
            btnFolderLewy.Image = FolderBitmap();
            btnFolderPrawy.Image = FolderBitmap();
            btnFolderLewy.Click += delegate { Browse(txtLeft); };
            btnFolderPrawy.Click += delegate { Browse(txtRight); };
            new ToolTip().SetToolTip(btnFolderLewy, "Wybierz folder");
            new ToolTip().SetToolTip(btnFolderPrawy, "Wybierz folder");
            btnSprawdzLewy.Click += delegate { TestPath(txtLeft.Text); };
            btnSprawdzPrawy.Click += delegate { TestPath(txtRight.Text); };
            arrow.Click += delegate { if (cmbDir.SelectedIndex >= 0) cmbDir.SelectedIndex = (cmbDir.SelectedIndex + 1) % 3; };
            new ToolTip().SetToolTip(arrow, "Kliknij, aby zmienić kierunek synchronizacji");
            lnkSwap.Click += delegate { string sw = txtLeft.Text; txtLeft.Text = txtRight.Text; txtRight.Text = sw; };

            // edytor zadania
            new ToolTip().SetToolTip(chkSubFolder, "Tworzy w folderze docelowym podfolder o nazwie folderu źródłowego.");
            new ToolTip().SetToolTip(chkMirror, "Cel staje się dokładną kopią źródła: usuwa elementy nieobecne w źródle i może nadpisać nowsze pliki w celu.");
            new ToolTip().SetToolTip(chkWersje, "Zanim plik zostanie nadpisany, jego poprzednia wersja trafia do _SyncArchive.\n" +
                "Dzięki temu da się cofnąć skutki pomyłki (np. zły kierunek synchronizacji).\n" +
                "Działa tylko przy usuwaniu do _SyncArchive. Kosztem jest miejsce na dysku.");
            new ToolTip().SetToolTip(cmbDelete, "Archiwum działa także na dyskach sieciowych. Kosz Windows może tam usuwać trwale.");
            cmbWatchDelay.Items.AddRange(NAZWY_CISZY);
            cmbWatchDelay.SelectedIndex = 0;
            new ToolTip().SetToolTip(chkWatch, "Synchronizuje po wykryciu zmian, gdy przez wybrany czas nie nadejdzie kolejna zmiana.");
            btnSave.Click += delegate { SaveEditor(); };
            btnDiscard.Click += delegate { DiscardEditor(); };
            linkStalaDecyzja.LinkClicked += delegate
            {
                var z = editing;
                if (z == null || z.StalaDecyzjaZnikniete.Length == 0) return;
                if (MessageBox.Show(this, "Wycofać stałą decyzję dla zadania \"" + z.Name + "\"?\n\n" +
                        "Program znowu będzie pytał, gdy coś zniknie po drugiej stronie.",
                        "Echo Sync", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                z.StalaDecyzjaZnikniete = "";
                SaveConfig();
                OdswiezLinkStalejDecyzji();
                AppendLog("Zadanie \"" + z.Name + "\": wycofano stałą decyzję – program znowu będzie pytał.");
            };

            // zmiany w edytorze -> model
            EventHandler changed = delegate { EditorChanged(); };
            txtName.TextChanged += changed;
            txtLeft.TextChanged += changed;
            txtRight.TextChanged += changed;
            txtExcl.TextChanged += changed;
            cmbDir.SelectedIndexChanged += changed;
            cmbDelete.SelectedIndexChanged += changed;
            chkMirror.CheckedChanged += changed;
            chkEnabled.CheckedChanged += changed;
            chkInterval.CheckedChanged += changed;
            numInterval.ValueChanged += changed;
            chkWatch.CheckedChanged += changed;
            chkWersje.CheckedChanged += changed;
            cmbWatchDelay.SelectedIndexChanged += changed;
            chkSubFolder.CheckedChanged += changed;

            lvActions.DoubleClick += delegate
            {
                var it = lvActions.SelectedItems.Count > 0 ? lvActions.SelectedItems[0] : null;
                var node = it != null ? it.Tag as ActionNode : null;
                if (node != null && node.IsFolder) ToggleFolder(node);
                else OpenSelectedInExplorer();
            };
            new ToolTip().SetToolTip(lvActions, "Kliknij dwukrotnie na folder, aby go zwinąć/rozwinąć.\nMożna zaznaczyć cały folder i usunąć jego zawartość z dysku.");
            // zaznaczanie wielu (Ctrl/Shift) – licznik odświeżany z opóźnieniem, żeby duże zakresy nie spowalniały
            var selTimer = new System.Windows.Forms.Timer { Interval = 80 };
            selTimer.Tick += delegate { selTimer.Stop(); UpdateSelectionInfo(); };
            lvActions.SelectedIndexChanged += delegate { selTimer.Stop(); selTimer.Start(); };
            lvActions.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Delete && btnDeleteFiles.Enabled) { e.Handled = true; DeleteSelectedFiles(); }
                else if (e.KeyCode == Keys.A && e.Control)
                {
                    e.Handled = true;
                    lvActions.BeginUpdate();
                    foreach (ListViewItem it in lvActions.Items) it.Selected = true;
                    lvActions.EndUpdate();
                }
            };
        }

        // ---------- usuwanie zaznaczonych plików z listy

        Button btnDeleteFiles;
        Label lblSelection;

        List<SyncAction> SelectedActions()
        {
            var list = new List<SyncAction>();
            foreach (ListViewItem it in lvActions.SelectedItems)
            {
                var node = it.Tag as ActionNode;
                var a = node != null ? node.EffectiveAction : null;
                if (a != null) list.Add(a);
            }
            return list;
        }

        static string LeftPathOf(SyncAction a) { return a.ToRight ? a.SrcPath : a.DstPath; }
        static string RightPathOf(SyncAction a) { return a.ToRight ? a.DstPath : a.SrcPath; }

        static bool PathExists(string p) { return !string.IsNullOrEmpty(p) && (File.Exists(p) || Directory.Exists(p)); }

        static long SizeOf(string p)
        {
            try { return File.Exists(p) ? new FileInfo(p).Length : 0; } catch { return 0; }
        }

        void UpdateSelectionInfo()
        {
            int n = lvActions.SelectedItems.Count;
            btnDeleteFiles.Enabled = n > 0 && !busy && editing != null && !editing.Unsaved;
            btnDeleteFiles.Text = n > 0 ? "🗑 Usuń wybrane pliki (" + n + ")" : "🗑 Usuń wybrane pliki";
            if (n == 0) { lblSelection.Text = ""; return; }
            long size = SelectedActions().Sum(a => a.Size);
            lblSelection.Text = "Zaznaczono: " + n + (n == 1 ? " element" : n < 5 ? " elementy" : " elementów") + "  |  " + FmtSize(size);
        }

        void DeleteSelectedFiles()
        {
            var acts = SelectedActions();
            var t = editing;
            if (acts.Count == 0 || t == null || busy) return;
            if (dirty) { MessageBox.Show(this, "Najpierw zapisz lub odrzuć zmiany w ustawieniach zadania.", "Echo Sync"); return; }

            var left = acts.Select(LeftPathOf).Where(PathExists).ToList();
            var right = acts.Select(RightPathOf).Where(PathExists).ToList();
            if (left.Count == 0 && right.Count == 0)
            {
                MessageBox.Show(this, "Zaznaczone pliki już nie istnieją na dysku. Kliknij „Analizuj”, aby odświeżyć listę.", "Echo Sync");
                return;
            }

            string modeText = t.DeleteMode == DeleteMode.Archive ? "przeniesione do folderu _SyncArchive (można je odzyskać)"
                            : t.DeleteMode == DeleteMode.RecycleBin ? "przeniesione do Kosza Windows (na dyskach sieciowych – usunięte trwale!)"
                            : "USUNIĘTE TRWALE – bez możliwości odzyskania!";

            // okno potwierdzenia z wyborem strony
            using (var dlg = new Form())
            {
                dlg.Text = "Usuń wybrane pliki";
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.MinimizeBox = dlg.MaximizeBox = false;
                dlg.ShowInTaskbar = false;
                dlg.Font = Font;
                dlg.ClientSize = new Size(580, 310);
                dlg.Icon = Icon;

                var head = new Label { Text = "Usunąć " + acts.Count + " wybranych elementów?", Font = new Font("Segoe UI", 12f, FontStyle.Bold), Location = new Point(16, 14), AutoSize = true };
                string opisLewy2 = t.Direction == SyncDirection.LeftToRight ? " (źródło)" : t.Direction == SyncDirection.RightToLeft ? " (cel)" : "";
                string opisPrawy2 = t.Direction == SyncDirection.LeftToRight ? " (cel)" : t.Direction == SyncDirection.RightToLeft ? " (źródło)" : "";
                var rbLeft = new RadioButton { Text = "Z folderu LEWEGO" + opisLewy2 + ":   " + left.Count + " elem. (" + FmtSize(left.Sum(p => SizeOf(p))) + ")", Location = new Point(20, 56), AutoSize = true, Enabled = left.Count > 0 };
                var rbRight = new RadioButton { Text = "Z folderu PRAWEGO" + opisPrawy2 + ":   " + right.Count + " elem. (" + FmtSize(right.Sum(p => SizeOf(p))) + ")", Location = new Point(20, 82), AutoSize = true, Enabled = right.Count > 0 };
                var rbBoth = new RadioButton { Text = "Z OBU folderów:   " + (left.Count + right.Count) + " elem.", Location = new Point(20, 108), AutoSize = true, Enabled = left.Count > 0 && right.Count > 0 };
                // domyślnie strona docelowa (tam, gdzie trafiają kopie), jeśli coś tam jest
                bool targetRight = t.Direction != SyncDirection.RightToLeft;
                if (targetRight && right.Count > 0) rbRight.Checked = true;
                else if (!targetRight && left.Count > 0) rbLeft.Checked = true;
                else if (right.Count > 0) rbRight.Checked = true;
                else rbLeft.Checked = true;

                var info = new Label
                {
                    Location = new Point(16, 142), Size = new Size(548, 110),
                    Text = "Pliki zostaną " + modeText + "\n(zgodnie z ustawieniem „Usuwane pliki” tego zadania)." +
                           (t.Direction == SyncDirection.TwoWay
                               ? "\n\n⚠ Zadanie jest DWUKIERUNKOWE – plik usunięty tylko z jednej strony zostanie przy następnej synchronizacji skopiowany z powrotem z drugiej."
                               : ""),
                    ForeColor = t.DeleteMode == DeleteMode.Permanent ? Color.DarkRed : SystemColors.ControlText
                };

                var ok = new Button { Text = "Usuń", Location = new Point(380, 266), Size = new Size(90, 30), DialogResult = DialogResult.OK };
                var cancel = new Button { Text = "Anuluj", Location = new Point(476, 266), Size = new Size(90, 30), DialogResult = DialogResult.Cancel };
                dlg.AcceptButton = cancel;   // Enter nie usuwa przez przypadek
                dlg.CancelButton = cancel;
                dlg.Controls.AddRange(new Control[] { head, rbLeft, rbRight, rbBoth, info, ok, cancel });
                dlg.Shown += delegate { cancel.Focus(); };
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                var chosen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (rbLeft.Checked || rbBoth.Checked) chosen.UnionWith(left);
                if (rbRight.Checked || rbBoth.Checked) chosen.UnionWith(right);
                RunDeleteFiles(t, acts, chosen, rbLeft.Checked ? "lewy" : rbRight.Checked ? "prawy" : "oba");
            }
        }

        void RunDeleteFiles(SyncTask t, List<SyncAction> acts, HashSet<string> chosen, string sideText)
        {
            // folder główny = pełna ścieżka minus ścieżka względna (archiwum trafia do _SyncArchive tego folderu)
            var jobs = new List<Tuple<string, string>>();
            foreach (var a in acts)
                foreach (var p in new[] { LeftPathOf(a), RightPathOf(a) })
                    if (p != null && chosen.Contains(p) && p.Length > a.Rel.Length && p.EndsWith(a.Rel, StringComparison.OrdinalIgnoreCase))
                        jobs.Add(Tuple.Create(p.Substring(0, p.Length - a.Rel.Length).TrimEnd('\\'), a.Rel));
            // jeśli zaznaczono folder, nie usuwaj osobno plików z jego wnętrza
            var dirs = jobs.Select(j => Path.Combine(j.Item1, j.Item2)).Where(Directory.Exists).ToList();
            jobs = jobs.Where(j => !dirs.Any(d => Path.Combine(j.Item1, j.Item2).StartsWith(d + "\\", StringComparison.OrdinalIgnoreCase))).ToList();

            SetBusy(true, false);
            AppendLog("=== USUWANIE WYBRANYCH (" + jobs.Count + ", strona: " + sideText + "): " + t.Name);
            var mode = t.DeleteMode;
            var th = new Thread(() =>
            {
                string stamp = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
                int okN = 0, errN = 0, i = 0;
                foreach (var j in jobs)
                {
                    i++;
                    string full = Path.Combine(j.Item1, j.Item2);
                    int idx = i;
                    try
                    {
                        Ui(() => { SetStatus("Usuwanie (" + idx + "/" + jobs.Count + "): " + j.Item2); progress.Value = Math.Min(1000, idx * 1000 / Math.Max(1, jobs.Count)); });
                        Engine.DoDelete(j.Item1, j.Item2, mode, stamp);
                        okN++;
                        Ui(() => AppendLog("  Usunięto: " + full));
                    }
                    catch (Exception ex)
                    {
                        errN++;
                        string msg = ex.Message;
                        Ui(() => AppendLog("  BŁĄD usuwania: " + full + " – " + msg));
                    }
                }
                int okF = okN, errF = errN;
                Ui(() =>
                {
                    lvActions.BeginUpdate();
                    // sprawdzamy wszystkie widoczne wiersze, nie tylko zaznaczone - usunięty folder
                    // zabiera ze sobą też wiersze plików w jego wnętrzu, które same nie były zaznaczone
                    foreach (var it in lvActions.Items.Cast<ListViewItem>().ToList())
                    {
                        var node = it.Tag as ActionNode;
                        var a = node != null ? node.EffectiveAction : null;
                        if (a != null && !PathExists(LeftPathOf(a)) && !PathExists(RightPathOf(a))) lvActions.Items.Remove(it);
                        else if (it.Selected) it.ForeColor = Color.Gray;
                    }
                    lvActions.EndUpdate();
                    SetBusy(false);
                    progress.Value = 1000;
                    AppendLog("  Zakończono: usunięto " + okF + ", błędów " + errF);
                    SetStatus(errF == 0 ? "Usunięto " + okF + " elementów. Kliknij „Analizuj”, aby odświeżyć listę."
                                        : "Usunięto " + okF + ", błędów: " + errF + " – zobacz Dziennik.");
                    UpdateSelectionInfo();
                });
            });
            th.IsBackground = true;
            th.Start();
        }

        RoboForm roboForm;

        void OpenRobo()
        {
            if (roboForm == null || roboForm.IsDisposed)
            {
                roboForm = new RoboForm(Icon, s => AppendLog(s));
                roboForm.Show(this);
            }
            else
            {
                if (roboForm.WindowState == FormWindowState.Minimized) roboForm.WindowState = FormWindowState.Normal;
                roboForm.Activate();
            }
        }

        void OpenSelectedInExplorer()
        {
            if (lvActions.SelectedItems.Count == 0) return;
            var node = lvActions.SelectedItems[0].Tag as ActionNode;
            var a = node != null ? node.EffectiveAction : null;
            if (a == null) return;
            foreach (var p in new[] { a.DstPath, a.SrcPath })
            {
                if (string.IsNullOrEmpty(p)) continue;
                try
                {
                    if (File.Exists(p) || Directory.Exists(p)) { Process.Start("explorer.exe", "/select,\"" + p + "\""); return; }
                    string dir = Path.GetDirectoryName(p);
                    if (Directory.Exists(dir)) { Process.Start("explorer.exe", "\"" + dir + "\""); return; }
                }
                catch { }
            }
            SetStatus("Ten element już nie istnieje.");
        }

        // ---------- historia ostatnich zmian zadania

        static string HistoryFile(string taskName)
        {
            string safe = new string(taskName.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch).ToArray());
            return Path.Combine(Path.Combine(Config.AppDir, "historia"), safe + ".xml");
        }

        static void SaveHistory(string taskName, List<SyncAction> acts)
        {
            try
            {
                string f = HistoryFile(taskName);
                Directory.CreateDirectory(Path.GetDirectoryName(f));
                using (var fs = File.Create(f))
                    new XmlSerializer(typeof(List<SyncAction>)).Serialize(fs, acts.Take(20000).ToList());
            }
            catch { }
        }

        void ShowHistory(SyncTask t)
        {
            shown.Clear();
            treeForest.Clear();
            lvActions.Items.Clear();
            lblSummary.Text = "";
            if (t == null || t.Unsaved) return;
            string f = HistoryFile(t.Name);
            if (!File.Exists(f)) { lblSummary.Text = "Brak zapisanych zmian dla tego zadania – kliknij „Analizuj”, aby zobaczyć, co zostanie zsynchronizowane."; return; }
            try
            {
                List<SyncAction> acts;
                using (var fs = File.OpenRead(f))
                    acts = (List<SyncAction>)new XmlSerializer(typeof(List<SyncAction>)).Deserialize(fs);
                ShowActions(t, acts, false);
                // To jest zapis PRZESZŁOŚCI (co zostało zrobione), nie bieżący stan folderów.
                // Bez wyraźnego oznaczenia "brak w celu" z historii wygląda jak aktualna bzdura,
                // gdy pliki dawno są na miejscu - stąd kolor i wprost napisana instrukcja.
                lblSummary.Text = "HISTORIA – tak wyglądało OSTATNIE WYKONANIE (" + File.GetLastWriteTime(f).ToString("yyyy-MM-dd HH:mm:ss") +
                                  "): " + lblSummary.Text + "   •   To NIE jest bieżący stan – kliknij „Analizuj”." +
                                  "   •   dwuklik = pokaż w Eksploratorze";
                lblSummary.BackColor = Color.FromArgb(255, 249, 219);   // żółtawy pasek = patrzysz w przeszłość
            }
            catch (Exception ex) { lblSummary.Text = "Nie można wczytać historii: " + ex.Message; }
        }

        void UpdateTargetLabel()
        {
            if (lblTarget == null) return;
            var tmp = new SyncTask
            {
                Left = txtLeft.Text, Right = txtRight.Text, SubFolder = chkSubFolder.Checked,
                Direction = (SyncDirection)Math.Max(0, cmbDir.SelectedIndex)
            }.Resolved();
            string target = tmp.Direction == SyncDirection.RightToLeft ? tmp.Left : tmp.Right;
            lblTarget.Text = target.Trim().Length > 0 ? "Cel:  " + target : "";
        }

        static Bitmap folderBitmap;

        static Bitmap FolderBitmap()
        {
            if (folderBitmap != null) return folderBitmap;
            var b = new Bitmap(36, 32);
            using (var g = Graphics.FromImage(b))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (var back = new SolidBrush(Color.FromArgb(230, 170, 40)))
                    g.FillPolygon(back, new[] { new Point(2, 5), new Point(13, 5), new Point(16, 9), new Point(33, 9), new Point(33, 29), new Point(2, 29) });
                using (var front = new System.Drawing.Drawing2D.LinearGradientBrush(new Rectangle(0, 12, 36, 18), Color.FromArgb(255, 225, 120), Color.FromArgb(245, 190, 50), 90f))
                    g.FillPolygon(front, new[] { new Point(2, 13), new Point(34, 13), new Point(33, 29), new Point(2, 29) });
                using (var pen = new Pen(Color.FromArgb(180, 120, 20)))
                    g.DrawRectangle(pen, 2, 13, 31, 16);
            }
            return folderBitmap = b;
        }

        Control MkFolderSide(string title, TextBox txt, out Label lblTitle)
        {
            var side = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 3, BackColor = Color.Transparent, Margin = new Padding(2) };
            side.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 52));
            side.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            side.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            side.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            side.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            side.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var btnFolder = new Button { Image = FolderBitmap(), Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(235, 245, 255), Cursor = Cursors.Hand, Margin = new Padding(0, 4, 4, 0) };
            btnFolder.FlatAppearance.BorderColor = Color.FromArgb(70, 130, 200);
            btnFolder.Click += delegate { Browse(txt); };
            new ToolTip().SetToolTip(btnFolder, "Wybierz folder");
            side.Controls.Add(btnFolder, 0, 0);
            side.SetRowSpan(btnFolder, 2);

            lblTitle = new Label { Text = title, Dock = DockStyle.Fill, TextAlign = ContentAlignment.BottomLeft, Font = new Font("Segoe UI", 11f), BackColor = Color.Transparent, ForeColor = Color.FromArgb(0, 35, 90), AutoEllipsis = true };
            side.Controls.Add(lblTitle, 1, 0);
            var btnTest = new Button { Text = "Sprawdź", Dock = DockStyle.Fill, Margin = new Padding(3, 6, 0, 2), BackColor = SystemColors.Control, UseVisualStyleBackColor = true };
            btnTest.Click += delegate { TestPath(txt.Text); };
            side.Controls.Add(btnTest, 2, 0);

            txt.Margin = new Padding(0, 4, 0, 0);
            side.Controls.Add(txt, 1, 1);
            side.SetColumnSpan(txt, 2);
            return side;
        }

        static Button MkButton(string text, int width, EventHandler click)
        {
            var b = new Button { Text = text, Width = width, Height = 28 };
            b.Click += click;
            return b;
        }

        static void AddRow(TableLayoutPanel tl, int row, string label, Control main, Control c2, Control c3)
        {
            tl.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
            tl.Controls.Add(main, 1, row);
            if (c2 == null && c3 == null) tl.SetColumnSpan(main, 3);
            if (c2 != null) tl.Controls.Add(c2, 2, row);
            if (c3 != null) tl.Controls.Add(c3, 3, row);
        }

        // ---------- zadania

        SyncTask Current { get { return lstTasks.SelectedItem as SyncTask; } }

        void RefreshTaskList(int select)
        {
            // filtr z pola szukania: lista pokazuje tylko pasujące zadania (jak w Windows),
            // puste pole = wszystkie. Zaznaczenie wybieramy PO OBIEKCIE, nie po indeksie,
            // bo przy filtrze indeksy listy i cfg.Tasks przestają się pokrywać.
            string filtr = txtSearchTasks != null ? txtSearchTasks.Text.Trim() : "";
            var chciane = cfg.Tasks.Count > 0 ? cfg.Tasks[Math.Max(0, Math.Min(select, cfg.Tasks.Count - 1))] : null;
            loading = true;
            lstTasks.BeginUpdate();
            lstTasks.Items.Clear();
            foreach (var t in cfg.Tasks)
                if (filtr.Length == 0 || t.Name.IndexOf(filtr, StringComparison.OrdinalIgnoreCase) >= 0)
                    lstTasks.Items.Add(t);
            lstTasks.EndUpdate();
            loading = false;
            if (chciane != null && lstTasks.Items.Contains(chciane)) lstTasks.SelectedItem = chciane;
            else if (lstTasks.Items.Count > 0) lstTasks.SelectedIndex = 0;
            else if (cfg.Tasks.Count == 0) LoadEditor();
        }

        void FindNextTask()
        {
            string term = txtSearchTasks.Text.Trim();
            if (term.Length == 0) return;
            if (taskSearchDirty) { taskSearchIndex = -1; taskSearchDirty = false; }
            int count = lstTasks.Items.Count;
            if (count == 0) return;
            for (int step = 1; step <= count; step++)
            {
                int idx = (taskSearchIndex + step) % count;
                var task = lstTasks.Items[idx] as SyncTask;
                if (task != null && task.Name.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    taskSearchIndex = idx;
                    lstTasks.SelectedIndex = idx;
                    lstTasks.TopIndex = Math.Max(0, idx - 3);
                    return;
                }
            }
        }

        void TaskSelectionChanged()
        {
            if (loading) return;
            var target = Current;
            if (editing != null && target != editing && dirty)
            {
                bool wasUnsaved = editing.Unsaved;
                if (!ConfirmLeave())
                {
                    loading = true;
                    lstTasks.SelectedItem = editing;
                    loading = false;
                    return;
                }
                if (wasUnsaved && !cfg.Tasks.Contains(editing))
                {
                    editing = null;
                    RefreshTaskList(Math.Max(0, cfg.Tasks.IndexOf(target)));
                    return;
                }
            }
            LoadEditor();
        }

        // true = można przejść dalej (zapisano lub odrzucono)
        bool ConfirmLeave()
        {
            if (!dirty || editing == null) return true;
            var r = MessageBox.Show(this, "Zadanie \"" + (txtName.Text.Length > 0 ? txtName.Text : editing.Name) + "\" ma niezapisane zmiany.\n\nZapisać je?",
                "Niezapisane zmiany", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (r == DialogResult.Cancel) return false;
            if (r == DialogResult.Yes) return SaveEditor();
            if (editing.Unsaved) cfg.Tasks.Remove(editing);
            dirty = false;
            return true;
        }

        bool SaveEditor()
        {
            var t = editing;
            if (t == null) return true;
            if (txtName.Text.Trim().Length == 0) { MessageBox.Show(this, "Podaj nazwę zadania."); txtName.Focus(); return false; }
            if (txtLeft.Text.Trim().Length == 0 || txtRight.Text.Trim().Length == 0)
            {
                MessageBox.Show(this, "Podaj oba foldery (lewy i prawy).");
                return false;
            }
            if (cfg.Tasks.Any(x => x != t && x.Name.Equals(txtName.Text.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show(this, "Zadanie o tej nazwie już istnieje.");
                return false;
            }
            // zmiana tego, CO i GDZIE jest synchronizowane → automatyka czeka na ręczną synchronizację
            var newDir = (SyncDirection)Math.Max(0, cmbDir.SelectedIndex);
            if (t.Unsaved || t.Left != txtLeft.Text.Trim() || t.Right != txtRight.Text.Trim() || t.Direction != newDir ||
                t.Mirror != chkMirror.Checked || t.SubFolder != chkSubFolder.Checked || t.Excludes != txtExcl.Text)
            {
                t.AutoArmed = false;
                // Migawka opisuje stan POPRZEDNIEJ pary folderów. Po zmianie tego, co i gdzie
                // jest synchronizowane, byłaby nieprawdą i kazałaby kasować rzeczy bez powodu.
                Migawki.Skasuj(t.Name);
            }
            if (t.Name != txtName.Text.Trim()) Migawki.Skasuj(t.Name);   // migawka leży pod starą nazwą
            t.Name = txtName.Text.Trim();
            t.Left = txtLeft.Text.Trim();
            t.Right = txtRight.Text.Trim();
            t.Excludes = txtExcl.Text;
            t.Direction = (SyncDirection)Math.Max(0, cmbDir.SelectedIndex);
            t.DeleteMode = (DeleteMode)Math.Max(0, cmbDelete.SelectedIndex);
            t.Mirror = chkMirror.Checked;
            t.Enabled = chkEnabled.Checked;
            t.IntervalMinutes = chkInterval.Checked ? (int)numInterval.Value : 0;
            t.WatchChanges = chkWatch.Checked;
            t.ZachowajPoprzednieWersje = chkWersje.Checked;
            t.WatchDelaySeconds = CZASY_CISZY[Math.Max(0, Math.Min(CZASY_CISZY.Length - 1, cmbWatchDelay.SelectedIndex))];
            t.SubFolder = chkSubFolder.Checked;
            if (t.CreatedAt == DateTime.MinValue) t.CreatedAt = DateTime.Now;
            bool wasNew = t.Unsaved;
            t.Unsaved = false;
            dirty = false;

            var rt = Rt(t);
            rt.NextRun = DateTime.MinValue;
            rt.WatchKey = "?force";          // wymuś ponowne podłączenie obserwatora
            watchersDirty = true;
            lastEdit = DateTime.MinValue;
            SaveConfig();

            loading = true;
            if (!lstTasks.Items.Contains(t)) RefreshTaskList(Math.Max(0, cfg.Tasks.IndexOf(t)));   // np. nowe zadanie przy aktywnym filtrze
            lstTasks.Invalidate();   // ta sama referencja - wystarczy przerysować (lista jest owner-drawn)
            lstTasks.SelectedItem = t;
            loading = false;
            UpdateEditorState();
            AppendLog((wasNew ? "Dodano" : "Zapisano") + " zadanie \"" + t.Name + "\"" +
                      (t.Enabled && (t.IntervalMinutes > 0 || t.WatchChanges) ? " – automatyka aktywna" : ""));
            SetStatus("Zapisano zadanie \"" + t.Name + "\".");
            return true;
        }

        void DiscardEditor()
        {
            if (editing == null) return;
            if (editing.Unsaved)
            {
                int i = cfg.Tasks.IndexOf(editing);
                cfg.Tasks.Remove(editing);
                dirty = false;
                editing = null;
                RefreshTaskList(Math.Max(0, i - 1));
            }
            else
            {
                dirty = false;
                LoadEditor();
            }
        }

        void UpdateEditorState()
        {
            btnSave.Enabled = dirty;
            btnDiscard.Enabled = dirty;
            grpEdit.Text = editing == null ? "Ustawienia zadania"
                : "Ustawienia zadania" + (editing.Unsaved ? " – NOWE (kliknij Zapisz, aby aktywować)" : dirty ? " – niezapisane zmiany" : "");
            grpEdit.ForeColor = dirty ? Color.DarkOrange : SystemColors.ControlText;
            foreach (Control c in grpEdit.Controls) if (c != lblStatusBar) c.ForeColor = SystemColors.ControlText;
            UpdateInfo();
        }

        void UpdateInfo()
        {
            var t = editing;
            if (t == null || lblInfo == null) { if (lblInfo != null) lblInfo.Text = ""; return; }
            string s = "Utworzono: " + (t.CreatedAt == DateTime.MinValue ? (t.Unsaved ? "(niezapisane)" : "—") : t.CreatedAt.ToString("yyyy-MM-dd HH:mm"));
            if (!string.IsNullOrEmpty(t.LastSyncResult)) s += "     Wynik: " + t.LastSyncResult;
            if (lblStatusBar != null)
                lblStatusBar.Text = "Ostatnia pomyślna synchronizacja: " + (t.LastSuccessAt != DateTime.MinValue ? t.LastSuccessAt.ToString("dd.MM.yyyy HH:mm")
                                        : t.LastSyncAt != DateTime.MinValue && (t.LastSyncResult ?? "").StartsWith("OK") ? t.LastSyncAt.ToString("dd.MM.yyyy HH:mm") : "nigdy") +
                                    "   –   Ostatnia próba synchronizacji: " + (t.LastSyncAt == DateTime.MinValue ? "nigdy" : t.LastSyncAt.ToString("dd.MM.yyyy HH:mm"));
            TaskRuntime rt;
            if (!t.Unsaved && !t.AutoArmed && (t.IntervalMinutes > 0 || t.WatchChanges))
                s += "     ⏸ Automatyka czeka – kliknij „Synchronizuj”, aby ją uruchomić";
            else if (!t.Unsaved && t.Enabled && cfg.AutomationEnabled && t.IntervalMinutes > 0 &&
                runtimes.TryGetValue(t, out rt) && rt.NextRun != DateTime.MinValue)
                s += "     Następna: " + rt.NextRun.ToString("HH:mm");
            lblInfo.Text = s;
        }

        void LoadEditor()
        {
            if (loading) return;
            var t = Current;
            editing = t;
            dirty = false;
            loading = true;
            grpEdit.Enabled = t != null;
            btnPreview.Enabled = btnSync.Enabled = t != null && !busy;
            if (t != null)
            {
                txtName.Text = t.Name;
                txtLeft.Text = t.Left;
                txtRight.Text = t.Right;
                txtExcl.Text = t.Excludes;
                cmbDir.SelectedIndex = (int)t.Direction;
                cmbDelete.SelectedIndex = (int)t.DeleteMode;
                chkMirror.Checked = t.Mirror;
                chkEnabled.Checked = t.Enabled;
                chkInterval.Checked = t.IntervalMinutes > 0;
                numInterval.Value = t.IntervalMinutes > 0 ? Math.Min(10080, t.IntervalMinutes) : 15;
                chkWatch.Checked = t.WatchChanges;
                chkWersje.Checked = t.ZachowajPoprzednieWersje;
                int ci = Array.IndexOf(CZASY_CISZY, CiszaSekund(t));
                cmbWatchDelay.SelectedIndex = ci >= 0 ? ci : 0;
                chkSubFolder.Checked = t.SubFolder;
            }
            else
            {
                txtName.Text = txtLeft.Text = txtRight.Text = txtExcl.Text = "";
            }
            UpdateModeControls();
            OdswiezLinkStalejDecyzji();
            loading = false;
            UpdateEditorState();
            if (!busy) ShowHistory(t);
        }

        // Link "Stała decyzja: ..." obok Zapisz - widoczny tylko, gdy zadanie ma zapamiętaną
        // decyzję o znikniętych; kliknięcie ją wycofuje (program znowu zacznie pytać).
        void OdswiezLinkStalejDecyzji()
        {
            if (linkStalaDecyzja == null) return;
            var t = editing;
            bool jest = t != null && t.StalaDecyzjaZnikniete.Length > 0;
            linkStalaDecyzja.Visible = jest;
            if (jest)
                linkStalaDecyzja.Text = "Stała decyzja: " +
                    (t.StalaDecyzjaZnikniete == "przywroc" ? "PRZYWRACAJ zniknięte" : "USUWAJ także po drugiej stronie") +
                    "  (kliknij, aby wycofać)";
        }

        void UpdateModeControls()
        {
            bool twoWay = cmbDir.SelectedIndex == (int)SyncDirection.TwoWay;
            chkMirror.Enabled = !twoWay;
            cmbDelete.Enabled = !twoWay && chkMirror.Checked;
            chkMirror.ForeColor = chkMirror.Checked && !twoWay ? Color.DarkRed : SystemColors.ControlText;
            numInterval.Enabled = chkInterval.Checked;
            cmbWatchDelay.Enabled = chkWatch.Checked;
            if (arrow != null)
            {
                arrow.Mode = Math.Max(0, cmbDir.SelectedIndex);
                lblDirMode.Text = cmbDir.SelectedIndex == 1 ? "Prawy  →  Lewy" : cmbDir.SelectedIndex == 2 ? "Dwukierunkowo  ⇄" : "Lewy  →  Prawy";
            }
            // Domyslnie LEWY to zrodlo, PRAWY to cel (czytamy od lewej do prawej) - ale przy "Prawy -> Lewy"
            // role sie odwracaja, a "Zamien foldery miejscami" nie zmienia kierunku, wiec etykieta zostaje trafna.
            if (lblLewyTytul != null && lblPrawyTytul != null)
            {
                string sufLewy = cmbDir.SelectedIndex == 0 ? " – ŹRÓDŁO" : cmbDir.SelectedIndex == 1 ? " – CEL" : "";
                string sufPrawy = cmbDir.SelectedIndex == 0 ? " – CEL" : cmbDir.SelectedIndex == 1 ? " – ŹRÓDŁO" : "";
                lblLewyTytul.Text = "Folder LEWY" + sufLewy;
                lblPrawyTytul.Text = "Folder PRAWY" + sufPrawy;
            }
            UpdateTargetLabel();
        }

        void EditorChanged()
        {
            if (loading) return;
            UpdateModeControls();
            if (editing == null) return;
            // zmiany trafiają do zadania dopiero po kliknięciu "Zapisz"
            dirty = true;
            UpdateEditorState();
        }

        void SaveConfig()
        {
            watchersDirty = true;
            try { cfg.Save(); }
            catch (Exception ex) { SetStatus("Nie można zapisać konfiguracji: " + ex.Message); }
        }

        void AddTask(SyncTask t)
        {
            if (!ConfirmLeave()) return;
            t.Unsaved = true;
            cfg.Tasks.Add(t);
            dirty = false;
            editing = null;
            RefreshTaskList(cfg.Tasks.Count - 1);
            dirty = true;                 // nowe zadanie czeka na "Zapisz"
            UpdateEditorState();
            txtName.Focus();
            txtName.SelectAll();
        }

        void DuplicateTask()
        {
            var c = editing;
            if (c == null || c.Unsaved) return;
            AddTask(new SyncTask
            {
                Name = c.Name + " (kopia)", Left = c.Left, Right = c.Right, Direction = c.Direction,
                Mirror = c.Mirror, DeleteMode = c.DeleteMode, Excludes = c.Excludes, Enabled = c.Enabled,
                IntervalMinutes = c.IntervalMinutes, WatchChanges = c.WatchChanges,
                WatchDelaySeconds = c.WatchDelaySeconds, SubFolder = c.SubFolder,
                ZachowajPoprzednieWersje = c.ZachowajPoprzednieWersje, ArchiwumDni = c.ArchiwumDni
            });
        }

        void DeleteTask()
        {
            var c = Current;
            if (c == null) return;

            // Uwaga: UZYWAMY ścieżki ROZWIAZANEJ (Resolved) - dokladnie tej, ktora widac jako "Cel:"
            // w edytorze. Surowe Left/Right z konfiguracji to co innego, gdy wlaczony jest podfolder -
            // usuwanie surowej wartosci mogloby trafic np. w korzen calego dysku.
            var r = c.Resolved();
            string lewy = "", prawy = "";
            try { lewy = Engine.Norm(r.Left); } catch { lewy = r.Left ?? ""; }
            try { prawy = Engine.Norm(r.Right); } catch { prawy = r.Right ?? ""; }
            bool korzenLewy = lewy.Length > 0 && lewy.TrimEnd('\\').Length <= 3;    // np. "X:" - caly dysk
            bool korzenPrawy = prawy.Length > 0 && prawy.TrimEnd('\\').Length <= 3;
            bool maLewy = lewy.Length > 0 && Directory.Exists(lewy) && !korzenLewy;
            bool maPrawy = prawy.Length > 0 && Directory.Exists(prawy) && !korzenPrawy;
            // Etykiety "(zrodlo)"/"(cel)" - zeby nie trzeba bylo zgadywac po samej geometrii lewy/prawy.
            string opisLewy = c.Direction == SyncDirection.LeftToRight ? " (źródło)" : c.Direction == SyncDirection.RightToLeft ? " (cel)" : "";
            string opisPrawy = c.Direction == SyncDirection.LeftToRight ? " (cel)" : c.Direction == SyncDirection.RightToLeft ? " (źródło)" : "";

            RadioButton rbZostaw, rbLewy, rbPrawy, rbOba, rbKosz, rbTrwale;
            GroupBox gbJak;
            DialogResult wynik;
            using (var dlg = new Form
            {
                Text = "Usuń zadanie", Size = new Size(640, 400), MinimumSize = new Size(640, 400),
                StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false,
                FormBorderStyle = FormBorderStyle.FixedDialog, Icon = AppIcon()
            })
            {
                var naglowek = new Label
                {
                    Dock = DockStyle.Top, Height = 46, Padding = new Padding(16, 12, 12, 0),
                    Font = new Font("Segoe UI", 11f, FontStyle.Bold),
                    Text = "Usunąć zadanie \"" + c.Name + "\" z listy?"
                };

                var gbCo = new GroupBox { Text = "Co zrobić z folderami na dysku?", Dock = DockStyle.Top, Height = 150, Padding = new Padding(10, 8, 10, 0) };
                rbZostaw = new RadioButton { Text = "Zostaw oba foldery bez zmian (najbezpieczniejsze)", AutoSize = true, Checked = true, Dock = DockStyle.Top, Margin = new Padding(0, 4, 0, 4) };
                rbLewy = new RadioButton { Text = "Usuń też folder LEWY" + opisLewy + " z dysku:  " + (korzenLewy ? "(cały dysk - pominięte)" : lewy.Length > 0 ? lewy : "(nie ustawiono)"), AutoSize = true, Enabled = maLewy, Dock = DockStyle.Top, Margin = new Padding(0, 4, 0, 4) };
                rbPrawy = new RadioButton { Text = "Usuń też folder PRAWY" + opisPrawy + " z dysku:  " + (korzenPrawy ? "(cały dysk - pominięte)" : prawy.Length > 0 ? prawy : "(nie ustawiono)"), AutoSize = true, Enabled = maPrawy, Dock = DockStyle.Top, Margin = new Padding(0, 4, 0, 4) };
                rbOba = new RadioButton { Text = "Usuń OBA foldery z dysku", AutoSize = true, Enabled = maLewy || maPrawy, Dock = DockStyle.Top, Margin = new Padding(0, 4, 0, 4) };
                gbCo.Controls.Add(rbOba); gbCo.Controls.Add(rbPrawy); gbCo.Controls.Add(rbLewy); gbCo.Controls.Add(rbZostaw);

                gbJak = new GroupBox { Text = "Jak usunąć?", Dock = DockStyle.Top, Height = 80, Padding = new Padding(10, 8, 10, 0), Enabled = false };
                rbKosz = new RadioButton { Text = "Do Kosza Windows (bezpieczne, można cofnąć)", AutoSize = true, Checked = true, Dock = DockStyle.Top, Margin = new Padding(0, 4, 0, 4) };
                rbTrwale = new RadioButton { Text = "Trwale – bez możliwości cofnięcia", AutoSize = true, ForeColor = Color.FromArgb(170, 30, 30), Dock = DockStyle.Top, Margin = new Padding(0, 4, 0, 4) };
                gbJak.Controls.Add(rbTrwale); gbJak.Controls.Add(rbKosz);

                EventHandler odswiezJak = delegate { gbJak.Enabled = !rbZostaw.Checked; };
                rbZostaw.CheckedChanged += odswiezJak; rbLewy.CheckedChanged += odswiezJak;
                rbPrawy.CheckedChanged += odswiezJak; rbOba.CheckedChanged += odswiezJak;

                var dol = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 50, Padding = new Padding(12, 10, 0, 0) };
                var btnUsun = MkButton("Usuń zadanie", 140, null);
                btnUsun.DialogResult = DialogResult.OK;
                btnUsun.Font = new Font(btnUsun.Font, FontStyle.Bold);
                var btnAnuluj = MkButton("Anuluj", 100, null);
                btnAnuluj.DialogResult = DialogResult.Cancel;
                foreach (var b in new[] { btnUsun, btnAnuluj }) { b.Height = 34; b.Margin = new Padding(0, 0, 8, 0); }
                dol.Controls.AddRange(new Control[] { btnUsun, btnAnuluj });

                dlg.Controls.Add(gbJak);
                dlg.Controls.Add(gbCo);
                dlg.Controls.Add(naglowek);
                dlg.Controls.Add(dol);
                dlg.AcceptButton = btnUsun;
                dlg.CancelButton = btnAnuluj;
                wynik = dlg.ShowDialog(this);

                if (wynik != DialogResult.OK) return;

                bool usunLewy = (rbLewy.Checked || rbOba.Checked) && maLewy;
                bool usunPrawy = (rbPrawy.Checked || rbOba.Checked) && maPrawy;
                if (usunLewy || usunPrawy)
                {
                    var lista = new List<string>();
                    if (usunLewy) lista.Add("LEWY:   " + lewy);
                    if (usunPrawy) lista.Add("PRAWY:  " + prawy);
                    bool trwale = rbTrwale.Checked;
                    var potw = MessageBox.Show(this,
                        "Na pewno usunąć z dysku poniższe foldery razem z ich całą zawartością?\n\n" + string.Join("\n", lista) +
                        "\n\n" + (trwale ? "To jest USUWANIE TRWAŁE – bez możliwości cofnięcia."
                                         : "Trafią do Kosza Windows – da się je stamtąd przywrócić."),
                        "Potwierdź usuwanie folderów", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
                    if (potw != DialogResult.Yes) return;

                    foreach (var para in new[] { new { Rob = usunLewy, Sciezka = lewy, Strona = "lewy" }, new { Rob = usunPrawy, Sciezka = prawy, Strona = "prawy" } })
                    {
                        if (!para.Rob) continue;
                        try
                        {
                            if (trwale) Archiwum.UsunDrzewo(para.Sciezka);
                            else Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(para.Sciezka,
                                Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                            AppendLog("Usunięto z dysku folder " + para.Strona + " zadania \"" + c.Name + "\": " + para.Sciezka +
                                       (trwale ? " (trwale)" : " (do Kosza)"));
                        }
                        catch (Exception ex)
                        {
                            AppendLog("BŁĄD usuwania folderu " + para.Strona + " zadania \"" + c.Name + "\": " + ex.Message);
                            MessageBox.Show(this, "Nie udało się usunąć folderu " + para.Strona + ":\n" + para.Sciezka + "\n\n" + ex.Message,
                                "Błąd usuwania", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                    }
                }
            }

            int i = lstTasks.SelectedIndex;
            cfg.Tasks.Remove(c);
            dirty = false;
            editing = null;
            SaveConfig();
            RefreshTaskList(i);
        }

        void MoveTask(int delta)
        {
            int i = lstTasks.SelectedIndex, j = i + delta;
            if (i < 0 || j < 0 || j >= cfg.Tasks.Count) return;
            if (!ConfirmLeave()) return;
            if (!cfg.Tasks.Contains(editing)) { editing = null; RefreshTaskList(i); return; }
            var t = cfg.Tasks[i];
            cfg.Tasks.RemoveAt(i);
            cfg.Tasks.Insert(j, t);
            SaveConfig();
            RefreshTaskList(j);
        }

        void Browse(TextBox target)
        {
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = "Wybierz folder (dyski sieciowe: rozwiń „Sieć” lub wpisz ścieżkę \\\\serwer\\udział ręcznie)";
                dlg.ShowNewFolderButton = true;
                try { if (Directory.Exists(target.Text)) dlg.SelectedPath = target.Text; } catch { }
                if (dlg.ShowDialog(this) == DialogResult.OK) target.Text = dlg.SelectedPath;
            }
        }

        void TestPath(string path)
        {
            SetStatus("Sprawdzanie dostępu do: " + path + " ...");
            ThreadPool.QueueUserWorkItem(delegate
            {
                string msg;
                try
                {
                    string p = Engine.Norm(path);
                    if (p.Length == 0) msg = "Nie podano ścieżki.";
                    else if (!Directory.Exists(p)) msg = "❌ Folder niedostępny lub nie istnieje: " + p;
                    else
                    {
                        int n = new DirectoryInfo(p).GetFileSystemInfos().Length;
                        string probe = Path.Combine(p, ".synctest_" + Guid.NewGuid().ToString("N").Substring(0, 8));
                        string write;
                        try { File.WriteAllText(probe, "test"); File.Delete(probe); write = "zapis OK"; }
                        catch (Exception ex) { write = "BRAK prawa zapisu (" + ex.Message + ")"; }
                        msg = "✔ Folder dostępny: " + p + " – " + n + " elementów, " + write;
                    }
                }
                catch (Exception ex) { msg = "❌ " + ex.Message; }
                BeginInvoke((Action)(() => SetStatus(msg)));
            });
        }

        // ---------- uruchamianie

        void StartSelected(bool execute)
        {
            if (editing == null) return;
            if (dirty)
            {
                if (MessageBox.Show(this, "Najpierw trzeba zapisać zmiany w zadaniu. Zapisać teraz?", "Niezapisane zmiany",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                if (!SaveEditor()) return;
            }
            StartWork(new List<SyncTask> { editing }, execute, true, false);
        }

        void RunAuto()
        {
            List<SyncTask> list;
            if (string.IsNullOrEmpty(autoTaskName)) list = cfg.Tasks.Where(x => x.Enabled).ToList();
            else list = cfg.Tasks.Where(x => x.Name.Equals(autoTaskName, StringComparison.OrdinalIgnoreCase)).ToList();
            if (list.Count == 0)
            {
                AppendLog("Brak zadań do uruchomienia (" + (autoTaskName ?? "wszystkie włączone") + ").");
                exitCode = 2;
                Close();
                return;
            }
            StartWork(list, true, false, true);
        }

        void SetBusy(bool b, bool background = false)
        {
            busy = b;
            if (b) StartTrayAnimation(); else StopTrayAnimation();
            if (b) StartHeaderAnim(); else StopHeaderAnim();
            progress.Animate = b;
            btnPreview.Enabled = btnSync.Enabled = !b && Current != null;
            btnRunAll.Enabled = !b;
            btnCancel.Enabled = b;
            if (btnDeleteFiles != null) UpdateSelectionInfo();
            lstTasks.Enabled = !b || background;
            grpEdit.Enabled = (!b || background) && Current != null;
            if (b) progress.Value = 0;
            lblSpeed.Text = "";
        }

        // ---------- bajery: wirujace logo w naglowku podczas pracy
        void StartHeaderAnim()
        {
            if (headerAnimTimer == null)
            {
                headerAnimTimer = new System.Windows.Forms.Timer { Interval = 40 };
                headerAnimTimer.Tick += delegate { headerRot = (headerRot + 8f) % 360f; headerPanel.Invalidate(new Rectangle(8, 2, 56, 56)); };
            }
            headerAnimTimer.Start();
        }
        void StopHeaderAnim()
        {
            if (headerAnimTimer != null) headerAnimTimer.Stop();
            headerRot = 0f;
            if (headerPanel != null) headerPanel.Invalidate(new Rectangle(8, 2, 56, 56));
        }

        // ---------- bajery: kolorowa kropka statusu i chwilowe podswietlenie wiersza po auto-syncu
        Color StatusDotColor(SyncTask t)
        {
            if (running.Contains(t)) return Color.FromArgb(30, 120, 230);
            if (t.CzekaDecyzji > 0) return Color.FromArgb(235, 175, 30);
            if (t.Unsaved || !t.Enabled) return Color.FromArgb(165, 165, 165);
            if (!string.IsNullOrEmpty(t.LastSyncResult) &&
                (t.LastSyncResult.StartsWith("BŁĄD") || t.LastSyncResult.IndexOf("błędów", StringComparison.Ordinal) >= 0))
                return Color.FromArgb(205, 40, 40);
            if (t.LastSuccessAt == DateTime.MinValue) return Color.FromArgb(195, 195, 195);
            return Color.FromArgb(40, 160, 70);
        }

        void LstTasks_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= lstTasks.Items.Count) return;
            var t = lstTasks.Items[e.Index] as SyncTask;
            bool selected = (e.State & DrawItemState.Selected) != 0;
            DateTime flashDo;
            bool flash = t != null && flashUntil.TryGetValue(t, out flashDo) && DateTime.Now < flashDo;
            using (var b = new SolidBrush(flash && !selected ? Color.FromArgb(220, 236, 255) : selected ? SystemColors.Highlight : lstTasks.BackColor))
                e.Graphics.FillRectangle(b, e.Bounds);
            if (t != null)
            {
                const int d = 10;
                int cy = e.Bounds.Y + (e.Bounds.Height - d) / 2;
                using (var db = new SolidBrush(StatusDotColor(t))) e.Graphics.FillEllipse(db, e.Bounds.X + 6, cy, d, d);
                using (var dp = new Pen(Color.FromArgb(70, 0, 0, 0))) e.Graphics.DrawEllipse(dp, e.Bounds.X + 6, cy, d, d);
                var fg = selected ? SystemColors.HighlightText : lstTasks.ForeColor;
                var textRect = new Rectangle(e.Bounds.X + 6 + d + 6, e.Bounds.Y, e.Bounds.Width - (d + 12), e.Bounds.Height);
                TextRenderer.DrawText(e.Graphics, t.ToString(), lstTasks.Font, textRect, fg,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
            e.DrawFocusRectangle();
        }

        void OznaczMigniecie(SyncTask t)
        {
            flashUntil[t] = DateTime.Now.AddMilliseconds(1400);
            if (flashTimer == null)
            {
                flashTimer = new System.Windows.Forms.Timer { Interval = 150 };
                flashTimer.Tick += delegate
                {
                    var teraz = DateTime.Now;
                    foreach (var k in flashUntil.Where(kv => kv.Value <= teraz).Select(kv => kv.Key).ToList()) flashUntil.Remove(k);
                    lstTasks.Invalidate();
                    if (flashUntil.Count == 0) flashTimer.Stop();
                };
            }
            flashTimer.Start();
            lstTasks.Invalidate();
        }

        // ---------- bajery: pasek decyzji wsuwa/wysuwa sie zamiast pojawiac skokowo
        System.Windows.Forms.Timer decyzjeAnimTimer;
        int decyzjeCel;
        const int WysokoscPanelDecyzji = 46;
        void AnimujPanelDecyzji(bool pokaz)
        {
            decyzjeCel = pokaz ? WysokoscPanelDecyzji : 0;
            if (pokaz) panelDecyzji.Visible = true;
            if (decyzjeAnimTimer == null)
            {
                decyzjeAnimTimer = new System.Windows.Forms.Timer { Interval = 12 };
                decyzjeAnimTimer.Tick += delegate
                {
                    int h = panelDecyzji.Height;
                    if (h < decyzjeCel) h = Math.Min(decyzjeCel, h + 8);
                    else if (h > decyzjeCel) h = Math.Max(decyzjeCel, h - 8);
                    panelDecyzji.Height = h;
                    if (h == decyzjeCel)
                    {
                        decyzjeAnimTimer.Stop();
                        if (decyzjeCel == 0) panelDecyzji.Visible = false;
                    }
                };
            }
            decyzjeAnimTimer.Start();
        }

        void SetStatus(string s) { pendingStatus = null; lblStatus.Text = s; }

        // ---------- dziennik: wpisy zbierane w kolejce i zapisywane paczkami (okno + plik) kilka razy na sekundę
        readonly System.Collections.Concurrent.ConcurrentQueue<string> logQueue = new System.Collections.Concurrent.ConcurrentQueue<string>();
        System.Windows.Forms.Timer logTimer;
        volatile string pendingStatus;

        // bezpieczne z każdego wątku
        void AppendLog(string s)
        {
            logQueue.Enqueue(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + s);
        }

        void QueueStatus(string s) { pendingStatus = s; }

        void EnsureLogTimer()
        {
            if (logTimer != null) return;
            logTimer = new System.Windows.Forms.Timer { Interval = 200 };
            logTimer.Tick += delegate { FlushLog(); };
            logTimer.Start();
        }

        void FlushLog()
        {
            var st = pendingStatus;
            if (st != null) { pendingStatus = null; lblStatus.Text = st; }
            if (logQueue.IsEmpty) return;

            var wiersze = new List<string>();
            var file = new StringBuilder();
            string line;
            while (logQueue.TryDequeue(out line))
            {
                file.AppendLine(line);
                wiersze.Add(line.Substring(11));   // w oknie bez daty, zostaje "HH:mm:ss  wiadomosc"
            }
            int n = wiersze.Count;

            // W oknie najnowsze na górze. Kazda linia wstawiana jest na sam poczatek (pozycja 0),
            // wiec wstawiajac paczke od NAJSTARSZEJ do najnowszej, ta ostatnia konczy na samej gorze -
            // a juz wstawiony, kolorowany wczesniej tekst zostaje nietkniety (w przeciwienstwie do
            // zwyklego TextBox, gdzie trzeba by bylo za kazdym razem sklejac cala tresc od nowa).
            const int limitOkna = 500000;   // znakow w oknie - plik dziennika ma caly log bez limitu
            int ile = Math.Min(n, 3000);
            txtLog.SuspendLayout();
            if (n > ile)
                WstawLinie("        ", "  ... (" + (n - ile) + " wpisów pominiętych w oknie – pełna lista w pliku dziennika)");
            for (int k = n - ile; k <= n - 1; k++)
            {
                string czas = wiersze[k].Substring(0, 8);
                string tresc = wiersze[k].Substring(8);
                WstawLinie(czas == ostatniPokazanyCzas ? "        " : czas, tresc);
                ostatniPokazanyCzas = czas;
            }
            if (txtLog.TextLength > limitOkna)
            {
                string all = txtLog.Text;
                int cut = all.LastIndexOf('\n', Math.Min(limitOkna, all.Length - 1));
                if (cut > 0) { txtLog.Select(cut, all.Length - cut); txtLog.SelectedText = ""; }
            }
            txtLog.SelectionStart = 0;
            txtLog.SelectionLength = 0;
            txtLog.ScrollToCaret();
            txtLog.ResumeLayout();

            try
            {
                if (logFile == null)
                {
                    string dir = Path.Combine(Config.AppDir, "logi");
                    Directory.CreateDirectory(dir);
                    logFile = new StreamWriter(Path.Combine(dir, DateTime.Now.ToString("yyyy-MM-dd") + ".log"), true, Encoding.UTF8, 1 << 16);
                }
                logFile.Write(file.ToString());
                logFile.Flush();
            }
            catch { }
        }

        string ostatniPokazanyCzas = "";

        // Wstawia jeden kolorowany wiersz na sam poczatek dziennika (pozycja 0) - dzieki temu
        // powtarzajace sie wywolania zostawiaja NOWSZE wpisy zawsze na samej gorze.
        void WstawLinie(string czas, string tresc)
        {
            bool blankBefore = tresc.TrimStart().StartsWith("===");
            txtLog.SelectionStart = 0;
            txtLog.SelectionLength = 0;
            if (blankBefore)
            {
                txtLog.SelectionColor = Color.Black;
                txtLog.SelectedText = "\r\n";
                txtLog.SelectionStart = 0;
                txtLog.SelectionLength = 0;
            }
            bool bold;
            Color kolor = KolorWiersza(tresc, out bold);
            txtLog.SelectionColor = Color.FromArgb(140, 140, 140);
            txtLog.SelectionFont = txtLog.Font;
            txtLog.SelectedText = czas;
            txtLog.SelectionColor = kolor;
            txtLog.SelectionFont = new Font(txtLog.Font, bold ? FontStyle.Bold : FontStyle.Regular);
            txtLog.SelectedText = tresc + "\r\n";
        }

        // Typ wpisu -> kolor/pogrubienie, zeby dziennik dalo sie przeczesac wzrokiem zamiast
        // czytac linijka po linijce identyczna czcionka.
        static Color KolorWiersza(string tresc, out bool bold)
        {
            bold = false;
            string m = tresc.TrimStart();
            if (m.StartsWith("===")) { bold = true; return Color.FromArgb(0, 70, 160); }
            if (m.StartsWith("⚠")) { bold = true; return Color.FromArgb(190, 110, 0); }
            if (m.IndexOf("BŁĄD", StringComparison.Ordinal) >= 0) return Color.FromArgb(180, 20, 20);
            if (m.StartsWith("Kopiuj →")) return Color.FromArgb(0, 60, 150);
            if (m.StartsWith("Kopiuj ←")) return Color.FromArgb(0, 110, 40);
            if (m.StartsWith("Usuń") || m.StartsWith("URATOWANE")) return Color.FromArgb(170, 30, 30);
            if (m.StartsWith("Zakończono:"))
            {
                bold = true;
                return m.IndexOf("0 błędów", StringComparison.Ordinal) >= 0 ? Color.FromArgb(0, 110, 40) : Color.FromArgb(190, 110, 0);
            }
            if (m.StartsWith("Zaplanowano") || m.StartsWith("Obserwuję zmiany") || m.StartsWith("Zapisano zadanie") ||
                m.StartsWith("Automatyka") || m.StartsWith("Program uruchomiony") || m.StartsWith("Skanowanie") ||
                m.StartsWith("Pominięto") || m.StartsWith("Brak zmian") || m.StartsWith("Do skopiowania") ||
                m.StartsWith("Lewy:") || m.StartsWith("..."))
                return Color.FromArgb(120, 120, 120);
            return Color.FromArgb(25, 25, 25);
        }


        void Ui(Action a)
        {
            if (IsDisposed) return;
            try { BeginInvoke(a); } catch { }
        }

        internal static string Rozmiar(long b) { return FmtSize(b); }

        static string FmtSize(long b)
        {
            if (b < 1024) return b + " B";
            if (b < 1024 * 1024) return (b / 1024.0).ToString("0.0") + " KB";
            if (b < 1024L * 1024 * 1024) return (b / 1048576.0).ToString("0.0") + " MB";
            return (b / 1073741824.0).ToString("0.00") + " GB";
        }

        static string Summary(List<SyncAction> acts)
        {
            var copies = acts.Where(a => a.Type == ActionType.Copy).ToList();
            int dirs = acts.Count(a => a.Type == ActionType.CreateDir);
            int dels = acts.Count(a => a.Type == ActionType.Delete);
            return "Do skopiowania: " + copies.Count + " plików (" + FmtSize(copies.Sum(a => a.Size)) + "), " +
                   "nowych folderów: " + dirs + ", do usunięcia: " + dels +
                   ", bez zmian: " + acts.Count(a => a.Type == ActionType.Same);
        }

        static string FmtTime(DateTime? utc)
        {
            return utc.HasValue ? utc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") : "";
        }

        int sortColumn = -1;
        bool sortDesc;

        void SortActions(int col)
        {
            if (col == sortColumn) sortDesc = !sortDesc;
            else { sortColumn = col; sortDesc = col == 2 || col == 3 || col == 4; }  // rozmiar i daty: od największych/najnowszych
            for (int i = 0; i < lvActions.Columns.Count; i++)
            {
                string h = lvActions.Columns[i].Text.TrimEnd(' ', '▲', '▼');
                lvActions.Columns[i].Text = i == col ? h + (sortDesc ? " ▼" : " ▲") : h;
            }
            RenderForest();
        }

        // foldery zawsze przed plikami (jak w Eksploratorze); w ich obrębie - kolumna wybrana kliknięciem nagłówka
        int CompareNodes(ActionNode x, ActionNode y)
        {
            if (x.IsFolder != y.IsFolder) return x.IsFolder ? -1 : 1;
            if (sortColumn >= 0)
            {
                var a = x.EffectiveAction; var b = y.EffectiveAction;
                int r;
                switch (sortColumn)
                {
                    case 0: r = a != null && b != null ? a.Type.CompareTo(b.Type) : 0; break;
                    case 2: r = (a != null && a.Type >= ActionType.Copy ? a.Size : -1).CompareTo(b != null && b.Type >= ActionType.Copy ? b.Size : -1); break;
                    case 3: r = a != null && b != null ? Nullable.Compare(a.SrcTime, b.SrcTime) : 0; break;
                    case 4: r = a != null && b != null ? Nullable.Compare(a.DstTime, b.DstTime) : 0; break;
                    default: r = string.Compare(x.Name, y.Name, StringComparison.CurrentCultureIgnoreCase); break;
                }
                if (r != 0) return sortDesc ? -r : r;
            }
            return string.Compare(x.Name, y.Name, StringComparison.CurrentCultureIgnoreCase);
        }

        // ostatnio pokazane dane – do przełączania "pokaż pliki bez zmian"
        readonly List<Tuple<SyncTask, List<SyncAction>, bool>> shown = new List<Tuple<SyncTask, List<SyncAction>, bool>>();
        readonly List<ActionNode> treeForest = new List<ActionNode>();          // drzewo folderów/plików aktualnie pokazane
        readonly HashSet<string> collapsedKeys = new HashSet<string>();        // zapamiętane zwinięte foldery (przetrwa odświeżenie)
        readonly HashSet<string> initializedFolders = new HashSet<string>();   // foldery, dla których już ustawiono stan domyślny (zwinięty)
        CheckBox chkShowSame;

        // wyszukiwanie pliku/folderu po nazwie na liście "Podgląd zmian"
        readonly List<ActionNode> searchMatches = new List<ActionNode>();
        int searchIndex = -1;
        bool searchDirty = true;

        // Filtr na żywo (jak w Eksploratorze): drzewo pokazuje tylko dopasowane pozycje,
        // ich przodków (żeby było widać gdzie leżą) i całą zawartość dopasowanego folderu.
        string treeFilter = "";
        HashSet<ActionNode> filterVisible;   // null = filtr wyłączony

        bool MarkFilterVisible(ActionNode n)
        {
            bool self = !n.IsTaskHeader && n.Name.IndexOf(treeFilter, StringComparison.OrdinalIgnoreCase) >= 0;
            bool any = self;
            if (n.Children != null)
                foreach (var c in n.Children)
                    if (self) AddSubtree(c);                    // dopasowany folder → pokaż wszystko w środku
                    else any |= MarkFilterVisible(c);
            if (any) filterVisible.Add(n);
            return any;
        }

        void AddSubtree(ActionNode n)
        {
            filterVisible.Add(n);
            if (n.Children != null) foreach (var c in n.Children) AddSubtree(c);
        }

        void FindNextInTree()
        {
            string term = txtSearchTree.Text.Trim();
            if (term.Length == 0) { lblSearchInfo.Text = ""; return; }
            if (searchDirty)
            {
                searchMatches.Clear();
                foreach (var r in treeForest) CollectMatches(r, term, searchMatches);
                searchIndex = -1;
                searchDirty = false;
            }
            if (searchMatches.Count == 0) { lblSearchInfo.Text = "Brak wyników"; return; }
            searchIndex = (searchIndex + 1) % searchMatches.Count;
            GoToNode(searchMatches[searchIndex]);
            lblSearchInfo.Text = (searchIndex + 1) + " / " + searchMatches.Count;
        }

        static void CollectMatches(ActionNode node, string term, List<ActionNode> outList)
        {
            if (!node.IsTaskHeader && node.Name.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0) outList.Add(node);
            if (node.Children != null) foreach (var c in node.Children) CollectMatches(c, term, outList);
        }

        void GoToNode(ActionNode node)
        {
            for (var p = node.Parent; p != null; p = p.Parent) collapsedKeys.Remove(p.Key);
            RenderForest();
            foreach (ListViewItem it in lvActions.Items) it.Selected = ReferenceEquals(it.Tag, node);
            foreach (ListViewItem it in lvActions.Items)
                if (ReferenceEquals(it.Tag, node)) { it.EnsureVisible(); lvActions.TopItem = it; break; }
        }

        void RerenderActions()
        {
            var copy = shown.ToList();
            shown.Clear();
            treeForest.Clear();
            lvActions.Items.Clear();
            lblSummary.Text = "";
            foreach (var s in copy) ShowActions(s.Item1, s.Item2, s.Item3);
        }

        void ToggleFolder(ActionNode node)
        {
            if (!node.IsFolder) return;
            if (!collapsedKeys.Remove(node.Key)) collapsedKeys.Add(node.Key);
            RenderForest();
        }

        void CollapseAllFolders()
        {
            foreach (var r in treeForest) CollapseWalk(r);
            RenderForest();
        }

        private void btnSprawdzLewy_Click(object sender, EventArgs e)
        {

        }

        void ExpandAllFolders()
        {
            collapsedKeys.Clear();
            RenderForest();
        }

        void CollapseWalk(ActionNode n)
        {
            if (n.IsFolder) collapsedKeys.Add(n.Key);
            if (n.Children != null) foreach (var c in n.Children) CollapseWalk(c);
        }

        // nowy folder startuje domyślnie zwinięty (czytelniejsza lista); raz ustawiony stan
        // zapamiętujemy, żeby kolejne odświeżenia nie kasowały wyboru użytkownika
        void DefaultCollapse(ActionNode node)
        {
            if (initializedFolders.Add(node.Key)) collapsedKeys.Add(node.Key);
        }

        ActionNode GetOrAddFolder(ActionNode parent, string name, string rel, SyncTask t)
        {
            ActionNode node;
            if (!parent.ChildIndex.TryGetValue(name, out node))
            {
                node = new ActionNode { Name = name, Rel = rel, IsFolder = true, Task = t, Key = t.Name + "\u0001" + rel, Parent = parent,
                    Children = new List<ActionNode>(), ChildIndex = new Dictionary<string, ActionNode>(StringComparer.OrdinalIgnoreCase) };
                parent.ChildIndex[name] = node;
                parent.Children.Add(node);
                DefaultCollapse(node);
            }
            else if (node.ChildIndex == null)
            {
                // ta nazwa była wcześniej wpięta jako PLIK (para akcji "zmiana plik/folder") -
                // awansujemy węzeł na folder, inaczej wpinanie jego dzieci wywala NullReference
                node.IsFolder = true;
                node.Children = new List<ActionNode>();
                node.ChildIndex = new Dictionary<string, ActionNode>(StringComparer.OrdinalIgnoreCase);
                DefaultCollapse(node);
            }
            return node;
        }

        // wpina jedną akcję (plik albo folder) do drzewa, tworząc po drodze brakujące węzły-foldery
        void InsertAction(ActionNode root, SyncAction a, SyncTask t)
        {
            if (a == null || a.Rel == null) return;   // uszkodzony wpis (np. stara historia) - pomiń zamiast wywalać program
            var parts = a.Rel.Split('\\');
            var cur = root;
            string relSoFar = "";
            for (int i = 0; i < parts.Length - 1; i++)
            {
                relSoFar = relSoFar.Length == 0 ? parts[i] : relSoFar + "\\" + parts[i];
                cur = GetOrAddFolder(cur, parts[i], relSoFar, t);
            }
            string leafName = parts[parts.Length - 1];
            if (a.Type == ActionType.CreateDir)
            {
                GetOrAddFolder(cur, leafName, a.Rel, t).Action = a;
            }
            else
            {
                ActionNode node;
                if (!cur.ChildIndex.TryGetValue(leafName, out node))
                {
                    node = new ActionNode { Name = leafName, Rel = a.Rel, IsFolder = false, Task = t, Key = t.Name + "\u0001" + a.Rel, Parent = cur };
                    cur.ChildIndex[leafName] = node;
                    cur.Children.Add(node);
                }
                node.Action = a;
            }
        }

        // suma rozmiarów plików w folderze (rekurencyjnie) - żeby dało się ocenić, co się usuwa
        static long ComputeSize(ActionNode node)
        {
            long total = !node.IsFolder && node.Action != null ? node.Action.Size : 0;
            if (node.Children != null) foreach (var c in node.Children) total += ComputeSize(c);
            node.Size = total;
            return total;
        }

        void RenderForest()
        {
            // zapamiętaj widok (górny wiersz + zaznaczenie), żeby zwijanie/rozwijanie folderu
            // nie przeskakiwało listy z powrotem na samą górę
            string topKey = null;
            var topItem = lvActions.TopItem;
            if (topItem != null && topItem.Tag is ActionNode) topKey = ((ActionNode)topItem.Tag).Key;
            var selectedKeys = new HashSet<string>();
            foreach (ListViewItem sel in lvActions.SelectedItems)
            {
                var n = sel.Tag as ActionNode;
                if (n != null) selectedKeys.Add(n.Key);
            }

            filterVisible = null;
            if (treeFilter.Length > 0)
            {
                filterVisible = new HashSet<ActionNode>();
                foreach (var r in treeForest) MarkFilterVisible(r);
            }

            lvActions.BeginUpdate();
            lvActions.Items.Clear();
            foreach (var root in treeForest)
            {
                if (root.IsTaskHeader) RenderNode(root, 0);
                else
                {
                    // korzeń pojedynczego zadania jest "wirtualny" (bez nagłówka) - pokazujemy tylko dzieci
                    var children = root.Children.ToList();
                    children.Sort(CompareNodes);
                    foreach (var c in children) RenderNode(c, 0);
                }
            }
            if (selectedKeys.Count > 0)
                foreach (ListViewItem it in lvActions.Items)
                {
                    var n = it.Tag as ActionNode;
                    if (n != null && selectedKeys.Contains(n.Key)) it.Selected = true;
                }
            lvActions.EndUpdate();

            if (topKey != null)
                foreach (ListViewItem it in lvActions.Items)
                {
                    var n = it.Tag as ActionNode;
                    if (n != null && n.Key == topKey) { lvActions.TopItem = it; break; }
                }
        }

        void RenderNode(ActionNode node, int depth)
        {
            if (filterVisible != null && !filterVisible.Contains(node)) return;
            lvActions.Items.Add(MakeItem(node, depth));
            // przy aktywnym filtrze foldery są rozwinięte na siłę - inaczej dopasowań nie widać
            if (node.IsFolder && (filterVisible != null || !collapsedKeys.Contains(node.Key)))
            {
                var children = node.Children.ToList();
                children.Sort(CompareNodes);
                foreach (var c in children) RenderNode(c, depth + 1);
            }
        }

        ListViewItem MakeItem(ActionNode node, int depth)
        {
            var a = node.EffectiveAction;
            string col0 = node.Action != null ? node.Action.Describe() : node.IsTaskHeader ? "📦 Zadanie" : "";
            var it = new ListViewItem(col0) { Tag = node };
            string pad = new string(' ', depth * 4);
            string glyph = node.IsFolder ? (filterVisible == null && collapsedKeys.Contains(node.Key) ? "▶ " : "▼ ") : "";
            string icon = node.IsFolder ? "📁 " : "";
            string label = node.IsTaskHeader ? "[" + node.Name + "]" : node.Name;
            it.SubItems.Add(pad + glyph + icon + label);
            it.SubItems.Add(node.IsFolder ? (node.IsTaskHeader ? "" : FmtSize(node.Size))
                            : a != null && (a.Type == ActionType.Copy || a.Type == ActionType.Same) ? FmtSize(a.Size) : "");
            it.SubItems.Add(a != null ? FmtTime(a.SrcTime) : "");
            it.SubItems.Add(a != null ? FmtTime(a.DstTime) : "");
            it.SubItems.Add(a != null ? a.Reason : "");
            // kolor odróżnia poziom: zadanie / folder główny / podfolder / plik (kolor pliku wg rodzaju akcji)
            if (node.IsTaskHeader)
            {
                it.ForeColor = Color.FromArgb(90, 0, 120);
                it.Font = new Font(lvActions.Font, FontStyle.Bold);
            }
            else if (node.IsFolder)
            {
                it.ForeColor = depth == 0 ? Color.FromArgb(150, 60, 0) : Color.FromArgb(0, 100, 130);
                if (depth == 0) it.Font = new Font(lvActions.Font, FontStyle.Bold);
            }
            else if (node.Action != null)
            {
                if (node.Action.Type == ActionType.Delete) it.ForeColor = Color.DarkRed;
                else if (node.Action.Type == ActionType.CreateDir) it.ForeColor = Color.DarkBlue;
                else if (node.Action.Type == ActionType.Copy) it.ForeColor = node.Action.ToRight ? Color.DarkBlue : Color.DarkGreen;
                else if (node.Action.Type == ActionType.Same) it.ForeColor = node.Action.Reason.StartsWith("identyczne") ? Color.Gray : Color.DarkOrange;
            }
            return it;
        }

        void ShowActions(SyncTask t, List<SyncAction> acts, bool append)
        {
            const int MaxRows = 100000;
            if (!append) { shown.Clear(); treeForest.Clear(); }
            shown.Add(Tuple.Create(t, acts, append));
            bool showSame = chkShowSame == null || chkShowSame.Checked;
            searchDirty = true;   // stare dopasowania wskazywały na węzły poprzedniego drzewa

            ActionNode root = append
                ? new ActionNode { Name = t.Name, Rel = "", IsFolder = true, IsTaskHeader = true, Task = t, Key = "TASK\u0001" + t.Name,
                    Children = new List<ActionNode>(), ChildIndex = new Dictionary<string, ActionNode>(StringComparer.OrdinalIgnoreCase) }
                : new ActionNode { Name = "", Rel = "", IsFolder = true, Task = t, Key = "ROOT\u0001" + t.Name,
                    Children = new List<ActionNode>(), ChildIndex = new Dictionary<string, ActionNode>(StringComparer.OrdinalIgnoreCase) };
            if (append) DefaultCollapse(root);
            treeForest.Add(root);

            int n = 0;
            foreach (var a in acts)
            {
                if (a.Type == ActionType.Same && !showSame) continue;
                if (n >= MaxRows) break;
                InsertAction(root, a, t);
                n++;
            }
            ComputeSize(root);

            RenderForest();
            string s = (append ? "[" + t.Name + "] " : "") + Summary(acts);
            if (acts.Count > MaxRows) s += "   (pokazano pierwsze " + MaxRows + " pozycji)";
            lblSummary.Text = s;
            lblSummary.BackColor = SystemColors.Control;   // świeża analiza/synchronizacja - zdejmij żółty pasek historii
        }


        class DecyzjaZnikniete
        {
            public List<ZnikloPoDrugiejStronie> Przywroc = new List<ZnikloPoDrugiejStronie>();
            public List<ZnikloPoDrugiejStronie> Usun = new List<ZnikloPoDrugiejStronie>();
        }

        // Okno decyzji o pozycjach, które zniknęły po drugiej stronie. Trzy wyjścia, bo tylko tyle
        // ma sens: odtworzyć, przyjąć usunięcie, albo nie robić nic i wrócić do tego później.
        // Nic nie dzieje się samo - zamknięcie okna zostawia sprawę otwartą.
        DecyzjaZnikniete OknoZnikniete(SyncTask t, List<ZnikloPoDrugiejStronie> lista, SyncTask oryginal = null)
        {
            var wynik = new DecyzjaZnikniete();
            bool lustro = t.Mirror && t.Direction != SyncDirection.TwoWay;

            using (var o = new Form())
            {
                o.Text = "Echo Sync – po drugiej stronie coś skasowano";
                o.Size = new Size(900, 640);
                o.MinimumSize = new Size(900, 460);
                o.StartPosition = FormStartPosition.CenterParent;
                o.Icon = AppIcon();
                o.MinimizeBox = false; o.MaximizeBox = true;
                o.FormBorderStyle = FormBorderStyle.Sizable;

                var lv = new ListView { Dock = DockStyle.Fill, View = View.Details, CheckBoxes = true,
                    FullRowSelect = true, GridLines = true };
                lv.Columns.Add("Pozycja", 470);
                lv.Columns.Add("Rozmiar", 90);
                lv.Columns.Add("Co się stało", 250);
                foreach (var z in lista.OrderBy(x => x.Rel, StringComparer.OrdinalIgnoreCase))
                {
                    var it = new ListViewItem((z.IsDir ? "[folder]  " : "") + z.Rel) { Tag = z, Checked = true };
                    it.SubItems.Add(z.IsDir ? "" : FmtSize(z.Size));
                    it.SubItems.Add(z.UsunPoPrawej ? "skasowane po LEWEJ, jest po prawej"
                                                   : "skasowane po PRAWEJ, jest po lewej");
                    lv.Items.Add(it);
                }
                // Naglowek: jedno zdanie duza czcionka - po co to okno w ogole wyskoczylo.
                var naglowek = new Label
                {
                    Dock = DockStyle.Top,
                    Height = 62,
                    BackColor = Color.FromArgb(255, 249, 226),
                    Padding = new Padding(14, 10, 12, 0),
                    Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
                    ForeColor = Color.FromArgb(90, 60, 0),
                    Text = lista.Count + (lista.Count == 1 ? " pozycja zniknęła" : lista.Count < 5 ? " pozycje zniknęły" : " pozycji zniknęło")
                           + " po drugiej stronie – zadanie \"" + t.Name + "\".\r\n"
                           + "Były przy poprzedniej synchronizacji, teraz ich nie ma. Ktoś je skasował."
                };

                // Legenda przyciskow - kolorami, zeby dalo sie ja przeczytac jednym spojrzeniem.
                var legenda = new TableLayoutPanel
                {
                    Dock = DockStyle.Top, Height = 80, ColumnCount = 2, RowCount = 3,
                    Padding = new Padding(14, 8, 12, 0), BackColor = Color.White
                };
                legenda.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
                legenda.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
                for (int i = 0; i < 3; i++) legenda.RowStyles.Add(new RowStyle(SizeType.Absolute, 23));
                Action<int, string, Color, string> wiersz = delegate(int r, string co, Color kolor, string opisTekst)
                {
                    legenda.Controls.Add(new Label { Text = co, AutoSize = true, ForeColor = kolor,
                        Font = new Font("Segoe UI", 9f, FontStyle.Bold), Margin = new Padding(0, 3, 0, 0) }, 0, r);
                    legenda.Controls.Add(new Label { Text = opisTekst, AutoSize = true, ForeColor = Color.FromArgb(60, 60, 60),
                        Margin = new Padding(0, 3, 0, 0) }, 1, r);
                };
                wiersz(0, "↩ PRZYWRÓĆ", Color.FromArgb(0, 110, 40), "odtworzy je tam, gdzie zniknęły – nadal masz je po drugiej stronie");
                wiersz(1, "🗑 USUŃ TAKŻE", Color.FromArgb(170, 30, 30), "skasuje je też po tej stronie; ostatnia kopia i tak trafi najpierw do _SyncArchive");
                wiersz(2, "PÓŹNIEJ", Color.FromArgb(90, 90, 90), "nic nie robię – zapytam znowu przy następnej synchronizacji");

                var podpowiedz = new Label
                {
                    Dock = DockStyle.Top, Height = 26, Padding = new Padding(14, 4, 0, 0),
                    ForeColor = Color.FromArgb(40, 40, 40),
                    Text = "Zaznacz pozycje, których ma dotyczyć decyzja:"
                };

                var dol = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 58, Padding = new Padding(12, 10, 0, 0),
                    WrapContents = false, AutoScroll = true };   // bez zawijania - inaczej ostatni przycisk ginie ponizej widocznej wysokosci
                var bPrzywroc = MkButton("↩ Przywróć zaznaczone", 190, null);
                var bUsun = MkButton("🗑 Usuń także po drugiej stronie", 230, null);
                var bPozniej = MkButton("Później", 100, null);
                var bZaznacz = MkButton("Zaznacz wszystko", 140, delegate { foreach (ListViewItem i in lv.Items) i.Checked = true; });
                var bOdznacz = MkButton("Odznacz wszystko", 140, delegate { foreach (ListViewItem i in lv.Items) i.Checked = false; });
                foreach (var b in new[] { bPrzywroc, bUsun, bPozniej, bZaznacz, bOdznacz })
                { b.Height = 34; b.Margin = new Padding(0, 0, 8, 0); }
                bPrzywroc.DialogResult = DialogResult.Yes;
                bUsun.DialogResult = DialogResult.No;
                bPozniej.DialogResult = DialogResult.Cancel;
                bPrzywroc.Font = new Font(bPrzywroc.Font, FontStyle.Bold);
                bPrzywroc.ForeColor = Color.FromArgb(0, 90, 30);
                bUsun.ForeColor = Color.FromArgb(150, 25, 25);
                bZaznacz.Margin = new Padding(24, 0, 8, 0);   // odsun narzedzia od decyzji
                var tip = new ToolTip();
                tip.SetToolTip(bPrzywroc, "Odtworzy zaznaczone pozycje tam, gdzie zniknęły.");
                tip.SetToolTip(bUsun, "Skasuje zaznaczone pozycje także po tej stronie, na której jeszcze są.\nTrafią najpierw do _SyncArchive – nawet przy usuwaniu trwałym.");
                tip.SetToolTip(bPozniej, "Zostawia sprawę otwartą. Pliki czekają w _SyncPending, zapytam ponownie.");
                dol.Controls.AddRange(new Control[] { bPrzywroc, bUsun, bPozniej, bZaznacz, bOdznacz });

                // Kolejnosc dodawania ma znaczenie: dokowanie idzie od konca kolekcji,
                // wiec wypelnienie (lista) musi byc pierwsze, a naglowek - ostatni z gornych.
                o.Controls.Add(lv);
                o.Controls.Add(podpowiedz);
                o.Controls.Add(legenda);
                o.Controls.Add(naglowek);
                o.Controls.Add(dol);
                o.AcceptButton = bPrzywroc;   // Enter = odtworzenie, czyli nic nie ginie
                o.CancelButton = bPozniej;    // Esc = nie decyduję teraz

                var odp = o.ShowDialog(this);
                if (odp == DialogResult.Yes || odp == DialogResult.No)
                {
                    var wybrane = lv.Items.Cast<ListViewItem>().Where(i => i.Checked)
                                    .Select(i => (ZnikloPoDrugiejStronie)i.Tag).ToList();
                    if (odp == DialogResult.Yes) wynik.Przywroc = wybrane; else wynik.Usun = wybrane;

                    // Propozycja zapamiętania decyzji NA STAŁE dla tego zadania - żeby program
                    // nie pytał w kółko. Wycofanie: link "Stała decyzja..." w edytorze zadania.
                    if (wybrane.Count > 0 && oryginal != null && oryginal.StalaDecyzjaZnikniete.Length == 0)
                    {
                        bool przywracaj = odp == DialogResult.Yes;
                        var pyt = MessageBox.Show(this,
                            "Czy zapamiętać tę decyzję NA STAŁE dla zadania \"" + oryginal.Name + "\"?\n\n" +
                            (przywracaj
                                ? "Od teraz wszystko, co zniknie po drugiej stronie, będzie automatycznie PRZYWRACANE – bez pytania."
                                : "Od teraz każde usunięcie po drugiej stronie będzie automatycznie PRZYJMOWANE (kasowanie także u siebie, z kopią w _SyncArchive) – bez pytania.") +
                            "\n\nDecyzję możesz w każdej chwili wycofać w ustawieniach zadania (link „Stała decyzja” obok przycisku Zapisz).",
                            "Echo Sync – zapamiętać decyzję?", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                            MessageBoxDefaultButton.Button2);
                        if (pyt == DialogResult.Yes)
                        {
                            oryginal.StalaDecyzjaZnikniete = przywracaj ? "przywroc" : "usun";
                            SaveConfig();
                            OdswiezLinkStalejDecyzji();
                            AppendLog("Zadanie \"" + oryginal.Name + "\": zapamiętano stałą decyzję – " +
                                      (przywracaj ? "PRZYWRACAJ zniknięte" : "USUWAJ także po drugiej stronie") + ".");
                        }
                    }
                }

                AppendLog("Zniknięte po drugiej stronie w \"" + t.Name + "\": " + lista.Count
                    + "  –  przywracam: " + wynik.Przywroc.Count + ", usuwam także po drugiej stronie: " + wynik.Usun.Count
                    + ", zostawiam na później: " + (lista.Count - wynik.Przywroc.Count - wynik.Usun.Count));
            }
            return wynik;
        }

        // Pasek jest widoczny tak długo, jak długo cokolwiek czeka na decyzję - w przeciwieństwie
        // do powiadomienia, które znika po kilku sekundach.
        // ---------- tymczasowy schowek na czas decyzji
        // Dopóki sprawa czeka, plik istnieje już tylko po JEDNEJ stronie. Gdyby ktoś skasował
        // i tę kopię, przepadłby bezpowrotnie. Odkładamy więc kopię do _SyncPending i trzymamy
        // ją dokładnie tak długo, jak trwa decyzja - po niej schowek znika.
        static string SchowekKorzen(SyncTask t, ZnikloPoDrugiejStronie z)
        {
            return Path.Combine(Engine.Norm(z.UsunPoPrawej ? t.Right : t.Left), Engine.PendingDir);
        }

        static void ZabezpieczNaCzasDecyzji(SyncTask t, List<ZnikloPoDrugiejStronie> lista, Action<string> log)
        {
            int zrobione = 0; long bajty = 0;
            foreach (var z in lista.Where(x => !x.IsDir))
            {
                try
                {
                    string zrodlo = Path.Combine(Engine.Norm(z.UsunPoPrawej ? t.Right : t.Left), z.Rel);
                    if (!File.Exists(zrodlo)) continue;
                    string cel = Path.Combine(SchowekKorzen(t, z), z.Rel);
                    if (File.Exists(cel)) continue;              // już zabezpieczone przy poprzednim przebiegu
                    Directory.CreateDirectory(Path.GetDirectoryName(cel));
                    File.Copy(zrodlo, cel, false);
                    zrobione++; bajty += z.Size;
                }
                catch { }
            }
            if (zrobione > 0) log("  Kopia bezpieczenstwa na czas decyzji: " + zrobione + " plikow ("
                                  + FmtSize(bajty) + ") w " + Engine.PendingDir);
        }

        // Czy tresc spod tej sciezki da sie jeszcze skadkolwiek odzyskac: z ktorejs ze stron
        // albo z _SyncArchive (tam trafia to, czego usuniecie zostalo przyjete).
        static bool DaSieOdzyskac(SyncTask t, string rel)
        {
            foreach (string korzen in new[] { Engine.Norm(t.Left), Engine.Norm(t.Right) })
            {
                if (File.Exists(Path.Combine(korzen, rel))) return true;
                string arch = Path.Combine(korzen, Engine.ArchiveDir);
                if (!Directory.Exists(arch)) continue;
                try
                {
                    foreach (string stempel in Directory.GetDirectories(arch))
                        if (File.Exists(Path.Combine(stempel, rel))) return true;
                }
                catch { }
            }
            return false;
        }

        // Sprzątanie schowka po decyzji - ale NIE na ślepo. Kasujemy tylko to, co da się jeszcze
        // odzyskać skądinąd. Jeśli w trakcie decyzji zniknęła ostatnia kopia, schowek jest jedynym
        // miejscem, gdzie plik przetrwał - wtedy zostaje i mówimy o tym głośno.
        static int SprzatnijSchowek(SyncTask t, Action<string> log)
        {
            int uratowane = 0;
            foreach (string korzen in new[] { Path.Combine(Engine.Norm(t.Left), Engine.PendingDir),
                                              Path.Combine(Engine.Norm(t.Right), Engine.PendingDir) })
            {
                if (!Directory.Exists(korzen)) continue;
                try
                {
                    foreach (string plik in Directory.GetFiles(korzen, "*", SearchOption.AllDirectories))
                    {
                        string rel = plik.Substring(korzen.Length).TrimStart('\\');
                        if (DaSieOdzyskac(t, rel)) { try { File.Delete(plik); } catch { } }
                        else
                        {
                            uratowane++;
                            if (log != null) log("  URATOWANE: " + rel + " - ostatnia kopia zniknela w trakcie decyzji, "
                                                 + "plik lezy w " + Engine.PendingDir);
                        }
                    }
                    // puste katalogi po sprzataniu; korzen znika tylko wtedy, gdy nic w nim nie zostalo
                    foreach (string d in Directory.GetDirectories(korzen, "*", SearchOption.AllDirectories)
                                                  .OrderByDescending(x => x.Length))
                        try { if (Directory.GetFileSystemEntries(d).Length == 0) Directory.Delete(d); } catch { }
                    if (Directory.GetFileSystemEntries(korzen).Length == 0) Directory.Delete(korzen);
                }
                catch { }
            }
            return uratowane;
        }

        // Jedno wejście do decyzji dla paska, przycisku przy liście i kliknięcia w powiadomienie.
        // Ręczny przebieg wykryje sprawę jeszcze raz (migawka ją pamięta) i pokaże okno wyboru.
        void OtworzDecyzje()
        {
            var z = cfg.Tasks.FirstOrDefault(x => x.CzekaDecyzji > 0);
            if (z == null) { MessageBox.Show(this, "Nie ma teraz żadnych decyzji do podjęcia.", "Echo Sync"); return; }
            if (busy) { MessageBox.Show(this, "Trwa synchronizacja – spróbuj za chwilę.", "Echo Sync"); return; }
            if (!lstTasks.Items.Contains(z)) { txtSearchTasks.Text = ""; }   // zadanie schowane przez filtr - pokaż wszystkie
            lstTasks.SelectedItem = z;
            StartWork(new List<SyncTask> { z }, true, true, false);
        }

        // Okno archiwum: co lezy w _SyncArchive tego zadania, ile zajmuje, kasowanie reczne
        // i wybor, po jakim czasie stare paczki maja znikac same.
        void OtworzArchiwum()
        {
            var oryginal = Current;
            if (oryginal == null) { MessageBox.Show(this, "Najpierw zaznacz zadanie na liście.", "Echo Sync"); return; }
            if (busy) { MessageBox.Show(this, "Trwa synchronizacja – spróbuj za chwilę.", "Echo Sync"); return; }
            var t = oryginal.Resolved();

            using (var f = new Form
            {
                Text = "Archiwum kopii – " + oryginal.Name,
                Size = new Size(720, 520),
                StartPosition = FormStartPosition.CenterParent,
                MinimizeBox = false,
                MaximizeBox = false,
                FormBorderStyle = FormBorderStyle.Sizable,
                Icon = Icon
            })
            {
                var lblGdzie = new Label
                {
                    Dock = DockStyle.Top,
                    Height = 38,
                    ForeColor = Color.DimGray,
                    Padding = new Padding(4, 4, 4, 0),
                    Text = "Tu trafia wszystko, co program usunął lub nadpisał. Dzięki temu da się cofnąć pomyłkę.\r\n" +
                           string.Join("     ", Archiwum.Korzenie(t).ToArray())
                };

                var lv = new ListView
                {
                    Dock = DockStyle.Fill,
                    View = View.Details,
                    FullRowSelect = true,
                    CheckBoxes = true,
                    GridLines = true,
                    HideSelection = false
                };
                lv.Columns.Add("Kiedy", 130);
                lv.Columns.Add("Wiek", 90);
                lv.Columns.Add("Plików", 70, HorizontalAlignment.Right);
                lv.Columns.Add("Rozmiar", 90, HorizontalAlignment.Right);
                lv.Columns.Add("Folder", 300);

                var lblRazem = new Label { Dock = DockStyle.Top, Height = 24, Font = new Font(Font, FontStyle.Bold), Padding = new Padding(4, 3, 0, 0) };

                var dolny = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 84, Padding = new Padding(4) };
                var cmbDni = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200, Margin = new Padding(0, 2, 8, 0) };
                cmbDni.Items.AddRange(Archiwum.NAZWY_DNI);
                int idx = Array.IndexOf(Archiwum.DNI, oryginal.ArchiwumDni);
                cmbDni.SelectedIndex = idx >= 0 ? idx : 0;

                var paczki = new List<PaczkaArchiwum>();
                Action odswiez = delegate
                {
                    paczki.Clear();
                    paczki.AddRange(Archiwum.Spis(t));
                    lv.BeginUpdate();
                    lv.Items.Clear();
                    foreach (var p in paczki)
                    {
                        int wiek = (int)(DateTime.Now - p.Kiedy).TotalDays;
                        var it = new ListViewItem(p.Kiedy.ToString("dd.MM.yyyy HH:mm"));
                        it.SubItems.Add(wiek <= 0 ? "dziś" : wiek + (wiek == 1 ? " dzień" : " dni"));
                        it.SubItems.Add(p.Plikow.ToString());
                        it.SubItems.Add(MainForm.Rozmiar(p.Bajtow));
                        it.SubItems.Add(p.Sciezka);
                        it.Tag = p;
                        lv.Items.Add(it);
                    }
                    lv.EndUpdate();
                    lblRazem.Text = paczki.Count == 0
                        ? "Archiwum jest puste – nic nie zajmuje miejsca."
                        : "Razem: " + paczki.Count + " paczek, " + paczki.Sum(p => p.Plikow) + " plików, "
                          + MainForm.Rozmiar(paczki.Sum(p => p.Bajtow));
                };
                odswiez();

                Func<List<PaczkaArchiwum>> zaznaczone = delegate
                {
                    var l = new List<PaczkaArchiwum>();
                    foreach (ListViewItem it in lv.CheckedItems) l.Add((PaczkaArchiwum)it.Tag);
                    if (l.Count == 0)
                        foreach (ListViewItem it in lv.SelectedItems) l.Add((PaczkaArchiwum)it.Tag);
                    return l;
                };

                Action<List<PaczkaArchiwum>, string> kasuj = delegate(List<PaczkaArchiwum> lista, string co)
                {
                    if (lista.Count == 0) { MessageBox.Show(f, "Nic nie zaznaczono.", "Echo Sync"); return; }
                    long b = lista.Sum(p => p.Bajtow);
                    int pl = lista.Sum(p => p.Plikow);
                    if (MessageBox.Show(f,
                        "Usunąć " + co + "?\r\n\r\n" + lista.Count + " paczek, " + pl + " plików, " + MainForm.Rozmiar(b) +
                        "\r\n\r\nTo jest KASOWANIE TRWAŁE i nieodwracalne.\r\nPo nim nie da się już cofnąć decyzji objętych tymi paczkami.",
                        "Czyszczenie archiwum", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                        return;
                    long zwolnione;
                    Cursor = Cursors.WaitCursor;
                    int n = Archiwum.Usun(lista, AppendLog, out zwolnione);
                    Cursor = Cursors.Default;
                    AppendLog("Archiwum \"" + oryginal.Name + "\": usunięto " + n + " paczek, zwolniono " + MainForm.Rozmiar(zwolnione));
                    odswiez();
                };

                var bZazn = new Button { Text = "🗑 Usuń zaznaczone", Width = 150, Height = 30, Margin = new Padding(0, 0, 6, 0) };
                bZazn.Click += delegate { kasuj(zaznaczone(), "zaznaczone paczki"); };

                var bWszystko = new Button { Text = "🗑 Usuń całą zawartość", Width = 165, Height = 30, Margin = new Padding(0, 0, 6, 0) };
                bWszystko.Click += delegate { kasuj(new List<PaczkaArchiwum>(paczki), "CAŁĄ zawartość archiwum"); };

                var bFolder = new Button { Text = "📂 Otwórz folder", Width = 130, Height = 30, Margin = new Padding(0, 0, 6, 0) };
                bFolder.Click += delegate
                {
                    foreach (string k in Archiwum.Korzenie(t))
                        if (Directory.Exists(k)) { try { System.Diagnostics.Process.Start("explorer.exe", "\"" + k + "\""); } catch { } break; }
                };

                var bOdswiez = new Button { Text = "Odśwież", Width = 90, Height = 30, Margin = new Padding(0, 0, 6, 0) };
                bOdswiez.Click += delegate { odswiez(); };

                var bZamknij = new Button { Text = "Zamknij", Width = 90, Height = 30, DialogResult = DialogResult.Cancel };

                dolny.Controls.AddRange(new Control[] { bZazn, bWszystko, bFolder, bOdswiez, bZamknij });
                dolny.SetFlowBreak(bZamknij, true);
                dolny.Controls.Add(new Label { Text = "Kasuj stare paczki same:", AutoSize = true, Margin = new Padding(0, 9, 4, 0) });
                dolny.Controls.Add(cmbDni);
                dolny.Controls.Add(new Label
                {
                    Text = "Sprzątanie robi się po każdej synchronizacji tego zadania.",
                    AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(0, 9, 0, 0)
                });

                f.Controls.Add(lv);
                f.Controls.Add(lblRazem);
                f.Controls.Add(lblGdzie);
                f.Controls.Add(dolny);
                f.CancelButton = bZamknij;
                f.ShowDialog(this);

                int nowe = Archiwum.DNI[Math.Max(0, Math.Min(Archiwum.DNI.Length - 1, cmbDni.SelectedIndex))];
                if (nowe != oryginal.ArchiwumDni)
                {
                    oryginal.ArchiwumDni = nowe;
                    SaveConfig();
                    AppendLog("Archiwum zadania \"" + oryginal.Name + "\": stare kopie kasowane " + Archiwum.OpisOkresu(nowe) + ".");
                    if (editing == oryginal) LoadEditor();
                }
            }
        }

        void OdswiezPasekDecyzji()
        {
            int razem = cfg.Tasks.Sum(x => x.CzekaDecyzji);
            int zadan = cfg.Tasks.Count(x => x.CzekaDecyzji > 0);
            btnDecyzjeLista.Text = "❗ Decyzje do podjęcia" + (razem > 0 ? " (" + razem + ")" : "");
            btnDecyzjeLista.Visible = razem > 0;
            if (razem == 0) { AnimujPanelDecyzji(false); return; }
            lblDecyzji.Text = "❗ Po drugiej stronie skasowano " + razem + " pozycji"
                + (zadan > 1 ? " w " + zadan + " zadaniach" : "")
                + " – czekam na Twoją decyzję: przywrócić czy usunąć też u siebie?";
            AnimujPanelDecyzji(true);
            Controls.SetChildIndex(panelDecyzji, 1);   // pod naglowkiem, nad trescia (patrz uwaga przy tworzeniu)
            OdswiezListeZadan();
        }

        void OdswiezListeZadan()
        {
            lstTasks.Invalidate();   // te same referencje - przerysowanie wystarczy (i nie psuje filtra listy)
        }

        void StartWork(List<SyncTask> tasks, bool execute, bool interactive, bool exitAfter, HashSet<SyncTask> watchTriggered = null)
        {
            if (busy || tasks.Count == 0) return;
            bool background = !interactive && !exitAfter;
            if (!background) SaveConfig();
            SetBusy(true, background);
            var snapshots = tasks.Select(x => x.Resolved()).ToList();
            running = tasks.ToList();
            lstTasks.Invalidate();   // kropka "w trakcie" przy tych zadaniach
            // Referencje pobrane TERAZ (w watku UI) - w watku roboczym nizej wolno juz tylko
            // blokowac (lock) pojedyncze obiekty TaskRuntime, nigdy dotykac samego slownika runtimes.
            var taskRuntimes = tasks.Select(x => { var rt = Rt(x); lock (rt) rt.Running = true; return rt; }).ToList();
            engine.Cancel = false;
            engine.Log = s => AppendLog(s);
            engine.Status = s => QueueStatus(s);
            engine.Progress = v => Ui(() => progress.Value = Math.Max(0, Math.Min(1000, v)));
            engine.Speed = s => Ui(() => lblSpeed.Text = s.Length == 0 ? "" : "⇵ " + s);
            bool multi = tasks.Count > 1;
            if (!background)
            {
                shown.Clear();
                treeForest.Clear();
                lvActions.Items.Clear();
                lblSummary.Text = "";
                tabs.SelectedIndex = execute ? 1 : 0;
            }
            TrayText("synchronizacja...");

            var th = new Thread(() =>
            {
                int totalErr = 0;
                var results = new string[snapshots.Count];
                var copiedSets = new HashSet<string>[snapshots.Count];
                var fullActs = new List<SyncAction>[snapshots.Count];   // pełny obraz (razem z "bez zmian") do odświeżenia widoku
                for (int ti = 0; ti < snapshots.Count; ti++)
                {
                    var t = snapshots[ti];
                    // t to KOPIA (Resolved klonuje). Stan widoczny dla człowieka - licznik decyzji,
                    // zaznaczenie na liście - musi trafiać na oryginał z cfg.Tasks, inaczej ginie.
                    var oryginal = tasks[ti];
                    var rt = taskRuntimes[ti];
                    if (engine.Cancel) break;

                    // Folder zadania niedostępny (drugi komputer wyłączony, dysk odłączony)?
                    // Pomijamy zadanie W CAŁOŚCI: pusty "cel" wyglądałby jak tysiące skasowanych
                    // plików albo dziesiątki błędów kopiowania. Sprawdzamy foldery z konfiguracji
                    // (bez dopisanego podfolderu - ten może jeszcze nie istnieć). Dziennik: raz,
                    // potem najwyżej co godzinę; powrót dostępności też jest odnotowany.
                    {
                        string brak = null;
                        foreach (var sciezka in new[] { oryginal.Left, oryginal.Right })
                        {
                            bool jest = false;
                            try { jest = Directory.Exists(Engine.Norm(sciezka)); } catch { }
                            if (!jest) { brak = sciezka; break; }
                        }
                        DateTime ost;
                        if (brak != null)
                        {
                            if (!niedostepneLog.TryGetValue(oryginal.Name, out ost) || (DateTime.Now - ost).TotalMinutes >= 60)
                            {
                                niedostepneLog[oryginal.Name] = DateTime.Now;
                                engine.Log("Pomijam \"" + t.Name + "\" – folder niedostępny: " + brak +
                                           " (wyłączony komputer / odłączony dysk). Wrócę, gdy się pojawi.");
                            }
                            continue;
                        }
                        DateTime byl; if (niedostepneLog.TryRemove(oryginal.Name, out byl))
                            engine.Log("\"" + t.Name + "\" – foldery znowu dostępne, synchronizuję.");
                    }
                    var sw = Stopwatch.StartNew();
                    engine.Log("=== " + (execute ? "SYNCHRONIZACJA" : "ANALIZA") + ": " + t.Name + "  [" + t.Left + "  " +
                               (t.Direction == SyncDirection.LeftToRight ? "→" : t.Direction == SyncDirection.RightToLeft ? "←" : "⇄") +
                               "  " + t.Right + "]" + (t.Mirror && t.Direction != SyncDirection.TwoWay ? "  LUSTRO" : ""));
                    try
                    {
                        engine.Migawka = Migawki.Wczytaj(t.Name);
                        engine.Znikniete = new List<ZnikloPoDrugiejStronie>();

                        // Szybki skan (jak indeks AllwaySync): jesli watcher wskazal DOKLADNIE ktore
                        // foldery sie zmienily i mamy zaufany cache z poprzedniego pelnego skanu, czytamy
                        // z dysku TYLKO te foldery. Kazdy inny przypadek (reczne uruchomienie, harmonogram,
                        // brak/przestarzaly cache, przerwa w obserwacji) wraca do pelnego, bezpiecznego skanu.
                        HashSet<string> onlyTop = null;
                        Dictionary<string, Entry> baseL = null, baseR = null;
                        List<string> pokryteFoldery = null;
                        bool zWatchera = watchTriggered != null && watchTriggered.Contains(oryginal);
                        if (zWatchera && rt.CachedL != null && rt.CachedR != null && !rt.FullRescanNeeded && DateTime.Now < rt.NextForcedFullScan)
                        {
                            lock (rt) if (rt.DirtyTop.Count > 0) pokryteFoldery = new List<string>(rt.DirtyTop);
                            if (pokryteFoldery != null)
                            {
                                onlyTop = new HashSet<string>(pokryteFoldery, StringComparer.OrdinalIgnoreCase);
                                baseL = rt.CachedL; baseR = rt.CachedR;
                            }
                        }

                        var acts = engine.Analyze(t, onlyTop, baseL, baseR);

                        // Zapamietaj swiezy obraz do nastepnego szybkiego skanu; pelny skan czysci cala
                        // tablice "brudnych" folderow, przyrostowy zdejmuje tylko to, co wlasnie odswiezono
                        // (nowe zmiany zgłoszone w miedzyczasie przez watcher zostaja na nastepny raz).
                        if (engine.LastLeftScan != null && engine.LastRightScan != null)
                        {
                            lock (rt)
                            {
                                rt.CachedL = engine.LastLeftScan;
                                rt.CachedR = engine.LastRightScan;
                                if (onlyTop == null)
                                {
                                    rt.DirtyTop.Clear();
                                    rt.FullRescanNeeded = false;
                                    rt.NextForcedFullScan = DateTime.Now.AddMinutes(30);
                                }
                                else foreach (var top in pokryteFoldery) rt.DirtyTop.Remove(top);
                            }
                        }

                        var tt = t;
                        var znikniete = engine.Znikniete;
                        // migawka listy DO wyświetlenia - "acts" leci dalej do decyzji o zniknięciach
                        // i modyfikacji (RemoveAll/Add), a UI odpala się asynchronicznie (BeginInvoke) -
                        // bez kopii mogłoby pokazać już okrojoną/zmienioną listę zamiast pełnego drzewa
                        var actsForUi = acts.ToList();
                        fullActs[ti] = actsForUi;
                        if (!background) Ui(() => ShowActions(tt, actsForUi, multi));
                        engine.Log("  " + Summary(acts));

                        // Coś zniknęło po drugiej stronie, a w migawce było - czyli ktoś to skasował.
                        // Bez pytania niczego tu nie kasujemy ani nie odtwarzamy po cichu.
                        if (znikniete.Count > 0 && execute)
                        {
                            // Dopóki człowiek nie zdecyduje, NIE ruszamy tych pozycji - także przy lustrze.
                            // Inaczej lustro odtworzyłoby je w tle i decyzja zniknęłaby razem z powodem.
                            var czekaja = new HashSet<string>(znikniete.Select(z => z.Rel), StringComparer.OrdinalIgnoreCase);
                            acts.RemoveAll(a => czekaja.Contains(a.Rel) &&
                                                (a.Type == ActionType.Copy || a.Type == ActionType.CreateDir));

                            ZabezpieczNaCzasDecyzji(t, znikniete, s => engine.Log(s));

                            foreach (var z in znikniete.Take(10))
                                engine.Log("  ZNIKNELO PO DRUGIEJ STRONIE: " + z.Rel + "  (" + z.Opis + ")");
                            if (znikniete.Count > 10) engine.Log("  ... i " + (znikniete.Count - 10) + " wiecej");

                            // Stała decyzja zadania (zapamiętana w oknie decyzji): stosujemy od razu,
                            // bez okna i bez powiadomienia. Wycofywalna linkiem w edytorze zadania.
                            DecyzjaZnikniete stala = null;
                            if (oryginal.StalaDecyzjaZnikniete == "przywroc")
                            { stala = new DecyzjaZnikniete { Przywroc = znikniete.ToList() };
                              engine.Log("  Stała decyzja zadania: PRZYWRÓĆ – stosuję bez pytania (" + znikniete.Count + " pozycji)."); }
                            else if (oryginal.StalaDecyzjaZnikniete == "usun")
                            { stala = new DecyzjaZnikniete { Usun = znikniete.ToList() };
                              engine.Log("  Stała decyzja zadania: USUŃ TAKŻE – stosuję bez pytania (" + znikniete.Count + " pozycji)."); }

                            if (stala == null && !interactive)
                            {
                                engine.Log("  Wykryto " + znikniete.Count + " usuniec po drugiej stronie - czekam na Twoja decyzje. " +
                                           "Kliknij powiadomienie albo \"Synchronizuj\" przy tym zadaniu.");
                                int ile = znikniete.Count;
                                Ui(() =>
                                {
                                    zadanieZDecyzja = oryginal;
                                    oryginal.CzekaDecyzji = ile;
                                    OdswiezPasekDecyzji();
                                    tray.ShowBalloonTip(15000, "Echo Sync – potrzebna decyzja",
                                        "Po drugiej stronie skasowano " + ile + " pozycji w zadaniu \"" + tt.Name +
                                        "\". Kliknij tutaj, żeby zdecydować: przywrócić czy usunąć też u siebie.", ToolTipIcon.Warning);
                                });
                            }
                            else
                            {
                                DecyzjaZnikniete d = stala;
                                if (d == null) Invoke((Action)(() => d = OknoZnikniete(tt, znikniete, oryginal)));

                                // PRZYWRÓĆ: odtwarzamy tam, gdzie zniknęło - kopiujemy z tej strony,
                                // która plik jeszcze ma. Przy lustrze działanie kopiowania już istnieje.
                                foreach (var z in d.Przywroc)
                                {
                                    if (acts.Any(a => a.Rel == z.Rel && (a.Type == ActionType.Copy || a.Type == ActionType.CreateDir))) continue;
                                    acts.Add(new SyncAction {
                                        Type = z.IsDir ? ActionType.CreateDir : ActionType.Copy,
                                        ToRight = !z.UsunPoPrawej, Rel = z.Rel, Size = z.Size,
                                        Reason = "przywrocone na zyczenie", TaskName = tt.Name });
                                }

                                // USUŃ TAKŻE: kasujemy po stronie, która plik jeszcze ma. Trzeba przy tym
                                // wycofać ewentualne kopiowanie tej samej pozycji - inaczej lustro
                                // skopiowałoby ją i skasowało w jednym przebiegu.
                                foreach (var z in d.Usun)
                                {
                                    acts.RemoveAll(a => a.Rel == z.Rel && (a.Type == ActionType.Copy || a.Type == ActionType.CreateDir));
                                    acts.Add(new SyncAction { Type = ActionType.Delete, ToRight = z.UsunPoPrawej,
                                        Rel = z.Rel, Size = z.Size, Reason = "przyjete usuniecie z drugiej strony",
                                        TaskName = tt.Name, ZawszeDoArchiwum = true });
                                }

                                if (d.Przywroc.Count > 0) engine.Log("  Przywracam " + d.Przywroc.Count + " pozycji.");
                                if (d.Usun.Count > 0) engine.Log("  Przyjmuje " + d.Usun.Count + " usuniec z drugiej strony.");
                                if (d.Przywroc.Count == 0 && d.Usun.Count == 0)
                                    engine.Log("  Decyzja odlozona - zapytam znowu przy nastepnej synchronizacji.");
                                int zostalo = znikniete.Count - d.Przywroc.Count - d.Usun.Count;
                                if (zostalo == 0) SprzatnijSchowek(t, s => engine.Log(s));   // decyzja podjeta
                                Ui(() => { oryginal.CzekaDecyzji = zostalo; OdswiezPasekDecyzji(); });
                            }
                        }

                        if (znikniete.Count == 0 && execute && oryginal.CzekaDecyzji != 0)
                        {
                            int urat = SprzatnijSchowek(t, s => engine.Log(s));
                            if (urat > 0) Ui(() => tray.ShowBalloonTip(15000, "Echo Sync - plik uratowany",
                                urat + " plik(ow) przetrwalo tylko w " + Engine.PendingDir + " - ostatnia kopia zniknela w trakcie decyzji.", ToolTipIcon.Warning));
                            Ui(() => { oryginal.CzekaDecyzji = 0; OdswiezPasekDecyzji(); });   // sprawa zamknieta
                        }

                        if (!execute) { engine.Log("  (analiza – nic nie zmieniono)"); continue; }
                        if (acts.All(a => a.Type == ActionType.Same))
                        {
                            int czeka = znikniete.Count(z => z.DoDecyzji);
                            if (czeka > 0)
                            {
                                engine.Log("  Poza tym nic do zrobienia – ale " + czeka + " usuniec czeka na Twoja decyzje.");
                                results[ti] = "OK, czeka decyzji: " + czeka;
                            }
                            else
                            {
                                engine.Log("  Brak zmian – foldery zsynchronizowane.");
                                results[ti] = "OK, brak zmian";
                                // Odśwież historię aktualnym obrazem - inaczej stary zapis
                                // ("brak w celu" sprzed kopiowania) wisiałby w Podglądzie zmian
                                // do następnej realnej zmiany i wyglądał jak bieżący problem.
                                SaveHistory(t.Name, acts);
                            }
                            if (engine.StanPoSynchronizacji != null) Migawki.Zapisz(t.Name, engine.StanPoSynchronizacji);
                            if (t.ArchiwumDni > 0) try { Archiwum.Sprzataj(t, t.ArchiwumDni, s => engine.Log(s)); }
                            catch (Exception ex) { engine.Log("  BŁĄD sprzątania archiwum: " + ex.Message); }
                            continue;
                        }

                        int dels = acts.Count(a => a.Type == ActionType.Delete);
                        if (interactive && dels > 0)
                        {
                            DialogResult dr = DialogResult.No;
                            Invoke((Action)(() =>
                            {
                                tabs.SelectedIndex = 0;
                                dr = MessageBox.Show(this,
                                    "Zadanie \"" + t.Name + "\"\n\n" + Summary(acts) +
                                    "\n\nUsuwane pliki: " + (t.DeleteMode == DeleteMode.Archive ? "przeniesione do _SyncArchive" :
                                                             t.DeleteMode == DeleteMode.RecycleBin ? "do Kosza" : "USUNIĘTE TRWALE") +
                                    "\n\nKontynuować?", "Potwierdź usuwanie", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                                tabs.SelectedIndex = 1;
                            }));
                            if (dr != DialogResult.Yes) { engine.Log("  Anulowano przez użytkownika."); continue; }
                        }

                        int ok, err;
                        engine.Execute(t, acts.Where(a => a.Type != ActionType.Same).ToList(), out ok, out err);
                        totalErr += err;
                        int skipped = engine.LastSkipped;
                        engine.Log("  Zakończono: " + ok + " OK, " + err + " błędów" + (skipped > 0 ? ", pominiętych (zmienione w trakcie): " + skipped : "") +
                                   ", czas " + sw.Elapsed.ToString(@"hh\:mm\:ss"));
                        // Obraz PO wykonaniu: to on idzie do historii i na ekran. Bez tego widok
                        // (i historia) pokazywały plan sprzed kopiowania - "brak po prawej" przy
                        // plikach, które od sekund są na miejscu - oraz gubiły pozycje "bez zmian",
                        // przez co "Pokaż pliki bez zmian" dawał okrojoną listę. Przy błędach lub
                        // pominięciach nie wiemy, które akcje weszły - zostaje surowy zapis akcji.
                        List<SyncAction> obrazPo = null;
                        if (err == 0 && engine.LastSkipped == 0)
                            obrazPo = acts.Select(a => a.Type == ActionType.Same ? a : new SyncAction {
                                Type = a.Type, ToRight = a.ToRight, Rel = a.Rel, Size = a.Size,
                                TaskName = a.TaskName, Reason = "✓ wykonano – " + a.Reason }).ToList();
                        SaveHistory(t.Name, obrazPo ?? acts.Where(a => a.Type != ActionType.Same).ToList());
                        if (obrazPo != null)
                        {
                            fullActs[ti] = obrazPo;
                            if (!background && !multi)   // przy wielu zadaniach drzewa się dokleja - nie mieszamy
                            {
                                var pokaz = obrazPo;
                                Ui(() => { ShowActions(tt, pokaz, false); lblSummary.Text = "WYKONANO: " + lblSummary.Text; });
                            }
                        }

                        // Cache z Analyze pokazuje stan SPRZED kopiowania. Bez naniesienia wykonanych
                        // akcji następny szybki skan brałby cel ze starego obrazu, uznał świeżo
                        // skopiowane pliki za "brak w celu" i kopiował wszystko od nowa - w kółko.
                        // Przy błędach/pominięciach nie zgadujemy, co weszło: pełny skan następnym razem.
                        lock (rt)
                        {
                            if (err > 0 || skipped > 0 || rt.CachedL == null || rt.CachedR == null)
                                rt.FullRescanNeeded = true;
                            else foreach (var a in acts)
                            {
                                var srcMap = a.ToRight ? rt.CachedL : rt.CachedR;
                                var dstMap = a.ToRight ? rt.CachedR : rt.CachedL;
                                if (a.Type == ActionType.Copy || a.Type == ActionType.CreateDir)
                                {
                                    Entry e;
                                    if (srcMap.TryGetValue(a.Rel, out e))
                                        dstMap[a.Rel] = new Entry { IsDir = e.IsDir, Size = e.Size, Time = e.Time };
                                    else rt.FullRescanNeeded = true;   // akcja spoza skanu (np. przywrócenie) - nie zgadujemy
                                }
                                else if (a.Type == ActionType.Delete)
                                {
                                    dstMap.Remove(a.Rel);
                                    string pref = a.Rel + "\\";
                                    foreach (var k in dstMap.Keys.Where(x => x.StartsWith(pref, StringComparison.OrdinalIgnoreCase)).ToList())
                                        dstMap.Remove(k);
                                }
                            }
                        }
                        // Archiwum rosnie z kazdym usunieciem i nadpisaniem - jesli uzytkownik
                        // ustawil czas zycia kopii, stare paczki znikaja tu, po udanym przebiegu.
                        if (t.ArchiwumDni > 0) try { Archiwum.Sprzataj(t, t.ArchiwumDni, s => engine.Log(s)); }
                        catch (Exception ex) { engine.Log("  BŁĄD sprzątania archiwum: " + ex.Message); }
                        results[ti] = err == 0 ? "OK, zmian: " + ok + (skipped > 0 ? " (pominięto " + skipped + ")" : "") : "błędów: " + err + " z " + (ok + err);

                        // Migawka nowego stanu - dopiero po niej program potrafi odroznic
                        // "ktos to skasowal" od "to jest nowe". Zapisujemy tylko po przebiegu,
                        // ktory doszedl do konca; przy bledach stan i tak jest znany.
                        if (engine.StanPoSynchronizacji != null)
                        {
                            var stan = new HashSet<string>(engine.StanPoSynchronizacji, StringComparer.OrdinalIgnoreCase);
                            foreach (var a in acts) if (a.Type == ActionType.Delete) stan.Remove(a.Rel);
                            Migawki.Zapisz(t.Name, stan);
                        }
                        copiedSets[ti] = new HashSet<string>(acts.Where(a => a.Type == ActionType.Copy).Select(a => (a.ToRight ? ">" : "<") + a.Rel), StringComparer.OrdinalIgnoreCase);
                    }
                    catch (OperationCanceledException) { engine.Log("  PRZERWANO."); totalErr++; results[ti] = "przerwano"; }
                    catch (Exception ex) { engine.Log("  BŁĄD: " + ex.Message); totalErr++; results[ti] = "BŁĄD: " + ex.Message; lock (rt) rt.FullRescanNeeded = true; }
                }

                Ui(() =>
                {
                    for (int ti = 0; ti < tasks.Count; ti++)
                        if (execute && copiedSets[ti] != null) CheckPingPong(tasks[ti], copiedSets[ti]);
                    bool changedCfg = false;
                    for (int ti = 0; ti < tasks.Count; ti++)
                    {
                        if (!execute || results[ti] == null) continue;
                        tasks[ti].LastSyncAt = DateTime.Now;
                        tasks[ti].LastSyncResult = results[ti].Length > 120 ? results[ti].Substring(0, 120) + "..." : results[ti];
                        changedCfg = true;
                        if (results[ti].StartsWith("OK")) tasks[ti].LastSuccessAt = DateTime.Now;
                        if (background && !results[ti].StartsWith("OK, brak zmian")) OznaczMigniecie(tasks[ti]);

                        // Pierwsza ręczna synchronizacja uzbraja automatykę. Blokada istnieje po to,
                        // żeby człowiek potwierdził KIERUNEK i FOLDERY - a to potwierdza już samo
                        // porównanie, które doszło do końca. Pojedynczy plik nie do skopiowania
                        // (zajęty, chwilowo niedostępny na dysku sieciowym) tego nie podważa i nie
                        // może na stałe wyłączać automatyki. Nie uzbrajamy tylko wtedy, gdy zadanie
                        // w ogóle nie ruszyło - to znaczy, że ścieżki albo ustawienia są złe.
                        bool porownanieDoszlo = results[ti].StartsWith("OK") || results[ti].StartsWith("błędów");
                        if (!background && !porownanieDoszlo && !tasks[ti].AutoArmed &&
                            (tasks[ti].IntervalMinutes > 0 || tasks[ti].WatchChanges))
                            AppendLog("Automatyka zadania \"" + tasks[ti].Name + "\" NIE została uruchomiona – synchronizacja nie doszła do skutku (" +
                                      results[ti] + "). Popraw foldery i kliknij „Synchronizuj” jeszcze raz.");

                        if (!background && porownanieDoszlo && !tasks[ti].AutoArmed)
                        {
                            tasks[ti].AutoArmed = true;
                            Rt(tasks[ti]).ManualSyncAt = DateTime.Now;
                            watchersDirty = true;
                            lastEdit = DateTime.MinValue;
                            if (tasks[ti].IntervalMinutes > 0 || tasks[ti].WatchChanges)
                                AppendLog("Automatyka zadania \"" + tasks[ti].Name + "\" została uruchomiona." +
                                    (results[ti].StartsWith("OK") ? "" : "  (mimo " + results[ti] + " – te pliki spróbuje skopiować przy następnej zmianie)"));
                            lstTasks.Invalidate();   // te same referencje - przerysowanie wystarczy (odporne na filtr listy)
                        }
                    }
                    if (changedCfg) { SaveConfig(); UpdateInfo(); }
                    // po synchronizacji w tle odśwież widok, jeśli oglądasz to zadanie - pełnym obrazem
                    // (z "bez zmian"), a nie zapisaną historią, bo ta zawiera tylko rzeczywiste akcje
                    // i przycinała drzewo do samych zmienionych pozycji, aż do ręcznego "Analizuj"
                    if (background && execute && editing != null && tasks.Contains(editing) && results.Any(r => r != null && r.StartsWith("OK, zmian")))
                    {
                        int idx = tasks.IndexOf(editing);
                        if (idx >= 0 && fullActs[idx] != null) ShowActions(editing, fullActs[idx], false);
                        else ShowHistory(editing);
                    }
                    if (!background && execute && tasks.Count == 1 && results[0] == "OK, brak zmian")
                    {
                        lblSummary.Text = "Brak nowych zmian – foldery są zsynchronizowane.   " + lblSummary.Text;
                    }
                    foreach (var t in tasks)
                    {
                        var rt = Rt(t);
                        lock (rt) { rt.Running = false; rt.IgnoreUntil = DateTime.Now.AddSeconds(3); }
                        if (t.IntervalMinutes > 0 && execute) rt.NextRun = DateTime.Now.AddMinutes(t.IntervalMinutes);
                    }
                    running.Clear();
                    lstTasks.Invalidate();   // kropka "w trakcie" znika
                    if (background && tray != null && totalErr > 0)
                        tray.ShowBalloonTip(5000, "Echo Sync", "Synchronizacja zakończona z błędami – kliknij, aby zobaczyć dziennik.", ToolTipIcon.Warning);
                    SetBusy(false);
                    progress.Value = execute ? 1000 : 0;
                    SetStatus(engine.Cancel ? "Przerwano." : totalErr > 0 ? "Zakończono z błędami – zobacz Dziennik." : "Zakończono pomyślnie.");
                    if (exitAfter) { exitCode = totalErr > 0 ? 1 : 0; reallyExit = true; Close(); }
                    else BeginInvoke((Action)StartPending);
                });
            });
            th.IsBackground = true;
            th.Start();
        }

        // ---------- miniaturowy wykres użycia procesora (jak w Menedżerze zadań) ------------
        // Lekki: jeden licznik wydajności, odczyt co sekundę, rysowanie GDI+ z podwójnym buforem.
        class CpuGraph : Panel
        {
            readonly float[] probki = new float[60];   // ostatnie 60 sekund
            int pos;
            System.Diagnostics.PerformanceCounter licznik;
            readonly System.Windows.Forms.Timer tik = new System.Windows.Forms.Timer { Interval = 1000 };

            public CpuGraph()
            {
                DoubleBuffered = true;
                BackColor = Color.White;
                try { licznik = new System.Diagnostics.PerformanceCounter("Processor", "% Processor Time", "_Total"); licznik.NextValue(); }
                catch { }   // brak liczników wydajności - wykres zostanie pusty, program działa dalej
                tik.Tick += delegate
                {
                    try { probki[pos = (pos + 1) % probki.Length] = licznik != null ? Math.Max(0, Math.Min(100, licznik.NextValue())) : 0; }
                    catch { }
                    Invalidate();
                };
                tik.Start();
                new ToolTip().SetToolTip(this, "Użycie procesora – ostatnie 60 sekund");
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing) { tik.Dispose(); if (licznik != null) licznik.Dispose(); }
                base.Dispose(disposing);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                var g = e.Graphics;
                var rc = new Rectangle(0, 0, Width - 1, Height - 1);
                var blue = Color.FromArgb(17, 125, 187);          // klasyczny błękit Menedżera zadań

                using (var grid = new Pen(Color.FromArgb(217, 234, 244)))
                {
                    for (int x = rc.Right - 1; x > rc.Left; x -= 12) g.DrawLine(grid, x, rc.Top + 1, x, rc.Bottom - 1);   // siatka "płynie" razem z czasem? nie - stała, jak w Windows
                    for (int i = 1; i < 4; i++) { int y = rc.Top + rc.Height * i / 4; g.DrawLine(grid, rc.Left + 1, y, rc.Right - 1, y); }
                }

                var pts = new PointF[probki.Length + 2];
                for (int i = 0; i < probki.Length; i++)
                {
                    float v = probki[(pos + 1 + i) % probki.Length];
                    pts[i] = new PointF(rc.Left + 1 + (rc.Width - 2) * i / (float)(probki.Length - 1),
                                        rc.Bottom - 1 - (rc.Height - 2) * v / 100f);
                }
                pts[probki.Length] = new PointF(rc.Right - 1, rc.Bottom - 1);
                pts[probki.Length + 1] = new PointF(rc.Left + 1, rc.Bottom - 1);

                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (var fill = new SolidBrush(Color.FromArgb(55, blue))) g.FillPolygon(fill, pts);
                using (var pen = new Pen(blue, 1.4f)) g.DrawLines(pen, pts.Take(probki.Length).ToArray());
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;

                using (var border = new Pen(Color.FromArgb(160, 190, 210))) g.DrawRectangle(border, rc);
                using (var f = new Font("Segoe UI", 7.5f))
                using (var tekst = new SolidBrush(Color.FromArgb(90, 110, 125)))
                    g.DrawString("CPU " + (int)probki[pos] + "%", f, tekst, rc.Left + 3, rc.Top + 2);
            }
        }

        // ---------- wykrywanie "ping-pongu": te same pliki kopiowane w każdej synchronizacji,
        // bo inny program (np. na drugim komputerze) cofa je do starszej wersji
        // kiedy ostatnio zalogowano niedostępność folderów zadania (dziennik bez powtórek)
        readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTime> niedostepneLog = new System.Collections.Concurrent.ConcurrentDictionary<string, DateTime>();

        class PingPongState { public HashSet<string> Last; public int Streak; public DateTime LastRun, Warned; }
        readonly Dictionary<SyncTask, PingPongState> pingPong = new Dictionary<SyncTask, PingPongState>();

        void CheckPingPong(SyncTask t, HashSet<string> copied)
        {
            PingPongState st;
            if (!pingPong.TryGetValue(t, out st)) { st = new PingPongState(); pingPong[t] = st; }
            var now = DateTime.Now;
            var prev = st.Last;
            int common = copied.Count == 0 || prev == null ? 0 : copied.Count(k => prev.Contains(k));
            bool repeated = common >= 3 && common >= copied.Count / 2 && (now - st.LastRun).TotalMinutes < 30;
            st.Streak = repeated ? st.Streak + 1 : 0;
            st.Last = copied;
            st.LastRun = now;

            var rt = Rt(t);
            if (st.Streak < 2) { rt.PingPongBackoffUntil = DateTime.MinValue; return; }

            // Zamiast kopiować w kółko przy każdej reakcji watchera, z każdym potwierdzeniem pętli
            // wydłużamy przerwę, aż drugi program (bez własnego mechanizmu obserwacji zmian) przestanie
            // nadpisywać te pliki albo człowiek ręcznie zsynchronizuje zadanie.
            int minut = Math.Min(30, 2 * st.Streak);
            rt.PingPongBackoffUntil = now.AddMinutes(minut);

            if ((now - st.Warned).TotalHours < 1) return;   // ostrzeżenie w dzienniku/tray max raz na godzinę
            st.Warned = now;

            var example = copied.Where(k => prev.Contains(k)).Take(3).Select(k => (k[0] == '>' ? "→ " : "← ") + k.Substring(1)).ToList();
            AppendLog("⚠ UWAGA \"" + t.Name + "\": te same pliki (" + common + ") są kopiowane w każdej synchronizacji. " +
                      "Zwykle oznacza to, że po drugiej stronie działa inny program (np. własny mechanizm zapisu/synchronizacji bez porządnej " +
                      "obserwacji zmian, w przeciwieństwie do Echo Sync), który co chwilę nadpisuje te pliki starszą wersją. " +
                      "Reakcja na zmiany w tym zadaniu jest teraz wygaszona na " + minut + " min, żeby nie kopiować bez końca – " +
                      "\"Synchronizuj\" ręcznie zadziała normalnie. Przykłady: " + string.Join("; ", example));
            if (tray != null)
                tray.ShowBalloonTip(8000, "Echo Sync – zapętlona synchronizacja",
                    "„" + t.Name + "”: " + common + " plików jest kopiowanych w kółko – inny program cofa zmiany. Automatyka zwolni tempo. Szczegóły w Dzienniku.", ToolTipIcon.Warning);
        }

        void OnClosing(object sender, FormClosingEventArgs e)
        {
            if (!reallyExit && !autoMode && tray != null && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                HideToTray();
                if (!trayHintShown)
                {
                    trayHintShown = true;
                    tray.ShowBalloonTip(3000, "Echo Sync", "Program działa dalej w tle. Zamkniesz go z menu ikony w tray.", ToolTipIcon.Info);
                }
                return;
            }
            if (busy && !autoMode)
            {
                if (MessageBox.Show("Trwa synchronizacja. Przerwać i zamknąć?", "Zamknij", MessageBoxButtons.YesNo) != DialogResult.Yes)
                {
                    e.Cancel = true;
                    reallyExit = false;
                    return;
                }
                engine.Cancel = true;
            }
            scheduler.Stop();
            animTimer.Stop();
            foreach (var rt in runtimes.Values) DisposeWatchers(rt);
            if (tray != null) { tray.Visible = false; tray.Dispose(); }
            if (!autoMode) SaveConfig();
            if (logTimer != null) logTimer.Stop();
            FlushLog();
            if (logFile != null) logFile.Dispose();
            Environment.ExitCode = exitCode;
        }
    }

    static class Program
    {
        // Parametry:  /auto            – uruchom wszystkie włączone zadania i zamknij
        //             /auto "Nazwa"    – uruchom jedno zadanie i zamknij
        //             /tray            – start ukryty w tray (autostart)
        [STAThread]
        static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            bool auto = args.Length > 0 && (args[0].Equals("/auto", StringComparison.OrdinalIgnoreCase) || args[0].Equals("-auto", StringComparison.OrdinalIgnoreCase));
            bool hidden = args.Any(a => a.Equals("/tray", StringComparison.OrdinalIgnoreCase));
            string name = auto && args.Length > 1 ? args[1] : null;

            System.Threading.Mutex mutex = null;
            if (!auto)
            {
                bool created;
                // GetHashCode() differs between processes, so the mutex name must be fixed.
                mutex = new System.Threading.Mutex(true, @"Local\MojSynchronizator_EchoSync", out created);
                if (!created)
                {
                    if (!hidden)
                        MessageBox.Show("Program już działa – znajdziesz go jako ikonę w tray (obok zegara).", "Echo Sync",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return 0;
                }
            }

            var form = new MainForm(auto, name, hidden);
            if (auto) form.WindowState = FormWindowState.Minimized;
            Application.Run(form);
            GC.KeepAlive(mutex);
            return Environment.ExitCode;
        }
    }
}