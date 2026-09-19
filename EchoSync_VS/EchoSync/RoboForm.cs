// Okno "Kopiuj / Przenieś" – jednorazowe kopiowanie lub przenoszenie folderów przez wbudowany w Windows robocopy
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace MojSync
{
    public class RoboForm : Form
    {
        TextBox txtSrc, txtDst, txtExcl, txtLog;
        RadioButton rbCopy, rbMove;
        CheckBox chkSub, chkXO, chkZ, chkB, chkEmpty;
        NumericUpDown numThreads, numRetry;
        Label lblCmd, lblStatus, lblTarget;
        ProgressBar bar;
        Button btnStart, btnCancel;
        Process proc;
        volatile bool cancelled;
        readonly Action<string> mainLog;

        static readonly bool IsAdmin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

        public RoboForm(Icon icon, Action<string> mainLog)
        {
            this.mainLog = mainLog ?? delegate { };
            Text = "Kopiuj / Przenieś – Echo Sync";
            Icon = icon;
            Font = new Font("Segoe UI", 9f);
            StartPosition = FormStartPosition.CenterParent;
            var wa = Screen.PrimaryScreen.WorkingArea;
            Size = new Size(Math.Min(980, wa.Width), Math.Min(760, wa.Height));
            MinimumSize = new Size(760, 600);

            var tl = new TableLayoutPanel { Dock = DockStyle.Top, Height = 330, ColumnCount = 3, Padding = new Padding(10, 10, 10, 0) };
            tl.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            tl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            tl.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            for (int i = 0; i < 9; i++) tl.RowStyles.Add(new RowStyle(SizeType.Absolute, i == 8 ? 42 : 34));

            txtSrc = new TextBox { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 10f) };
            txtDst = new TextBox { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 10f) };
            Row(tl, 0, "Źródło:", txtSrc, Browse(txtSrc));
            Row(tl, 1, "Cel:", txtDst, Browse(txtDst));

            chkSub = new CheckBox { Text = "Utwórz podfolder źródła w celu", Checked = true, AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
            lblTarget = new Label { AutoSize = true, ForeColor = Color.DarkBlue, Margin = new Padding(14, 9, 0, 0) };
            Row(tl, 2, "", Flow(chkSub, lblTarget), null);
            new ToolTip().SetToolTip(chkSub, "Tworzy w celu podfolder o nazwie folderu źródłowego.");

            rbCopy = new RadioButton { Text = "Kopiuj (źródło zostaje bez zmian)", Checked = true, AutoSize = true, Margin = new Padding(0, 8, 20, 0) };
            rbMove = new RadioButton { Text = "Przenieś (usuń pliki ze źródła po skopiowaniu)", AutoSize = true, Margin = new Padding(0, 8, 0, 0), ForeColor = Color.DarkRed };
            Row(tl, 3, "Operacja:", Flow(rbCopy, rbMove), null);

            chkXO = new CheckBox { Text = "Nie nadpisuj nowszych plików w celu", Checked = true, AutoSize = true, Margin = new Padding(0, 8, 20, 0) };
            chkEmpty = new CheckBox { Text = "Kopiuj też puste foldery", Checked = true, AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
            Row(tl, 4, "Opcje:", Flow(chkXO, chkEmpty), null);

            chkZ = new CheckBox { Text = "Wznawiaj przerwane pliki", AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
            new ToolTip().SetToolTip(chkZ, "Tryb wznowienia jest przydatny dla dużych plików i połączeń sieciowych, ale może spowolnić małe pliki.");
            Row(tl, 5, "", chkZ, null);

            chkB = new CheckBox
            {
                Text = IsAdmin ? "Tryb kopii zapasowej"
                               : "Tryb kopii zapasowej (wymaga administratora)",
                AutoSize = true, Enabled = IsAdmin, Margin = new Padding(0, 8, 0, 0)
            };
            new ToolTip().SetToolTip(chkB, "Kopiuje także pliki zablokowane i bez uprawnień.");
            Row(tl, 6, "", chkB, null);

            numThreads = new NumericUpDown { Minimum = 1, Maximum = 32, Value = 8, Width = 55, Margin = new Padding(0, 5, 4, 0) };
            numRetry = new NumericUpDown { Minimum = 0, Maximum = 20, Value = 2, Width = 55, Margin = new Padding(0, 5, 4, 0) };
            txtExcl = new TextBox { Width = 160, Text = "Thumbs.db desktop.ini", Margin = new Padding(0, 5, 0, 0) };
            Row(tl, 7, "Wydajność:", Flow(
                new Label { Text = "Wątki:", AutoSize = true, Margin = new Padding(0, 8, 4, 0) }, numThreads,
                new Label { Text = "Ponowienia:", AutoSize = true, Margin = new Padding(16, 8, 4, 0) }, numRetry,
                new Label { Text = "Wyklucz:", AutoSize = true, Margin = new Padding(16, 8, 4, 0) }, txtExcl), null);

            btnStart = new Button { Text = "▶ Start", Width = 110, Height = 32, Font = new Font(Font, FontStyle.Bold) };
            btnCancel = new Button { Text = "■ Przerwij", Width = 100, Height = 32, Enabled = false };
            var btnCmd = new Button { Text = "Kopiuj polecenie", Width = 120, Height = 32 };
            btnStart.Click += delegate { Start(); };
            btnCancel.Click += delegate { Cancel(); };
            btnCmd.Click += delegate
            {
                try { Clipboard.SetText("robocopy " + BuildArgs()); lblStatus.Text = "Polecenie skopiowane do schowka."; }
                catch (Exception ex) { lblStatus.Text = "Nie można skopiować polecenia: " + ex.Message; }
            };
            Row(tl, 8, "", Flow(btnStart, btnCancel, btnCmd), null);

            lblCmd = new Label { Dock = DockStyle.Top, Height = 38, ForeColor = Color.DimGray, Font = new Font("Consolas", 8.5f), Padding = new Padding(12, 4, 12, 0) };
            bar = new ProgressBar { Dock = DockStyle.Top, Height = 18, Maximum = 1000 };
            lblStatus = new Label { Dock = DockStyle.Top, Height = 24, Text = "Wybierz źródło i cel, potem kliknij Start.", Padding = new Padding(10, 4, 0, 0), AutoEllipsis = true };
            txtLog = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Font = new Font("Consolas", 9f), BackColor = Color.White };

            var logPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 4, 10, 10) };
            logPanel.Controls.Add(txtLog);
            Controls.Add(logPanel);
            Controls.Add(lblStatus);
            Controls.Add(bar);
            Controls.Add(lblCmd);
            Controls.Add(tl);

            EventHandler upd = delegate { UpdatePreview(); };
            foreach (Control c in new Control[] { txtSrc, txtDst, txtExcl }) c.TextChanged += upd;
            foreach (var c in new[] { chkSub, chkXO, chkZ, chkB, chkEmpty }) c.CheckedChanged += upd;
            rbCopy.CheckedChanged += upd;
            numThreads.ValueChanged += upd;
            numRetry.ValueChanged += upd;
            FormClosing += (s, e) =>
            {
                if (proc != null && !proc.HasExited)
                {
                    if (MessageBox.Show(this, "Operacja trwa. Przerwać i zamknąć okno?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) { e.Cancel = true; return; }
                    Cancel();
                }
            };
            UpdatePreview();
        }

        // ---------- układ

        static void Row(TableLayoutPanel tl, int row, string label, Control main, Control right)
        {
            tl.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
            tl.Controls.Add(main, 1, row);
            if (right != null) tl.Controls.Add(right, 2, row);
            else tl.SetColumnSpan(main, 2);
        }

        static FlowLayoutPanel Flow(params Control[] cs)
        {
            var f = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0) };
            f.Controls.AddRange(cs);
            return f;
        }

        Button Browse(TextBox target)
        {
            var b = new Button { Text = "Wybierz...", Dock = DockStyle.Fill, Margin = new Padding(4, 3, 0, 3) };
            b.Click += delegate
            {
                using (var d = new FolderBrowserDialog { ShowNewFolderButton = true })
                {
                    try { if (Directory.Exists(target.Text)) d.SelectedPath = target.Text; } catch { }
                    if (d.ShowDialog(this) == DialogResult.OK) target.Text = d.SelectedPath;
                }
            };
            return b;
        }

        // ---------- polecenie robocopy

        string SrcPath() { return Engine.Norm(txtSrc.Text); }

        string DstPath()
        {
            string dst = Engine.Norm(txtDst.Text);
            if (chkSub.Checked && dst.Length > 0)
            {
                string name = SyncTask.SourceName(txtSrc.Text);
                if (name.Length > 0 && !Path.GetFileName(dst).Equals(name, StringComparison.OrdinalIgnoreCase))
                    dst = Path.Combine(dst, name);
            }
            return dst;
        }

        static string Q(string p)
        {
            // robocopy źle traktuje "C:\" – końcowy backslash przed cudzysłowem
            if (p.EndsWith("\\")) p += ".";
            return "\"" + p + "\"";
        }

        string BuildArgs()
        {
            var sb = new StringBuilder();
            sb.Append(Q(SrcPath())).Append(' ').Append(Q(DstPath()));
            sb.Append(chkEmpty.Checked ? " /E" : " /S");
            if (rbMove.Checked) sb.Append(" /MOVE");
            if (chkXO.Checked) sb.Append(" /XO");
            if (chkB.Checked && chkB.Enabled) sb.Append(chkZ.Checked ? " /ZB" : " /B");
            else if (chkZ.Checked) sb.Append(" /Z");
            sb.Append(" /MT:").Append((int)numThreads.Value);
            sb.Append(" /R:").Append((int)numRetry.Value).Append(" /W:5");
            sb.Append(" /COPY:DAT /DCOPY:DAT /FFT /XJ");           // daty, atrybuty; tolerancja czasu NAS; bez pętli junction
            var excl = (txtExcl.Text ?? "").Split(new[] { ' ', ';' }, StringSplitOptions.RemoveEmptyEntries);
            if (excl.Length > 0) sb.Append(" /XF ").Append(string.Join(" ", excl.Select(x => "\"" + x + "\"")));
            sb.Append(" /XD \"").Append(Engine.ArchiveDir).Append("\" \"").Append(Engine.PendingDir)
              .Append("\" \"$RECYCLE.BIN\" \"System Volume Information\"");
            sb.Append(" /BYTES /NP /FP /NDL /NJH");                // wyjście do odczytu postępu
            return sb.ToString();
        }

        void UpdatePreview()
        {
            lblTarget.Text = txtDst.Text.Trim().Length > 0 ? "Pliki trafią do:  " + DstPath() : "";
            lblCmd.Text = txtSrc.Text.Trim().Length > 0 && txtDst.Text.Trim().Length > 0 ? "robocopy " + BuildArgs() : "";
            chkZ.ForeColor = SystemColors.ControlText;
        }

        // robocopy wypisuje nazwy akcji i podsumowanie po angielsku – tłumaczymy na czytelny dziennik
        static readonly string[][] Pl = {
            new[] { "*EXTRA File", "Dodatkowy w celu" }, new[] { "*EXTRA Dir", "Dodatkowy folder" },
            new[] { "New File", "Nowy plik" }, new[] { "New Dir", "Nowy folder" }, new[] { "Newer", "Nowszy" },
            new[] { "Older", "Starszy" }, new[] { "Changed", "Zmieniony" }, new[] { "Modified", "Zmieniony" },
            new[] { "Tweaked", "Poprawiony" }, new[] { "Same", "Ten sam" }, new[] { "Mismatch", "Niezgodny" },
            new[] { "Total", "Razem" }, new[] { "Copied", "Skopiowano" }, new[] { "Skipped", "Pominięto" },
            new[] { "FAILED", "BŁĘDY" }, new[] { "Extras", "Dodatkowe" }, new[] { "Dirs :", "Foldery :" },
            new[] { "Files :", "Pliki :" }, new[] { "Bytes :", "Bajty :" }, new[] { "Times :", "Czas :" },
            new[] { "Speed :", "Szybkość :" }, new[] { "Ended :", "Koniec :" }, new[] { "Bytes/sec.", "bajtów/s" },
            new[] { "MegaBytes/min.", "MB/min" }
        };

        static string Translate(string s)
        {
            foreach (var p in Pl) s = s.Replace(p[0], p[1]);
            return s;
        }

        void Log(string s)
        {
            s = Translate(s);
            if (IsDisposed) return;
            if (txtLog.TextLength > 1500000) txtLog.Clear();
            txtLog.AppendText(s + Environment.NewLine);
        }

        void Ui(Action a) { if (!IsDisposed) try { BeginInvoke(a); } catch { } }

        static string Fmt(long b)
        {
            if (b < 1024) return b + " B";
            if (b < 1048576) return (b / 1024.0).ToString("0.0") + " KB";
            if (b < 1073741824) return (b / 1048576.0).ToString("0.0") + " MB";
            return (b / 1073741824.0).ToString("0.00") + " GB";
        }

        // ---------- uruchomienie

        void Start()
        {
            string src = SrcPath(), dst = DstPath();
            if (src.Length == 0 || txtDst.Text.Trim().Length == 0) { MessageBox.Show(this, "Podaj źródło i cel.", Text); return; }
            if (!Directory.Exists(src)) { MessageBox.Show(this, "Folder źródłowy nie istnieje lub jest niedostępny:\n" + src, Text); return; }
            if (dst.Equals(src, StringComparison.OrdinalIgnoreCase) || dst.StartsWith(src + "\\", StringComparison.OrdinalIgnoreCase))
            { MessageBox.Show(this, "Cel nie może być tym samym folderem co źródło ani leżeć w jego środku.", Text); return; }

            // szybkie policzenie źródła – do paska postępu i potwierdzenia
            long totalBytes = 0; int totalFiles = 0;
            Cursor = Cursors.WaitCursor;
            try
            {
                foreach (var f in new DirectoryInfo(src).EnumerateFiles("*", SearchOption.AllDirectories))
                { totalBytes += f.Length; totalFiles++; }
            }
            catch { }
            Cursor = Cursors.Default;

            if (rbMove.Checked &&
                MessageBox.Show(this, "PRZENIEŚĆ " + totalFiles + " plików (" + Fmt(totalBytes) + ")?\n\nZ:  " + src + "\nDo:  " + dst +
                                "\n\nPo skopiowaniu pliki zostaną usunięte ze źródła.", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;

            string args = BuildArgs();
            txtLog.Clear();
            Log("robocopy " + args);
            Log("");
            mainLog("=== " + (rbMove.Checked ? "PRZENOSZENIE" : "KOPIOWANIE") + " (robocopy): " + src + "  →  " + dst);
            SetRunning(true);
            bar.Value = 0;
            cancelled = false;
            string opName = rbMove.Checked ? "Przenoszenie" : "Kopiowanie";

            var sw = Stopwatch.StartNew();
            var th = new Thread(() =>
            {
                long done = 0; int files = 0, errors = 0, lastTick = 0;
                // linia pliku:  <typ>  <rozmiar>  <pełna ścieżka>
                var rxFile = new Regex(@"^\s*(.*?)\s+(\d+)\s+((?:[A-Za-z]:|\\\\).+)$");
                var rxErr = new Regex(@"(ERROR|BŁĄD|BLAD)\s+\d+", RegexOptions.IgnoreCase);
                var summary = new List<string>();
                int exit = -1;
                try
                {
                    var psi = new ProcessStartInfo("robocopy.exe", args)
                    {
                        UseShellExecute = false, CreateNoWindow = true,
                        RedirectStandardOutput = true, RedirectStandardError = true,
                        StandardOutputEncoding = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage)
                    };
                    proc = Process.Start(psi);
                    string line;
                    bool inSummary = false;
                    while ((line = proc.StandardOutput.ReadLine()) != null)
                    {
                        if (line.Trim().Length == 0) continue;
                        if (line.TrimStart().StartsWith("----")) { inSummary = true; continue; }
                        if (inSummary) { summary.Add(line); continue; }

                        if (rxErr.IsMatch(line))
                        {
                            errors++;
                            string l = line.Trim();
                            Ui(() => { Log("BŁĄD: " + l); mainLog("  BŁĄD (robocopy): " + l); });
                            continue;
                        }
                        var m = rxFile.Match(line);
                        if (m.Success)
                        {
                            long sz;
                            if (long.TryParse(m.Groups[2].Value, out sz)) done += sz;
                            files++;
                            string type = m.Groups[1].Value.Trim(), path = m.Groups[3].Value.Trim();
                            int f = files; long d = done;
                            Ui(() => Log(type + "  " + path));
                            if (Environment.TickCount - lastTick > 150)
                            {
                                lastTick = Environment.TickCount;
                                Ui(() =>
                                {
                                    bar.Value = (int)Math.Min(1000, totalBytes > 0 ? d * 1000 / totalBytes : 0);
                                    lblStatus.Text = opName + ": " + f + " / " + totalFiles + " plików, " + Fmt(d) + " / " + Fmt(totalBytes) +
                                                     "   " + (sw.Elapsed.TotalSeconds > 1 ? Fmt((long)(d / sw.Elapsed.TotalSeconds)) + "/s" : "");
                                });
                            }
                        }
                        else
                        {
                            string l = line.Trim();
                            Ui(() => Log(l));
                        }
                    }
                    proc.WaitForExit();
                    exit = proc.ExitCode;
                }
                catch (Exception ex)
                {
                    string msg = ex.Message;
                    Ui(() => Log("Nie udało się uruchomić robocopy: " + msg));
                }

                long elapsedMs = sw.ElapsedMilliseconds;
                Ui(() =>
                {
                    Log("");
                    foreach (var s in summary) Log(s);
                    // kody wyjścia robocopy: 0–7 = sukces (bitowo), 8+ = błędy
                    bool ok = exit >= 0 && exit < 8 && !cancelled;
                    string res = cancelled ? "Przerwano."
                               : exit < 0 ? "Błąd uruchomienia."
                               : exit == 0 ? "Nic do zrobienia – cel jest aktualny."
                               : exit < 8 ? "Zakończono pomyślnie" + ((exit & 4) != 0 ? " (w celu są dodatkowe pliki, których nie ma w źródle)" : "") + "."
                               : "Zakończono z błędami (kod " + exit + ") – zobacz dziennik poniżej.";
                    lblStatus.Text = res + "   Plików: " + files + ", " + Fmt(done) + ", czas " + TimeSpan.FromMilliseconds(elapsedMs).ToString(@"hh\:mm\:ss");
                    lblStatus.ForeColor = ok ? Color.DarkGreen : Color.DarkRed;
                    if (ok) bar.Value = 1000;
                    mainLog("  " + res + " Plików: " + files + ", " + Fmt(done) + ", błędów: " + errors + ", kod robocopy: " + exit);
                    SetRunning(false);
                    proc = null;
                });
            });
            th.IsBackground = true;
            th.Start();
        }

        void Cancel()
        {
            cancelled = true;
            try { if (proc != null && !proc.HasExited) proc.Kill(); } catch { }
            lblStatus.Text = "Przerywanie...";
        }

        void SetRunning(bool r)
        {
            btnStart.Enabled = !r;
            btnCancel.Enabled = r;
            lblStatus.ForeColor = SystemColors.ControlText;
            foreach (Control c in new Control[] { txtSrc, txtDst, txtExcl, rbCopy, rbMove, chkSub, chkXO, chkZ, chkEmpty, numThreads, numRetry })
                c.Enabled = !r;
            chkB.Enabled = !r && IsAdmin;
        }
    }
}
