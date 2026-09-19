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
    public enum SyncDirection { LeftToRight, RightToLeft, TwoWay }
    public enum DeleteMode { Archive, RecycleBin, Permanent }

    public class SyncTask
    {
        public string Name = "Nowe zadanie";
        public string Left = "";
        public string Right = "";
        public SyncDirection Direction = SyncDirection.LeftToRight;
        public bool Mirror = false;
        public DeleteMode DeleteMode = DeleteMode.Archive;
        public string Excludes = "Thumbs.db;desktop.ini;~$*;*.tmp";
        public bool Enabled = true;
        public int IntervalMinutes = 0;      // 0 = wyłączone
        public bool WatchChanges = false;    // synchronizacja po wykryciu zmian
        public int WatchDelaySeconds = 5;    // ile ciszy po ostatniej zmianie przed startem (5 s ... 5 min)
        public DateTime CreatedAt = DateTime.MinValue;
        public DateTime LastSyncAt = DateTime.MinValue;
        public string LastSyncResult = "";
        public DateTime LastSuccessAt = DateTime.MinValue;

        [XmlIgnore] public bool Unsaved;     // nowe zadanie, jeszcze nie zapisane – nie bierze udziału w automatyce

        public override string ToString()
        {
            return (Unsaved ? "✱ " : "") + (Enabled ? "" : "[wył.] ") + Name +
                   (IntervalMinutes > 0 ? "  ⏱" + IntervalMinutes + "m" : "") +
                   (WatchChanges ? "  ⚡" + (WatchDelaySeconds >= 60 ? (WatchDelaySeconds / 60) + "m" : WatchDelaySeconds + "s") : "") +
                   (CzekaDecyzji > 0 ? "  ❗" + CzekaDecyzji : "") +
                   (!Unsaved && !AutoArmed && (IntervalMinutes > 0 || WatchChanges) ? "  ⏸" : "");
        }

        public bool SubFolder = false;       // w celu twórz podfolder o nazwie folderu źródłowego
        public bool AutoArmed = true;        // automatyka aktywna dopiero po pierwszej ręcznej synchronizacji
        [XmlIgnore] public int CzekaDecyzji;  // ile usunięć z drugiej strony czeka na decyzję (nie zapisywane)
        public bool ZachowajPoprzednieWersje = true;   // nadpisywany plik najpierw do _SyncArchive
        public int ArchiwumDni = 0;          // 0 = trzymaj bez konca; >0 = kasuj paczki starsze niz N dni
        public string StalaDecyzjaZnikniete = "";   // "" = pytaj; "przywroc"/"usun" = stosuj bez pytania (wycofywalne w edytorze)
        [XmlIgnore] public bool TargetMayBeMissing;

        public SyncTask Clone() { return (SyncTask)MemberwiseClone(); }

        public static string SourceName(string src)
        {
            try
            {
                string p = Engine.Norm(src);
                if (p.Length == 0) return "";
                string n = Path.GetFileName(p.TrimEnd('\\'));
                if (string.IsNullOrEmpty(n)) n = "Dysk_" + p.Substring(0, 1);   // np. W:\ → Dysk_W
                return n;
            }
            catch { return ""; }
        }

        // kopia zadania z faktycznymi ścieżkami (uwzględnia podfolder w celu)
        public SyncTask Resolved()
        {
            var c = Clone();
            if (!SubFolder) return c;
            try
            {
                bool srcLeft = Direction != SyncDirection.RightToLeft;
                string src = srcLeft ? Left : Right;
                string dst = Engine.Norm(srcLeft ? Right : Left);
                string name = SourceName(src);
                if (name.Length == 0 || dst.Length == 0) return c;
                if (Path.GetFileName(dst).Equals(name, StringComparison.OrdinalIgnoreCase)) return c;  // cel już kończy się tą nazwą
                string nd = Path.Combine(dst, name);
                if (srcLeft) c.Right = nd; else c.Left = nd;
                c.TargetMayBeMissing = true;
            }
            catch { }
            return c;
        }
    }

    // ---- Archiwum kopii (_SyncArchive) ------------------------------------------------
    // Kazde usuniecie i kazde nadpisanie zostawia paczke ze stemplem czasu. Archiwum jest
    // tym, co pozwala cofnac bledna decyzje - ale rosnie. Ponizsze funkcje pozwalaja je
    // policzyc, wyczyscic recznie i kasowac automatycznie po ustalonym czasie.
    public class PaczkaArchiwum
    {
        public string Sciezka;
        public string Korzen;          // ktory folder zadania ja trzyma
        public DateTime Kiedy;
        public int Plikow;
        public long Bajtow;
        public string Nazwa { get { return Path.GetFileName(Sciezka); } }
    }

    public static class Archiwum
    {
        public static readonly int[] DNI = { 0, 7, 14, 30, 60, 90, 180, 365 };
        public static readonly string[] NAZWY_DNI = { "nigdy – trzymaj wszystko", "po 7 dniach", "po 14 dniach",
            "po 30 dniach", "po 60 dniach", "po 90 dniach", "po pół roku", "po roku" };

        public static string OpisOkresu(int dni)
        {
            int i = Array.IndexOf(DNI, dni);
            return i >= 0 ? NAZWY_DNI[i] : "po " + dni + " dniach";
        }

        // Foldery _SyncArchive obu stron zadania (bez duplikatow, gdy sciezki sie pokrywaja).
        public static List<string> Korzenie(SyncTask t)
        {
            var l = new List<string>();
            foreach (string k in new[] { Engine.Norm(t.Left), Engine.Norm(t.Right) })
            {
                if (string.IsNullOrEmpty(k)) continue;
                string a = Path.Combine(k, Engine.ArchiveDir);
                if (!l.Any(x => x.Equals(a, StringComparison.OrdinalIgnoreCase))) l.Add(a);
            }
            return l;
        }

        static DateTime Data(string dir)
        {
            DateTime d;
            if (DateTime.TryParseExact(Path.GetFileName(dir), "yyyy-MM-dd_HHmmss",
                System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out d))
                return d;
            try { return Directory.GetCreationTime(dir); } catch { return DateTime.Now; }
        }

        public static List<PaczkaArchiwum> Spis(SyncTask t)
        {
            var wynik = new List<PaczkaArchiwum>();
            foreach (string korzen in Korzenie(t))
            {
                if (!Directory.Exists(korzen)) continue;
                string[] pod;
                try { pod = Directory.GetDirectories(korzen); }
                catch { continue; }
                foreach (string d in pod)
                {
                    var p = new PaczkaArchiwum { Sciezka = d, Korzen = korzen, Kiedy = Data(d) };
                    try
                    {
                        foreach (string f in Directory.GetFiles(d, "*", SearchOption.AllDirectories))
                        {
                            p.Plikow++;
                            try { p.Bajtow += new FileInfo(f).Length; }
                            catch { }
                        }
                    }
                    catch { }
                    wynik.Add(p);
                }
            }
            wynik.Sort((a, b) => b.Kiedy.CompareTo(a.Kiedy));   // najnowsze na gorze
            return wynik;
        }

        // Usuwa drzewo na twardo - takze pliki z atrybutem "tylko do odczytu".
        public static void UsunDrzewo(string dir)
        {
            try
            {
                foreach (string f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                    try { File.SetAttributes(f, FileAttributes.Normal); }
                    catch { }
            }
            catch { }
            Directory.Delete(dir, true);
        }

        // Kasuje wskazane paczki. Zwraca, ile udalo sie usunac; bledy trafiaja do logu.
        public static int Usun(IEnumerable<PaczkaArchiwum> paczki, Action<string> log, out long zwolnione)
        {
            int n = 0;
            zwolnione = 0;
            foreach (var p in paczki)
            {
                try
                {
                    if (Directory.Exists(p.Sciezka)) UsunDrzewo(p.Sciezka);
                    zwolnione += p.Bajtow;
                    n++;
                }
                catch (Exception ex)
                {
                    if (log != null) log("  Nie udalo sie usunac " + p.Sciezka + ": " + ex.Message);
                }
            }
            return n;
        }

        // Automatyczne sprzatanie: znika to, co starsze niz `dni`. Zwraca liczbe usunietych paczek.
        public static int Sprzataj(SyncTask t, int dni, Action<string> log)
        {
            if (dni <= 0) return 0;
            DateTime granica = DateTime.Now.AddDays(-dni);
            var stare = Spis(t).Where(p => p.Kiedy < granica).ToList();
            if (stare.Count == 0) return 0;
            long zwolnione;
            int n = Usun(stare, log, out zwolnione);
            if (n > 0 && log != null)
                log("  Archiwum: usunieto " + n + " paczek starszych niz " + dni + " dni (" + MainForm.Rozmiar(zwolnione) + ")");
            return n;
        }
    }

    public class Config
    {
        public List<SyncTask> Tasks = new List<SyncTask>();
        public bool AutomationEnabled = true;

        public static string AppDir { get { return Path.GetDirectoryName(Application.ExecutablePath); } }
        static string FilePath { get { return Path.Combine(AppDir, "zadania.xml"); } }

        public static Config Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    using (var fs = File.OpenRead(FilePath))
                        return (Config)new XmlSerializer(typeof(Config)).Deserialize(fs);
            }
            catch (Exception ex) { MessageBox.Show("Nie udało się wczytać zadania.xml:\n" + ex.Message); }
            return new Config();
        }

        public void Save()
        {
            string tmp = FilePath + ".tmp";
            var copy = new Config { AutomationEnabled = AutomationEnabled, Tasks = Tasks.Where(t => !t.Unsaved).ToList() };
            using (var fs = File.Create(tmp))
                new XmlSerializer(typeof(Config)).Serialize(fs, copy);
            if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null);
            else File.Move(tmp, FilePath);
        }
    }

    // ------------------------------------------------------------------ SILNIK

    public class Entry { public bool IsDir; public long Size; public DateTime Time; }

    public enum ActionType { Delete, CreateDir, Copy, Same }

    public class SyncAction
    {
        public ActionType Type;
        public bool ToRight;      // strona docelowa: true = prawa, false = lewa
        public string Rel;
        public long Size;
        public string Reason;
        public DateTime? SrcTime, DstTime;   // UTC
        public string TaskName = "";
        public string SrcPath, DstPath;      // pełne ścieżki (do otwierania w Eksploratorze)
        // Usunięcie przyjęte z drugiej strony kasuje OSTATNIĄ kopię pliku - wtedy zawsze robimy
        // kopię zapasową w _SyncArchive, nawet gdy zadanie ma ustawione usuwanie trwałe.
        public bool ZawszeDoArchiwum;

        public string Describe()
        {
            string arrow = ToRight ? "→" : "←";
            switch (Type)
            {
                case ActionType.Copy: return "Kopiuj " + arrow;
                case ActionType.CreateDir: return "Utwórz folder " + arrow;
                case ActionType.Same: return "= Bez zmian";
                default: return "Usuń " + (ToRight ? "(prawy)" : "(lewy)");
            }
        }
    }

    // Węzeł drzewa "Podglądu zmian": grupuje akcje po folderach, żeby dało się je zwijać/rozwijać
    // i usuwać całe foldery naraz (nie tylko pojedyncze pliki, które akurat mają swoją akcję).
    public class ActionNode
    {
        public string Name;                 // nazwa segmentu (folderu/pliku) albo nazwy zadania
        public string Rel = "";             // ścieżka względna od korzenia zadania ("" = korzeń)
        public bool IsFolder;
        public bool IsTaskHeader;            // wiersz-nagłówek zadania (tryb "append" - kilka zadań na liście)
        public SyncAction Action;            // realna akcja (Copy/CreateDir/Delete/Same na pliku) - może być null dla folderu bez zmian
        public SyncTask Task;
        public string Key;                   // unikalny klucz do zapamiętania stanu zwiń/rozwiń
        public long Size;                    // dla folderów: suma rozmiarów wszystkich plików w środku (rekurencyjnie)
        public ActionNode Parent;            // do rozwijania przodków przy wyszukiwaniu
        public List<ActionNode> Children;    // tworzone tylko dla folderów
        public Dictionary<string, ActionNode> ChildIndex;
        SyncAction pseudo;

        // Akcja używana do usuwania: prawdziwa, jeśli jest, inaczej "wirtualna" reprezentująca
        // istniejący (niezmieniony) folder - dzięki temu też można go zaznaczyć i skasować z dysku.
        public SyncAction EffectiveAction
        {
            get
            {
                if (Action != null) return Action;
                if (!IsFolder || IsTaskHeader || string.IsNullOrEmpty(Rel) || Task == null) return null;
                if (pseudo == null)
                {
                    string l = Engine.Norm(Task.Left), r = Engine.Norm(Task.Right);
                    pseudo = new SyncAction
                    {
                        Type = ActionType.Same, ToRight = true, Rel = Rel, Reason = "folder",
                        SrcPath = Path.Combine(l, Rel), DstPath = Path.Combine(r, Rel)
                    };
                }
                return pseudo;
            }
        }
    }

    // Pozycja, która po jednej stronie zniknęła, a w migawce z poprzedniej synchronizacji była.
    // To jedyny sposób, żeby odróżnić "ktoś to skasował" od "to jest nowe po drugiej stronie" -
    // samo usunięcie nie zostawia daty, więc bez pamięci poprzedniego stanu jest to nierozstrzygalne.
    public class ZnikloPoDrugiejStronie
    {
        public string Rel;
        public bool IsDir;
        public long Size;
        public bool ZnikloWCelu;   // true: skasowane po stronie celu (lustro chce to przywrócić)
        public bool UsunPoPrawej;  // gdzie trzeba usunąć, żeby PRZYJĄĆ to usunięcie
        public bool DoDecyzji;     // true: wyjęte z działań i czeka na człowieka (dwukierunkowa)
                                   // false: lustro i tak to przywróci - tylko informujemy
        public string Opis;
    }

    // Migawka: lista ścieżek względnych znanych po ostatniej udanej synchronizacji zadania.
    static class Migawki
    {
        static string Plik(string nazwaZadania)
        {
            string safe = new string(nazwaZadania.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch).ToArray());
            return Path.Combine(Path.Combine(Config.AppDir, "migawki"), safe + ".txt");
        }

        public static HashSet<string> Wczytaj(string nazwaZadania)
        {
            var z = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string f = Plik(nazwaZadania);
                if (File.Exists(f))
                    foreach (string l in File.ReadAllLines(f, Encoding.UTF8))
                        if (!string.IsNullOrWhiteSpace(l)) z.Add(l);
            }
            catch { }
            return z;
        }

        public static void Zapisz(string nazwaZadania, IEnumerable<string> sciezki)
        {
            try
            {
                string f = Plik(nazwaZadania);
                Directory.CreateDirectory(Path.GetDirectoryName(f));
                File.WriteAllLines(f, sciezki.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(), new UTF8Encoding(false));
            }
            catch { }
        }

        public static void Skasuj(string nazwaZadania)
        {
            try { string f = Plik(nazwaZadania); if (File.Exists(f)) File.Delete(f); } catch { }
        }
    }

    public class Engine
    {
        public volatile bool Cancel;

        // wejście: stan z poprzedniej udanej synchronizacji (pusty zbiór = nie wiemy nic, nic nie wnioskujemy)
        public HashSet<string> Migawka;
        // wyjście: co zniknęło po jednej ze stron, choć w migawce było
        public List<ZnikloPoDrugiejStronie> Znikniete = new List<ZnikloPoDrugiejStronie>();
        // wyjście: stan do zapisania po udanej synchronizacji
        public HashSet<string> StanPoSynchronizacji;
        // ostatni odczytany obraz obu stron - baza dla nastepnego szybkiego (przyrostowego) skanu
        public Dictionary<string, Entry> LastLeftScan, LastRightScan;
        public Action<string> Log = delegate { };
        public Action<string> Status = delegate { };
        public Action<int> Progress = delegate { };
        public Action<string> Speed = delegate { };   // aktualna predkosc odczytu+zapisu (jeden strumien - kopiowanie czyta i zaraz pisze)

        public const string ArchiveDir = "_SyncArchive";
        public const string PendingDir = "_SyncPending";   // tymczasowy schowek na czas decyzji
        const string TmpExt = ".~synctmp";
        static readonly string[] RootSkip = { ArchiveDir, PendingDir, "$RECYCLE.BIN", "System Volume Information" };

        List<Regex> excl = new List<Regex>();
        long totalBytes, doneBytes;
        int lastTick;

        public static string Norm(string p)
        {
            p = (p ?? "").Trim().Trim('"');
            if (p.Length == 0) return p;
            p = Path.GetFullPath(p);
            if (p.Length > 3 && p.EndsWith("\\")) p = p.TrimEnd('\\');
            return p;
        }

        void BuildExcludes(string s)
        {
            excl.Clear();
            foreach (string raw in (s ?? "").Split(new[] { ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string p = raw.Trim();
                if (p.Length == 0) continue;
                string rx = "^" + Regex.Escape(p.Replace('/', '\\')).Replace("\\*", ".*").Replace("\\?", ".") + "$";
                excl.Add(new Regex(rx, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
            }
        }

        bool Excluded(string rel, string name)
        {
            if (name.EndsWith(TmpExt, StringComparison.OrdinalIgnoreCase)) return true;
            foreach (var r in excl)
                if (r.IsMatch(name) || r.IsMatch(rel)) return true;
            return false;
        }

        Dictionary<string, Entry> Scan(string root, HashSet<string> onlyTop = null)
        {
            var map = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
            if (!Directory.Exists(root)) return map;
            var stack = new Stack<string>();
            stack.Push("");
            int n = 0;
            while (stack.Count > 0)
            {
                if (Cancel) throw new OperationCanceledException();
                string rel = stack.Pop();
                string full = rel.Length == 0 ? root : Path.Combine(root, rel);
                FileSystemInfo[] items;
                try { items = new DirectoryInfo(full).GetFileSystemInfos(); }
                catch (Exception ex)
                {
                    if (rel.Length == 0) throw;
                    Log("  BŁĄD odczytu: " + full + " – " + ex.Message);
                    continue;
                }
                foreach (var fi in items)
                {
                    if (rel.Length == 0 && RootSkip.Any(x => x.Equals(fi.Name, StringComparison.OrdinalIgnoreCase))) continue;
                    if (rel.Length == 0 && onlyTop != null && !onlyTop.Contains(fi.Name)) continue;
                    string r = rel.Length == 0 ? fi.Name : rel + "\\" + fi.Name;
                    if (Excluded(r, fi.Name)) continue;
                    bool isDir = (fi.Attributes & FileAttributes.Directory) != 0;
                    if (isDir)
                    {
                        if ((fi.Attributes & FileAttributes.ReparsePoint) != 0)
                        {
                            Log("  Pominięto dowiązanie (junction/symlink): " + fi.FullName);
                            continue;
                        }
                        map[r] = new Entry { IsDir = true };
                        stack.Push(r);
                    }
                    else
                    {
                        map[r] = new Entry { Size = ((FileInfo)fi).Length, Time = fi.LastWriteTimeUtc };
                    }
                    if (++n % 500 == 0) Status("Skanowanie " + root + ": " + n + " elementów...");
                }
            }
            return map;
        }

        static string TopOf(string rel)
        {
            int i = rel.IndexOf('\\');
            return i < 0 ? rel : rel.Substring(0, i);
        }

        // Skan przyrostowy: czyta z dysku TYLKO wskazane foldery najwyższego poziomu, resztę bierze
        // z ostatniego pełnego skanu - jak własny indeks AllwaySync. Bez cache albo bez wskazanych
        // folderów wraca do zwykłego pełnego skanu (bezpieczny wariant domyślny).
        Dictionary<string, Entry> ScanMerge(string root, HashSet<string> onlyTop, Dictionary<string, Entry> baseMap)
        {
            if (onlyTop == null || onlyTop.Count == 0 || baseMap == null) return Scan(root);
            var partial = Scan(root, onlyTop);
            var merged = new Dictionary<string, Entry>(baseMap, StringComparer.OrdinalIgnoreCase);
            foreach (var k in merged.Keys.Where(k => onlyTop.Contains(TopOf(k))).ToList()) merged.Remove(k);
            foreach (var kv in partial) merged[kv.Key] = kv.Value;
            return merged;
        }

        static bool Differ(Entry a, Entry b)
        {
            return a.Size != b.Size || Math.Abs((a.Time - b.Time).TotalSeconds) > 2;
        }

        static bool Newer(Entry a, Entry b) { return (a.Time - b.Time).TotalSeconds > 2; }

        static bool SameContent(string left, string right)
        {
            try
            {
                using (var a = File.OpenRead(left))
                using (var b = File.OpenRead(right))
                using (var hash = System.Security.Cryptography.SHA256.Create())
                {
                    byte[] leftHash = hash.ComputeHash(a);
                    hash.Initialize();
                    byte[] rightHash = hash.ComputeHash(b);
                    return leftHash.SequenceEqual(rightHash);
                }
            }
            catch { return false; }
        }

        static bool AncestorIn(string rel, HashSet<string> set)
        {
            int i = rel.LastIndexOf('\\');
            while (i > 0)
            {
                rel = rel.Substring(0, i);
                if (set.Contains(rel)) return true;
                i = rel.LastIndexOf('\\');
            }
            return false;
        }

        static SyncAction Add(Entry e, string rel, bool toRight, string reason)
        {
            return new SyncAction
            {
                Type = e.IsDir ? ActionType.CreateDir : ActionType.Copy,
                ToRight = toRight, Rel = rel, Size = e.Size, Reason = reason
            };
        }

        static SyncAction SameAct(Entry e, string rel, bool toRight, string reason)
        {
            return new SyncAction { Type = ActionType.Same, ToRight = toRight, Rel = rel, Size = e.Size, Reason = reason };
        }

        public List<SyncAction> Analyze(SyncTask t, HashSet<string> onlyTop = null, Dictionary<string, Entry> baseL = null, Dictionary<string, Entry> baseR = null)
        {
            string L = Norm(t.Left), R = Norm(t.Right);
            if (L.Length == 0 || R.Length == 0) throw new Exception("Nie podano obu folderów.");
            if (L.Equals(R, StringComparison.OrdinalIgnoreCase)) throw new Exception("Foldery są identyczne.");
            if (R.StartsWith(L + "\\", StringComparison.OrdinalIgnoreCase) || L.StartsWith(R + "\\", StringComparison.OrdinalIgnoreCase))
                throw new Exception("Jeden folder znajduje się wewnątrz drugiego.");

            bool needL = t.Direction != SyncDirection.RightToLeft;
            bool needR = t.Direction != SyncDirection.LeftToRight;
            if (t.TargetMayBeMissing && t.Direction == SyncDirection.TwoWay) needR = false;   // podfolder powstanie przy kopiowaniu
            if (needL && !Directory.Exists(L)) throw new Exception("Folder lewy jest niedostępny: " + L);
            if (needR && !Directory.Exists(R)) throw new Exception("Folder prawy jest niedostępny: " + R);

            BuildExcludes(t.Excludes);
            bool szybki = onlyTop != null && onlyTop.Count > 0 && baseL != null && baseR != null;
            Status("Skanowanie: " + L);
            var lm = szybki ? ScanMerge(L, onlyTop, baseL) : Scan(L);
            Status("Skanowanie: " + R);
            var rm = szybki ? ScanMerge(R, onlyTop, baseR) : Scan(R);
            LastLeftScan = lm; LastRightScan = rm;
            Log("  Lewy: " + lm.Count + " elementów, prawy: " + rm.Count + " elementów" +
                (szybki ? "  [szybki skan: " + onlyTop.Count + " zmienionych folderów, reszta z pamięci]" : ""));

            var acts = new List<SyncAction>();

            if (t.Direction == SyncDirection.TwoWay)
            {
                foreach (var kv in lm)
                {
                    Entry r;
                    if (!rm.TryGetValue(kv.Key, out r)) acts.Add(Add(kv.Value, kv.Key, true, "brak po prawej"));
                    else if (kv.Value.IsDir != r.IsDir) Log("  KONFLIKT (plik/folder): " + kv.Key + " – pominięto");
                    else if (!r.IsDir && !Differ(kv.Value, r)) acts.Add(SameAct(kv.Value, kv.Key, true, "identyczne"));
                    else if (!r.IsDir)
                    {
                        string leftPath = Path.Combine(L, kv.Key);
                        string rightPath = Path.Combine(R, kv.Key);
                        if (kv.Value.Size == r.Size && SameContent(leftPath, rightPath))
                            acts.Add(SameAct(kv.Value, kv.Key, true, "identyczne dane (różna data)"));
                        else if (Newer(kv.Value, r)) acts.Add(Add(kv.Value, kv.Key, true, "nowszy po lewej"));
                        else if (Newer(r, kv.Value)) acts.Add(Add(r, kv.Key, false, "nowszy po prawej"));
                        else
                        {
                            Log("  KONFLIKT (ta sama data, inny rozmiar): " + kv.Key + " – pominięto");
                            acts.Add(SameAct(kv.Value, kv.Key, true, "KONFLIKT – ta sama data, inny rozmiar (pominięto)"));
                        }
                    }
                }
                foreach (var kv in rm)
                    if (!lm.ContainsKey(kv.Key)) acts.Add(Add(kv.Value, kv.Key, false, "brak po lewej"));

                // Usunięcia: coś było w migawce, jest tylko po jednej stronie -> ktoś to skasował
                // po drugiej. Takich pozycji NIE kopiujemy z powrotem bez pytania - wyjmujemy je
                // z listy działań i oddajemy do decyzji człowieka.
                if (Migawka != null && Migawka.Count > 0)
                {
                    var doDecyzji = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (string key in Migawka)
                    {
                        bool wL = lm.ContainsKey(key), wP = rm.ContainsKey(key);
                        if (wL == wP) continue;                     // jest po obu albo znikło po obu - nie ma sprawy
                        var e = wL ? lm[key] : rm[key];
                        doDecyzji.Add(key);
                        Znikniete.Add(new ZnikloPoDrugiejStronie {
                            Rel = key, IsDir = e.IsDir, Size = e.Size, ZnikloWCelu = wL,
                            UsunPoPrawej = wP, DoDecyzji = true,     // zostało po prawej -> przyjęcie usunięcia = skasuj po prawej
                            Opis = wL ? "skasowane po prawej stronie" : "skasowane po lewej stronie" });
                    }
                    if (doDecyzji.Count > 0)
                        acts.RemoveAll(a => a.Type == ActionType.Copy || a.Type == ActionType.CreateDir
                                            ? doDecyzji.Contains(a.Rel) : false);
                }
            }
            else
            {
                bool toRight = t.Direction == SyncDirection.LeftToRight;
                var src = toRight ? lm : rm;
                var dst = toRight ? rm : lm;

                if (t.Mirror && src.Count == 0 && dst.Count > 0)
                    throw new Exception("Źródło jest PUSTE, a cel nie – kopia lustrzana przerwana dla bezpieczeństwa " +
                                        "(czy dysk sieciowy na pewno jest podłączony?).");

                var deleted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in src)
                {
                    Entry d;
                    var s = kv.Value;
                    if (!dst.TryGetValue(kv.Key, out d)) acts.Add(Add(s, kv.Key, toRight, "brak w celu"));
                    else if (s.IsDir != d.IsDir)
                    {
                        if (t.Mirror)
                        {
                            acts.Add(new SyncAction { Type = ActionType.Delete, ToRight = toRight, Rel = kv.Key, Size = d.Size, Reason = "zmiana plik/folder" });
                            deleted.Add(kv.Key);
                            acts.Add(Add(s, kv.Key, toRight, "zmiana plik/folder"));
                        }
                        else Log("  KONFLIKT (plik/folder): " + kv.Key + " – pominięto");
                    }
                    else if (!s.IsDir && !Differ(s, d)) acts.Add(SameAct(s, kv.Key, toRight, "identyczne"));
                    else if (!s.IsDir)
                    {
                        string sourcePath = Path.Combine(toRight ? L : R, kv.Key);
                        string targetPath = Path.Combine(toRight ? R : L, kv.Key);
                        if (s.Size == d.Size && SameContent(sourcePath, targetPath))
                            acts.Add(SameAct(s, kv.Key, toRight, "identyczne dane (różna data)"));
                        else if (Newer(s, d)) acts.Add(Add(s, kv.Key, toRight, "nowszy w źródle"));
                        else if (t.Mirror) acts.Add(Add(s, kv.Key, toRight, Newer(d, s) ? "cel nowszy – nadpisanie (lustro)" : "inny rozmiar"));
                        else
                        {
                            Log("  Pominięto (cel nowszy lub inny rozmiar): " + kv.Key);
                            acts.Add(SameAct(s, kv.Key, toRight, "pominięto – w celu nowszy/inny (bez lustra)"));
                        }
                    }
                }

                if (t.Mirror)
                {
                    foreach (var key in dst.Keys.Where(k => !src.ContainsKey(k)).OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
                    {
                        if (AncestorIn(key, deleted)) continue;
                        var d = dst[key];
                        acts.Add(new SyncAction { Type = ActionType.Delete, ToRight = toRight, Rel = key, Size = d.Size, Reason = "brak w źródle" });
                        if (d.IsDir) deleted.Add(key);
                    }

                    // Lustro odtwarza to, co ktoś skasował w celu - i to jest poprawne. Ale człowiek
                    // ma o tym wiedzieć, bo to znak, że ktoś po drugiej stronie coś zrobił.
                    if (Migawka != null && Migawka.Count > 0)
                        foreach (string key in Migawka)
                        {
                            Entry s;
                            if (dst.ContainsKey(key) || !src.TryGetValue(key, out s)) continue;
                            Znikniete.Add(new ZnikloPoDrugiejStronie {
                                Rel = key, IsDir = s.IsDir, Size = s.Size, ZnikloWCelu = true,
                                UsunPoPrawej = !toRight, DoDecyzji = false,  // przyjęcie usunięcia = skasuj po stronie ŹRÓDŁA
                                Opis = "skasowane w celu, lustro chce to przywrócić" });
                        }
                }
            }

            // stan do zapamiętania po udanej synchronizacji: wszystko, co po niej zostanie
            var stan = new HashSet<string>(lm.Keys, StringComparer.OrdinalIgnoreCase);
            foreach (string k in rm.Keys) stan.Add(k);
            foreach (var a in acts) if (a.Type == ActionType.Delete) stan.Remove(a.Rel);
            // UWAGA: pozycje czekające na decyzję ZOSTAJĄ w migawce. Gdyby wypadły, następny przebieg
            // uznałby je za nowe pliki, po cichu przywrócił i człowiek nigdy nie zdążyłby zdecydować.
            // Znikną z migawki dopiero wtedy, gdy powstanie dla nich działanie Usuń (wyżej).
            StanPoSynchronizacji = stan;

            // daty plików do podglądu
            foreach (var a in acts)
            {
                var srcMap = a.ToRight ? lm : rm;
                var dstMap = a.ToRight ? rm : lm;
                Entry e;
                a.TaskName = t.Name;
                a.SrcPath = Path.Combine(a.ToRight ? L : R, a.Rel);
                a.DstPath = Path.Combine(a.ToRight ? R : L, a.Rel);
                if ((a.Type == ActionType.Copy || a.Type == ActionType.Same) && srcMap.TryGetValue(a.Rel, out e) && !e.IsDir) a.SrcTime = e.Time;
                if (dstMap.TryGetValue(a.Rel, out e) && !e.IsDir) a.DstTime = e.Time;
            }

            return acts.OrderBy(a => (int)a.Type)
                       .ThenBy(a => a.Rel, StringComparer.OrdinalIgnoreCase)
                       .ToList();
        }

        public void Execute(SyncTask t, List<SyncAction> acts, out int ok, out int err)
        {
            string L = Norm(t.Left), R = Norm(t.Right);
            string stamp = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
            totalBytes = Math.Max(1, acts.Where(a => a.Type == ActionType.Copy).Sum(a => a.Size));
            doneBytes = 0;
            int okC = 0, errC = 0, skipC = 0, done = 0;
            int total = acts.Count;

            // jedna operacja (błąd jest logowany i liczony, przerwanie przechodzi dalej)
            Action<SyncAction> run = a =>
            {
                string dstRoot = a.ToRight ? R : L;
                string srcRoot = a.ToRight ? L : R;
                try
                {
                    int n = Interlocked.Increment(ref done);
                    switch (a.Type)
                    {
                        case ActionType.Delete:
                            Status("Usuwanie (" + n + "/" + total + "): " + a.Rel);
                            DoDelete(dstRoot, a.Rel, a.ZawszeDoArchiwum ? DeleteMode.Archive : t.DeleteMode, stamp);
                            break;
                        case ActionType.CreateDir:
                            Directory.CreateDirectory(Path.Combine(dstRoot, a.Rel));
                            break;
                        case ActionType.Copy:
                            Status("Kopiowanie (" + n + "/" + total + "): " + a.Rel);
                            // Nadpisanie niszczy poprzednia wersje bezpowrotnie - a wlasnie zly
                            // kierunek synchronizacji jest najkosztowniejsza pomylka. Odkladamy
                            // wiec stara wersje do _SyncArchive, zanim zapiszemy nowa.
                            // Przeniesienie w obrebie tego samego dysku to zmiana nazwy - tanie.
                            if (t.ZachowajPoprzednieWersje && t.DeleteMode == DeleteMode.Archive
                                && File.Exists(Path.Combine(dstRoot, a.Rel)))
                                DoDelete(dstRoot, a.Rel, DeleteMode.Archive, stamp);
                            DoCopy(Path.Combine(srcRoot, a.Rel), Path.Combine(dstRoot, a.Rel));
                            break;
                    }
                    Interlocked.Increment(ref okC);
                    if (a.Type != ActionType.CreateDir)
                        Log("  " + a.Describe() + "  " + a.Rel);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    if (ex is FileNotFoundException || ex is DirectoryNotFoundException)
                    {
                        // plik zniknął lub został podmieniony w trakcie przez inny program – to nie błąd kopiowania,
                        // następna synchronizacja dokończy
                        Interlocked.Increment(ref skipC);
                        Log("  POMINIĘTO (zmieniony/usunięty w trakcie przez inny program): " + a.Rel);
                    }
                    else
                    {
                        Interlocked.Increment(ref errC);
                        Log("  BŁĄD: " + a.Describe() + " " + a.Rel + " – " + ex.Message);
                    }
                }
            };

            // 1) usuwanie i tworzenie folderów – po kolei (kolejność ma znaczenie)
            foreach (var a in acts.Where(x => x.Type != ActionType.Copy))
            {
                if (Cancel) throw new OperationCanceledException();
                run(a);
            }

            // 2) małe pliki – kilka naraz (najwolniejsze jest otwieranie/zamykanie plików, nie przesył)
            var swTransfer = Stopwatch.StartNew();   // do podsumowania predkosci w dzienniku po kopiowaniu
            var copies = acts.Where(x => x.Type == ActionType.Copy).ToList();
            var small = copies.Where(x => x.Size < ParallelMaxSize).ToList();
            if (small.Count > 0)
            {
                try
                {
                    System.Threading.Tasks.Parallel.ForEach(small,
                        new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = ParallelFiles },
                        (a, state) =>
                        {
                            if (Cancel) { state.Stop(); return; }
                            run(a);
                        });
                }
                catch (AggregateException ae)
                {
                    if (ae.Flatten().InnerExceptions.Any(x => x is OperationCanceledException)) throw new OperationCanceledException();
                    throw ae.Flatten().InnerException;
                }
                if (Cancel) throw new OperationCanceledException();
            }

            // 3) duże pliki – po jednym (równoległe pisanie dużych plików tylko spowalnia dysk)
            foreach (var a in copies.Where(x => x.Size >= ParallelMaxSize))
            {
                if (Cancel) throw new OperationCanceledException();
                run(a);
            }

            long bajtowSkopiowano = Interlocked.Read(ref doneBytes);
            if (bajtowSkopiowano > 0)
            {
                double sek = Math.Max(0.05, swTransfer.Elapsed.TotalSeconds);
                Log("  Transfer: " + FormatBytes(bajtowSkopiowano) + " w " + swTransfer.Elapsed.ToString(@"mm\:ss\.f") +
                    "  (średnio " + FormatSpeed(bajtowSkopiowano / sek) + ")");
            }

            ok = okC; err = errC; LastSkipped = skipC;
            Progress(1000);
            speedTickPoprzednio = 0;
            Speed("");
        }

        public int LastSkipped;   // pominięte, bo zmienione w trakcie przez inny program

        const int ParallelFiles = 4;                       // ile małych plików kopiować jednocześnie
        const long ParallelMaxSize = 16L * 1024 * 1024;    // "mały" plik: poniżej 16 MB

        // pomiar predkosci - probki co ~150 ms, wspoldzielone miedzy watkami kopiujacymi male pliki rownolegle
        readonly object speedLock = new object();
        long speedBajtowPoprzednio;
        int speedTickPoprzednio;

        static string FormatSpeed(double bps)
        {
            if (bps < 0) bps = 0;
            if (bps < 1024) return bps.ToString("0") + " B/s";
            if (bps < 1024 * 1024) return (bps / 1024.0).ToString("0.0") + " KB/s";
            if (bps < 1024L * 1024 * 1024) return (bps / 1048576.0).ToString("0.0") + " MB/s";
            return (bps / 1073741824.0).ToString("0.00") + " GB/s";
        }

        static string FormatBytes(long b)
        {
            if (b < 1024) return b + " B";
            if (b < 1024 * 1024) return (b / 1024.0).ToString("0.0") + " KB";
            if (b < 1024L * 1024 * 1024) return (b / 1048576.0).ToString("0.0") + " MB";
            return (b / 1073741824.0).ToString("0.00") + " GB";
        }

        // bufor kopiowania dobierany automatycznie do rozmiaru pliku
        static int AutoBuffer(long size)
        {
            if (size <= 64 * 1024) return 64 * 1024;             // małe: 64 KB
            if (size < 1024 * 1024) return (int)size;            // do 1 MB: dokładnie rozmiar pliku
            if (size < 100L * 1024 * 1024) return 1024 * 1024;   // średnie: 1 MB
            return 4 * 1024 * 1024;                              // duże (100 MB+): 4 MB
        }

        void DoCopy(string src, string dst)
        {
            var sfi = new FileInfo(src);
            Directory.CreateDirectory(Path.GetDirectoryName(dst));
            // unikalna nazwa tymczasowa – dwa programy kopiujące ten sam plik nie wejdą sobie w drogę
            string tmp = dst + "." + Guid.NewGuid().ToString("N").Substring(0, 8) + TmpExt;
            try
            {
                int bufSize = AutoBuffer(sfi.Length);
                byte[] buf = new byte[bufSize];
                using (var input = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.SequentialScan))
                using (var output = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1))
                {
                    if (sfi.Length > 0) output.SetLength(sfi.Length);   // rezerwacja miejsca – mniej fragmentacji
                    int r;
                    while ((r = input.Read(buf, 0, buf.Length)) > 0)
                    {
                        if (Cancel) throw new OperationCanceledException();
                        output.Write(buf, 0, r);
                        Interlocked.Add(ref doneBytes, r);
                        if (Environment.TickCount - lastTick > 150)
                        {
                            lastTick = Environment.TickCount;
                            long done = Interlocked.Read(ref doneBytes);
                            Progress((int)Math.Min(1000, done * 1000 / totalBytes));
                            lock (speedLock)
                            {
                                int teraz = Environment.TickCount;
                                int dtMs = teraz - speedTickPoprzednio;
                                if (speedTickPoprzednio != 0 && dtMs > 0)
                                    Speed(FormatSpeed((done - speedBajtowPoprzednio) * 1000.0 / dtMs));
                                speedBajtowPoprzednio = done;
                                speedTickPoprzednio = teraz;
                            }
                        }
                    }
                    output.SetLength(output.Position);   // gdyby plik źródłowy zmalał w trakcie kopiowania
                }
                File.SetCreationTimeUtc(tmp, sfi.CreationTimeUtc);
                File.SetLastWriteTimeUtc(tmp, sfi.LastWriteTimeUtc);
                if (File.Exists(dst))
                {
                    File.SetAttributes(dst, FileAttributes.Normal);
                    File.Delete(dst);
                }
                File.Move(tmp, dst);
                var keep = sfi.Attributes & (FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System | FileAttributes.Archive);
                if (keep != 0) File.SetAttributes(dst, keep);
            }
            catch
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                throw;
            }
        }

        static void ClearReadOnly(DirectoryInfo di)
        {
            foreach (var f in di.GetFileSystemInfos("*", SearchOption.AllDirectories))
                if ((f.Attributes & FileAttributes.ReadOnly) != 0)
                    f.Attributes &= ~FileAttributes.ReadOnly;
        }

        internal static void DoDelete(string root, string rel, DeleteMode mode, string stamp)
        {
            string full = Path.Combine(root, rel);
            bool isDir = Directory.Exists(full);
            if (!isDir && !File.Exists(full)) return;

            switch (mode)
            {
                case DeleteMode.Archive:
                    string dest = Path.Combine(Path.Combine(Path.Combine(root, ArchiveDir), stamp), rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    if (isDir) Directory.Move(full, dest);
                    else File.Move(full, dest);
                    break;

                case DeleteMode.RecycleBin:
                    if (isDir)
                        Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(full,
                            Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                            Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                    else
                        Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(full,
                            Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                            Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                    break;

                default:
                    if (isDir)
                    {
                        var di = new DirectoryInfo(full);
                        ClearReadOnly(di);
                        di.Delete(true);
                    }
                    else
                    {
                        File.SetAttributes(full, FileAttributes.Normal);
                        File.Delete(full);
                    }
                    break;
            }
        }
    }

    // ------------------------------------------------------------------ GUI

    // panel z pionowym gradientem
    public class GradientPanel : Panel
    {
        Color c1 = Color.FromArgb(20, 120, 220), c2 = Color.FromArgb(5, 60, 160);
        public Color GradientTop { get { return c1; } set { c1 = value; Invalidate(); } }
        public Color GradientBottom { get { return c2; } set { c2 = value; Invalidate(); } }
        public GradientPanel()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
        }
        public GradientPanel(Color top, Color bottom) : this()
        {
            c1 = top; c2 = bottom;
        }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (Width <= 0 || Height <= 0) return;
            using (var br = new System.Drawing.Drawing2D.LinearGradientBrush(ClientRectangle, c1, c2, 90f))
                e.Graphics.FillRectangle(br, ClientRectangle);
        }
    }

    // pasek postepu z delikatnym "shimmerem" - przesuwajacym sie pasmem jasniejszego koloru,
    // ktore sygnalizuje aktywnosc nawet przy bardzo wolno rosnacym postepie (np. dysk sieciowy)
    public class ShimmerProgressBar : Control
    {
        int val, max = 1000;
        float displayVal;      // wartosc faktycznie rysowana - dogania "val" stopniowo, nie skokiem
        float shimmerX = -80f;
        bool animate;
        readonly System.Windows.Forms.Timer timer;

        public int Maximum { get { return max; } set { max = Math.Max(1, value); Invalidate(); } }
        public int Value { get { return val; } set { val = Math.Max(0, Math.Min(max, value)); EnsureTimer(); } }
        public bool Animate
        {
            get { return animate; }
            set { animate = value; if (value) shimmerX = -80f; EnsureTimer(); }
        }

        public ShimmerProgressBar()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            timer = new System.Windows.Forms.Timer { Interval = 20 };
            timer.Tick += delegate
            {
                bool changed = false;
                float diff = val - displayVal;
                if (Math.Abs(diff) > 0.5f) { displayVal += diff * 0.25f; changed = true; }
                else if (displayVal != val) { displayVal = val; changed = true; }
                if (animate) { shimmerX += 16f; if (shimmerX > Width + 80) shimmerX = -80f; changed = true; }
                if (changed) Invalidate();
                if (!animate && Math.Abs(val - displayVal) < 0.5f) timer.Stop();
            };
        }

        void EnsureTimer() { if (!timer.Enabled) timer.Start(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var track = ClientRectangle;
            using (var b = new SolidBrush(Color.FromArgb(224, 227, 232))) g.FillRectangle(b, track);
            int w = (int)((long)Math.Round(displayVal) * track.Width / max);
            if (w > 0)
            {
                var fillRect = new Rectangle(0, 0, w, track.Height);
                using (var br = new System.Drawing.Drawing2D.LinearGradientBrush(fillRect, Color.FromArgb(35, 125, 235), Color.FromArgb(10, 90, 200), 0f))
                    g.FillRectangle(br, fillRect);
                if (animate)
                {
                    var stary = g.Clip;
                    g.SetClip(fillRect);
                    using (var blend = new System.Drawing.Drawing2D.LinearGradientBrush(
                        new RectangleF(shimmerX, 0, 70, track.Height), Color.Transparent, Color.Transparent, 0f))
                    {
                        blend.InterpolationColors = new System.Drawing.Drawing2D.ColorBlend
                        {
                            Colors = new[] { Color.FromArgb(0, 255, 255, 255), Color.FromArgb(110, 255, 255, 255), Color.FromArgb(0, 255, 255, 255) },
                            Positions = new[] { 0f, 0.5f, 1f }
                        };
                        g.FillRectangle(blend, shimmerX, 0, 70, track.Height);
                    }
                    g.Clip = stary;
                }
            }
            using (var pen = new Pen(Color.FromArgb(190, 195, 200))) g.DrawRectangle(pen, 0, 0, track.Width - 1, track.Height - 1);
        }
    }

    // duża klikalna strzałka kierunku: 0 = →, 1 = ←, 2 = ⇄
    public class DirectionArrow : Control
    {
        int mode;
        bool hover;
        public int Mode { get { return mode; } set { mode = value; Invalidate(); } }

        public DirectionArrow()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold | FontStyle.Underline);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            float w = Width - 8, h = Height - 6, x0 = 4, y0 = 3;
            if (w < 20 || h < 10) return;
            float head = Math.Min(h * 0.9f, w * 0.28f);
            float bodyTop = y0 + h * 0.25f, bodyBot = y0 + h * 0.75f, midY = y0 + h / 2;
            bool leftHead = mode == 1 || mode == 2, rightHead = mode == 0 || mode == 2;

            var pts = new List<PointF>();
            float lx = x0, rx = x0 + w;
            if (leftHead)
            {
                pts.Add(new PointF(lx, midY));
                pts.Add(new PointF(lx + head, y0));
                pts.Add(new PointF(lx + head, bodyTop));
            }
            else { pts.Add(new PointF(lx, bodyTop)); }
            if (rightHead)
            {
                pts.Add(new PointF(rx - head, bodyTop));
                pts.Add(new PointF(rx - head, y0));
                pts.Add(new PointF(rx, midY));
                pts.Add(new PointF(rx - head, y0 + h));
                pts.Add(new PointF(rx - head, bodyBot));
            }
            else { pts.Add(new PointF(rx, bodyTop)); pts.Add(new PointF(rx, bodyBot)); }
            if (leftHead)
            {
                pts.Add(new PointF(lx + head, bodyBot));
                pts.Add(new PointF(lx + head, y0 + h));
            }
            else { pts.Add(new PointF(lx, bodyBot)); }

            var rect = new RectangleF(x0, y0, w, h);
            Color top = hover ? Color.FromArgb(255, 255, 255) : Color.FromArgb(240, 248, 255);
            Color bottom = hover ? Color.FromArgb(120, 200, 255) : Color.FromArgb(40, 140, 230);
            using (var br = new System.Drawing.Drawing2D.LinearGradientBrush(rect, top, bottom, 90f))
                g.FillPolygon(br, pts.ToArray());
            using (var pen = new Pen(Color.FromArgb(0, 70, 160), 2f))
                g.DrawPolygon(pen, pts.ToArray());

            TextRenderer.DrawText(g, "Zmień", Font, Rectangle.Round(new RectangleF(x0 + head, bodyTop, w - 2 * head, bodyBot - bodyTop)),
                Enabled ? Color.FromArgb(0, 40, 120) : Color.Gray, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }
}
