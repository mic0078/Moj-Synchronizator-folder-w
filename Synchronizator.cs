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
        readonly Color c1, c2;
        public GradientPanel(Color top, Color bottom)
        {
            c1 = top; c2 = bottom;
            DoubleBuffered = true;
            ResizeRedraw = true;
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

    public class MainForm : Form
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

            Text = "Echo Sync";
            // okno od razu tak duże, żeby wszystko było widać (ale nie większe niż ekran)
            var wa = Screen.PrimaryScreen.WorkingArea;
            Size = new Size(Math.Min(1380, wa.Width), Math.Min(880, wa.Height));
            MinimumSize = new Size(900, 640);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9f);
            var ico = AppIcon();
            Icon = ico;

            BuildUi();
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

        void BuildUi()
        {
            var split = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1 };
            Controls.Add(split);

            // nagłówek
            var header = new GradientPanel(Color.FromArgb(20, 120, 220), Color.FromArgb(5, 60, 160)) { Dock = DockStyle.Top, Height = 60 };
            headerPanel = header;
            header.Paint += (s, e) =>
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
            Controls.Add(header);

            // Pasek decyzji: powiadomienie znika po kilku sekundach, a sprawa zostaje.
            // Ten pasek siedzi pod nagłówkiem tak długo, aż człowiek zdecyduje.
            panelDecyzji = new Panel { Dock = DockStyle.Top, Height = 0, BackColor = Color.FromArgb(255, 244, 205), Visible = false };
            lblDecyzji = new Label { Dock = DockStyle.Fill, ForeColor = Color.FromArgb(120, 70, 0),
                Font = new Font(Font.FontFamily, 9.5f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(14, 0, 0, 0) };
            btnDecyduj = new Button { Text = "Zdecyduj teraz", Dock = DockStyle.Right, Width = 160,
                BackColor = Color.FromArgb(255, 210, 120), FlatStyle = FlatStyle.Flat,
                Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0) };
            btnDecyduj.Click += delegate { OtworzDecyzje(); };
            panelDecyzji.Controls.Add(lblDecyzji);
            panelDecyzji.Controls.Add(btnDecyduj);
            Controls.Add(panelDecyzji);
            // Pasek ma ZEPCHNAC zawartosc w dol, a nie polozyc sie na niej. Dokowanie idzie od
            // konca kolekcji, wiec kolejnosc musi byc: [0] wypelnienie, [1] pasek, [2] naglowek.
            Controls.SetChildIndex(header, Controls.Count - 1);
            Load += delegate
            {
                try { if (split.Width > 600) split.SplitterDistance = 260; } catch { }
            };

            // lewy panel: lista zadań
            lstTasks = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, Font = new Font("Segoe UI", 10f),
                DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 22 };
            lstTasks.DrawItem += LstTasks_DrawItem;
            lstTasks.SelectedIndexChanged += delegate { TaskSelectionChanged(); };
            var taskBtns = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 180, Padding = new Padding(2) };
            taskBtns.Controls.Add(MkButton("Nowe", 70, delegate { AddTask(new SyncTask { SubFolder = true }); }));
            taskBtns.Controls.Add(MkButton("Duplikuj", 70, delegate { DuplicateTask(); }));
            taskBtns.Controls.Add(MkButton("Usuń", 70, delegate { DeleteTask(); }));
            taskBtns.Controls.Add(MkButton("▲", 34, delegate { MoveTask(-1); }));
            taskBtns.Controls.Add(MkButton("▼", 34, delegate { MoveTask(1); }));
            taskBtns.SetFlowBreak(taskBtns.Controls[taskBtns.Controls.Count - 1], true);
            btnDeleteFiles = MkButton("🗑 Usuń wybrane pliki", 226, delegate { DeleteSelectedFiles(); });
            btnDeleteFiles.Enabled = false;
            taskBtns.Controls.Add(btnDeleteFiles);
            new ToolTip().SetToolTip(btnDeleteFiles, "Usuwa pliki zaznaczone na liście „Podgląd zmian” (Ctrl/Shift + klik = wiele, Delete = usuń).\nDziała zgodnie z ustawieniem „Usuwane pliki” zadania.");

            // Drugie, zawsze widoczne wejście do decyzji o usunięciach - tuż przy liście zadań.
            btnDecyzjeLista = MkButton("❗ Decyzje do podjęcia", 226, delegate { OtworzDecyzje(); });
            btnDecyzjeLista.BackColor = Color.FromArgb(255, 210, 120);
            btnDecyzjeLista.Font = new Font(btnDecyzjeLista.Font, FontStyle.Bold);
            btnDecyzjeLista.Visible = false;
            taskBtns.SetFlowBreak(btnDeleteFiles, true);
            taskBtns.Controls.Add(btnDecyzjeLista);
            new ToolTip().SetToolTip(btnDecyzjeLista, "Po drugiej stronie coś skasowano. Kliknij, żeby zdecydować: przywrócić czy usunąć też u siebie.");

            var btnArchiwum = MkButton("🧹 Archiwum kopii", 226, delegate { OtworzArchiwum(); });
            taskBtns.SetFlowBreak(btnDecyzjeLista, true);
            taskBtns.Controls.Add(btnArchiwum);
            new ToolTip().SetToolTip(btnArchiwum, "Pokazuje, co leży w _SyncArchive i ile zajmuje.\nStąd czyścisz je ręcznie albo ustawiasz, po ilu dniach stare kopie mają znikać same.");
            var lblTasks = new Label { Text = "Zadania", Dock = DockStyle.Top, Height = 24, Font = new Font("Segoe UI", 10f, FontStyle.Bold), Padding = new Padding(3, 4, 0, 0) };
            var searchTasksPanel = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 27, Padding = new Padding(3, 0, 3, 0) };
            var lblSearchTasksIcon = new Label { Text = "🔎", AutoSize = true, Margin = new Padding(0, 4, 2, 0) };
            txtSearchTasks = new TextBox { Width = 150, Margin = new Padding(0, 2, 0, 0) };
            searchTasksPanel.Controls.Add(lblSearchTasksIcon);
            searchTasksPanel.Controls.Add(txtSearchTasks);
            new ToolTip().SetToolTip(txtSearchTasks, "Szukaj zadania po nazwie. Enter = następne dopasowanie.");
            txtSearchTasks.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.Handled = true; e.SuppressKeyPress = true; FindNextTask(); } };
            txtSearchTasks.TextChanged += delegate
            {
                taskSearchDirty = true;
                // filtrowanie na żywo: przefiltruj listę zachowując bieżące zaznaczenie
                var sel = lstTasks.SelectedItem as SyncTask;
                RefreshTaskList(sel != null ? Math.Max(0, cfg.Tasks.IndexOf(sel)) : 0);
            };
            split.Panel1.Controls.Add(lstTasks);
            split.Panel1.Controls.Add(taskBtns);
            split.Panel1.Controls.Add(searchTasksPanel);
            split.Panel1.Controls.Add(lblTasks);

            // prawy panel
            var right = split.Panel2;
            right.Padding = new Padding(6, 0, 6, 6);

            // zakładki: podgląd + log  (Fill – dodawane jako pierwsze)
            tabs = new TabControl { Dock = DockStyle.Fill };
            var tabPrev = new TabPage("Podgląd zmian");
            var tabLog = new TabPage("Dziennik");
            lvActions = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true };
            lvActions.Columns.Add("Akcja", 120);
            lvActions.Columns.Add("Ścieżka", 400);
            lvActions.Columns.Add("Rozmiar", 85, HorizontalAlignment.Right);
            lvActions.Columns.Add("Data w źródle", 125);
            lvActions.Columns.Add("Data w celu", 125);
            lvActions.Columns.Add("Powód", 190);
            lvActions.ColumnClick += (s, e) => SortActions(e.Column);
            var lvMenu = new ContextMenuStrip();
            lvMenu.Items.Add("Rozwiń wszystkie foldery", null, delegate { ExpandAllFolders(); });
            lvMenu.Items.Add("Zwiń wszystkie foldery", null, delegate { CollapseAllFolders(); });
            lvActions.ContextMenuStrip = lvMenu;
            var searchPanel = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 30, Padding = new Padding(3, 3, 3, 0) };
            var lblSearchIcon = new Label { Text = "🔎", AutoSize = true, Margin = new Padding(2, 5, 2, 0) };
            txtSearchTree = new TextBox { Width = 220, Margin = new Padding(0, 3, 4, 0) };
            var btnSearchTree = MkButton("Znajdź", 90, delegate { FindNextInTree(); });
            lblSearchInfo = new Label { AutoSize = true, Margin = new Padding(6, 7, 0, 0), ForeColor = Color.DimGray };
            searchPanel.Controls.Add(lblSearchIcon);
            searchPanel.Controls.Add(txtSearchTree);
            searchPanel.Controls.Add(btnSearchTree);
            searchPanel.Controls.Add(lblSearchInfo);
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
            lblSummary = new Label { Dock = DockStyle.Bottom, Height = 22, TextAlign = ContentAlignment.MiddleLeft };
            chkShowSame = new CheckBox { Text = "Pokaż pliki bez zmian", Checked = true, Dock = DockStyle.Right, Width = 170 };
            chkShowSame.CheckedChanged += delegate { RerenderActions(); };
            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 24 };
            lblSummary.Dock = DockStyle.Fill;
            bottom.Controls.Add(lblSummary);
            bottom.Controls.Add(chkShowSame);
            lblSelection = new Label { Dock = DockStyle.Right, Width = 270, TextAlign = ContentAlignment.MiddleRight, ForeColor = Color.DarkBlue, Font = new Font("Segoe UI", 9f, FontStyle.Bold) };
            bottom.Controls.Add(lblSelection);
            tabPrev.Controls.Add(lvActions);
            tabPrev.Controls.Add(bottom);
            tabPrev.Controls.Add(searchPanel);

            txtLog = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, WordWrap = false, DetectUrls = false,
                ScrollBars = RichTextBoxScrollBars.Both, Font = new Font("Consolas", 9f), BackColor = Color.White };
            tabLog.Controls.Add(txtLog);
            tabs.TabPages.Add(tabPrev);
            tabs.TabPages.Add(tabLog);
            right.Controls.Add(tabs);

            // pasek postępu i status
            var progPanel = new Panel { Dock = DockStyle.Top, Height = 46 };
            progress = new ShimmerProgressBar { Dock = DockStyle.Top, Height = 18, Maximum = 1000 };
            var statusRow = new Panel { Dock = DockStyle.Top, Height = 22 };
            lblStatus = new Label { Dock = DockStyle.Fill, Text = "Gotowy.", AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
            lblSpeed = new Label { Dock = DockStyle.Right, Width = 110, TextAlign = ContentAlignment.MiddleRight,
                ForeColor = Color.FromArgb(0, 90, 180), Font = new Font(Font, FontStyle.Bold) };
            statusRow.Controls.Add(lblStatus);
            statusRow.Controls.Add(lblSpeed);
            progPanel.Controls.Add(statusRow);
            progPanel.Controls.Add(progress);
            right.Controls.Add(progPanel);

            // przyciski uruchamiania
            var runBtns = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 76, Padding = new Padding(0, 6, 0, 0), AutoScroll = true };
            btnPreview = MkButton("🔍 Analizuj", 110, delegate { StartSelected(false); });
            new ToolTip().SetToolTip(btnPreview, "Czysta analiza – skanuje oba foldery i pokazuje, co zostanie skopiowane lub usunięte.\nNiczego nie zmienia na dysku.");
            btnSync = MkButton("▶ Synchronizuj", 130, delegate { StartSelected(true); });
            btnSync.Font = new Font(Font, FontStyle.Bold);
            btnRunAll = MkButton("▶▶ Uruchom wszystkie włączone", 210, delegate { StartWork(cfg.Tasks.Where(x => x.Enabled && !x.Unsaved).ToList(), true, true, false); });
            btnCancel = MkButton("■ Przerwij", 100, delegate { engine.Cancel = true; SetStatus("Przerywanie..."); });
            btnCancel.Enabled = false;
            chkAutomation = new CheckBox { Text = "Automatyka włączona", AutoSize = true, Checked = cfg.AutomationEnabled, Margin = new Padding(16, 7, 3, 3) };
            chkAutomation.CheckedChanged += delegate
            {
                cfg.AutomationEnabled = chkAutomation.Checked;
                if (miPause != null) miPause.Text = cfg.AutomationEnabled ? "Wstrzymaj automatykę" : "Wznów automatykę";
                SaveConfig();
                watchersDirty = true;
                lastEdit = DateTime.MinValue;
                AppendLog("Automatyka " + (cfg.AutomationEnabled ? "włączona" : "wstrzymana"));
            };
            chkAutostart = new CheckBox { Text = "Uruchamiaj z Windows (w tray)", AutoSize = true, Checked = AutostartEnabled(), Margin = new Padding(8, 7, 3, 3) };
            chkAutostart.CheckedChanged += delegate
            {
                try { SetAutostart(chkAutostart.Checked); }
                catch (Exception ex) { MessageBox.Show("Nie udało się zmienić autostartu: " + ex.Message); }
            };
            var btnRobo = MkButton("📦 Kopiuj / Przenieś", 150, delegate { OpenRobo(); });
            new ToolTip().SetToolTip(btnRobo, "Jednorazowe kopiowanie lub przenoszenie folderów (robocopy)");
            runBtns.Controls.AddRange(new Control[] { btnPreview, btnSync, btnRunAll, btnCancel, btnRobo, chkAutomation, chkAutostart });
            right.Controls.Add(runBtns);

            // edytor zadania
            grpEdit = new GroupBox { Text = "Ustawienia zadania", Dock = DockStyle.Top, Height = 418, Padding = new Padding(6) };
            var tl = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 7, Padding = new Padding(0, 6, 0, 0) };
            tl.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            tl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            tl.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40));
            tl.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 125));

            txtName = new TextBox { Dock = DockStyle.Fill };
            chkEnabled = new CheckBox { Text = "Włączone", Dock = DockStyle.Fill };
            AddRow(tl, 0, "Nazwa:", txtName, null, chkEnabled);

            // ---- pasek folderów: LEWY  [strzałka kierunku]  PRAWY
            txtLeft = new TextBox { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 10f) };
            txtRight = new TextBox { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 10f) };
            cmbDir = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Visible = false };
            cmbDir.Items.AddRange(new object[] { "Lewy → Prawy", "Prawy → Lewy", "Dwukierunkowo" });

            var folderBar = new GradientPanel(Color.FromArgb(150, 210, 250), Color.FromArgb(90, 170, 235)) { Dock = DockStyle.Top, Height = 118 };
            var fb = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, BackColor = Color.Transparent, Padding = new Padding(4) };
            fb.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            fb.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210));
            fb.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            fb.Controls.Add(MkFolderSide("Folder LEWY", txtLeft, out lblLewyTytul), 0, 0);

            arrow = new DirectionArrow { Dock = DockStyle.Fill };
            arrow.Click += delegate { if (cmbDir.SelectedIndex >= 0) cmbDir.SelectedIndex = (cmbDir.SelectedIndex + 1) % 3; };
            new ToolTip().SetToolTip(arrow, "Kliknij, aby zmienić kierunek synchronizacji");
            lblDirMode = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 9f, FontStyle.Bold), ForeColor = Color.FromArgb(0, 50, 120), BackColor = Color.Transparent };
            var lnkSwap = new LinkLabel { Text = "Zamień foldery miejscami", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, BackColor = Color.Transparent, LinkColor = Color.FromArgb(0, 50, 120) };
            lnkSwap.Click += delegate { string s = txtLeft.Text; txtLeft.Text = txtRight.Text; txtRight.Text = s; };
            var center = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, BackColor = Color.Transparent, Margin = new Padding(0) };
            center.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            center.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
            center.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
            center.Controls.Add(arrow, 0, 0);
            center.Controls.Add(lblDirMode, 0, 1);
            center.Controls.Add(lnkSwap, 0, 2);
            center.Controls.Add(cmbDir);
            fb.Controls.Add(center, 1, 0);

            fb.Controls.Add(MkFolderSide("Folder PRAWY", txtRight, out lblPrawyTytul), 2, 0);
            folderBar.Controls.Add(fb);

            lblStatusBar = new Label
            {
                Dock = DockStyle.Top, Height = 24, TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.FromArgb(10, 90, 190), ForeColor = Color.White, Font = new Font("Segoe UI", 9f, FontStyle.Bold)
            };

            chkSubFolder = new CheckBox { Text = "Twórz podfolder źródła w celu", AutoSize = true, Margin = new Padding(0, 6, 0, 0) };
            lblTarget = new Label { AutoSize = true, ForeColor = Color.DarkBlue, Margin = new Padding(16, 7, 0, 0) };
            var subPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = new Padding(0), WrapContents = false };
            subPanel.Controls.AddRange(new Control[] { chkSubFolder, lblTarget });
            new ToolTip().SetToolTip(chkSubFolder, "Tworzy w folderze docelowym podfolder o nazwie folderu źródłowego.");
            // wykres CPU pod polem "Włączone" - kolumny 2-3 wierszy 1-2 (Podfolder + Tryb)
            var cpuGraph = new CpuGraph { Dock = DockStyle.Fill, Margin = new Padding(8, 2, 2, 2) };
            AddRow(tl, 1, "Podfolder:", subPanel, cpuGraph, null);
            tl.SetColumnSpan(cpuGraph, 2);
            tl.SetRowSpan(cpuGraph, 2);

            chkMirror = new CheckBox { Text = "Kopia lustrzana (usuwa z celu elementy nieobecne w źródle)", Dock = DockStyle.Fill, AutoSize = false };
            new ToolTip().SetToolTip(chkMirror, "Cel staje się dokładną kopią źródła: usuwa elementy nieobecne w źródle i może nadpisać nowsze pliki w celu.");
            // wiersz 2 bez rozciągania na kolumny 2-3 - tam kończy się wykres CPU (rowspan z wiersza 1)
            tl.Controls.Add(new Label { Text = "Tryb:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 2);
            tl.Controls.Add(chkMirror, 1, 2);

            cmbDelete = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 190 };
            cmbDelete.Items.AddRange(new object[] {
                "Archiwum _SyncArchive",
                "Kosz Windows",
                "Usuń trwale" });
            chkWersje = new CheckBox { Text = "Zachowuj poprzednie wersje nadpisanych plików", AutoSize = true, Margin = new Padding(12, 6, 0, 0) };
            new ToolTip().SetToolTip(chkWersje, "Zanim plik zostanie nadpisany, jego poprzednia wersja trafia do _SyncArchive.\n" +
                "Dzięki temu da się cofnąć skutki pomyłki (np. zły kierunek synchronizacji).\n" +
                "Działa tylko przy usuwaniu do _SyncArchive. Kosztem jest miejsce na dysku.");
            var usuwaniePanel = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = new Padding(0), WrapContents = false };
            cmbDelete.Margin = new Padding(0, 2, 0, 0);
            usuwaniePanel.Controls.AddRange(new Control[] { cmbDelete, chkWersje });
            new ToolTip().SetToolTip(cmbDelete, "Archiwum działa także na dyskach sieciowych. Kosz Windows może tam usuwać trwale.");
            AddRow(tl, 3, "Usuwane pliki:", usuwaniePanel, null, null);

            txtExcl = new TextBox { Dock = DockStyle.Fill };
            AddRow(tl, 4, "Wykluczenia:", txtExcl, null, new Label { Text = "np. *.tmp;cache\\*", Dock = DockStyle.Fill, ForeColor = Color.Gray, TextAlign = ContentAlignment.MiddleLeft });
            chkInterval = new CheckBox { Text = "Co", AutoSize = true, Margin = new Padding(0, 6, 0, 0) };
            numInterval = new NumericUpDown { Minimum = 1, Maximum = 10080, Value = 15, Width = 70, Margin = new Padding(2, 3, 2, 0) };
            chkWatch = new CheckBox { Text = "Po wykryciu zmian w źródle, po ciszy", AutoSize = true, Margin = new Padding(24, 6, 0, 0) };
            cmbWatchDelay = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100, Margin = new Padding(6, 3, 0, 0) };
            cmbWatchDelay.Items.AddRange(NAZWY_CISZY);
            cmbWatchDelay.SelectedIndex = 0;
            var autoPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = new Padding(0), WrapContents = false };
            autoPanel.Controls.AddRange(new Control[] { chkInterval, numInterval,
                new Label { Text = "minut", AutoSize = true, Margin = new Padding(0, 7, 0, 0) }, chkWatch,
                new Label { Text = "przez", AutoSize = true, Margin = new Padding(6, 7, 0, 0) }, cmbWatchDelay });
            new ToolTip().SetToolTip(chkWatch, "Synchronizuje po wykryciu zmian, gdy przez wybrany czas nie nadejdzie kolejna zmiana.");
            AddRow(tl, 5, "Automatycznie:", autoPanel, null, null);

            btnSave = MkButton("💾 Zapisz", 100, delegate { SaveEditor(); });
            btnSave.Font = new Font(Font, FontStyle.Bold);
            btnDiscard = MkButton("Odrzuć zmiany", 110, delegate { DiscardEditor(); });
            lblInfo = new Label { AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(14, 8, 0, 0) };
            linkStalaDecyzja = new LinkLabel { AutoSize = true, Margin = new Padding(14, 8, 0, 0), Visible = false };
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
            var savePanel = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = new Padding(0), WrapContents = false };
            // link przed lblInfo: wiersz nie zawija, więc przy wąskim oknie ucina się KONIEC -
            // niech to będzie tekst informacyjny, a nie klikalna decyzja
            savePanel.Controls.AddRange(new Control[] { btnSave, btnDiscard, linkStalaDecyzja, lblInfo });
            // wiersz 6 bez rozciągania na kolumny 2-3 - tam siedzi wykres CPU (rowspan z wiersza 5)
            tl.Controls.Add(new Label { Text = "", Dock = DockStyle.Fill }, 0, 6);
            tl.Controls.Add(savePanel, 1, 6);
            for (int i = 0; i < 6; i++) tl.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            tl.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            grpEdit.Controls.Add(tl);
            grpEdit.Controls.Add(lblStatusBar);
            grpEdit.Controls.Add(folderBar);
            right.Controls.Add(grpEdit);

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