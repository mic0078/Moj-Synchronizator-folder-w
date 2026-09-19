namespace MojSync
{
    partial class MainForm
    {
        /// <summary>Wymagana zmienna projektanta.</summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>Czyszczenie używanych zasobów.</summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null)) components.Dispose();
            base.Dispose(disposing);
        }

        #region Kod generowany przez Projektanta formularzy systemu Windows

        /// <summary>
        /// Metoda wymagana do obsługi projektanta - nie należy modyfikować
        /// zawartości tej metody w edytorze kodu (edytuj wizualnie w Designerze).
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.splitMain = new System.Windows.Forms.SplitContainer();
            this.lstTasks = new System.Windows.Forms.ListBox();
            this.taskBtns = new System.Windows.Forms.FlowLayoutPanel();
            this.btnNoweZadanie = new System.Windows.Forms.Button();
            this.btnDuplikuj = new System.Windows.Forms.Button();
            this.btnUsunZadanie = new System.Windows.Forms.Button();
            this.btnTaskUp = new System.Windows.Forms.Button();
            this.btnTaskDown = new System.Windows.Forms.Button();
            this.btnDeleteFiles = new System.Windows.Forms.Button();
            this.btnDecyzjeLista = new System.Windows.Forms.Button();
            this.btnArchiwum = new System.Windows.Forms.Button();
            this.searchTasksPanel = new System.Windows.Forms.FlowLayoutPanel();
            this.lblSearchTasksIcon = new System.Windows.Forms.Label();
            this.txtSearchTasks = new System.Windows.Forms.TextBox();
            this.lblTasksHeader = new System.Windows.Forms.Label();
            this.tabs = new System.Windows.Forms.TabControl();
            this.tabPrev = new System.Windows.Forms.TabPage();
            this.lvActions = new System.Windows.Forms.ListView();
            this.colAkcja = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.colSciezka = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.colRozmiar = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.colDataZrodlo = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.colDataCel = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.colPowod = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.lvMenu = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.miExpandAll = new System.Windows.Forms.ToolStripMenuItem();
            this.miCollapseAll = new System.Windows.Forms.ToolStripMenuItem();
            this.bottomBar = new System.Windows.Forms.Panel();
            this.lblSummary = new System.Windows.Forms.Label();
            this.chkShowSame = new System.Windows.Forms.CheckBox();
            this.lblSelection = new System.Windows.Forms.Label();
            this.searchPanel = new System.Windows.Forms.FlowLayoutPanel();
            this.lblSearchIcon = new System.Windows.Forms.Label();
            this.txtSearchTree = new System.Windows.Forms.TextBox();
            this.btnSearchTree = new System.Windows.Forms.Button();
            this.lblSearchInfo = new System.Windows.Forms.Label();
            this.tabLog = new System.Windows.Forms.TabPage();
            this.txtLog = new System.Windows.Forms.RichTextBox();
            this.progPanel = new System.Windows.Forms.Panel();
            this.statusRow = new System.Windows.Forms.Panel();
            this.lblStatus = new System.Windows.Forms.Label();
            this.lblSpeed = new System.Windows.Forms.Label();
            this.progress = new MojSync.ShimmerProgressBar();
            this.runBtns = new System.Windows.Forms.FlowLayoutPanel();
            this.btnPreview = new System.Windows.Forms.Button();
            this.btnSync = new System.Windows.Forms.Button();
            this.btnRunAll = new System.Windows.Forms.Button();
            this.btnCancel = new System.Windows.Forms.Button();
            this.btnRobo = new System.Windows.Forms.Button();
            this.chkAutomation = new System.Windows.Forms.CheckBox();
            this.chkAutostart = new System.Windows.Forms.CheckBox();
            this.grpEdit = new System.Windows.Forms.GroupBox();
            this.tlEdit = new System.Windows.Forms.TableLayoutPanel();
            this.lblRowNazwa = new System.Windows.Forms.Label();
            this.txtName = new System.Windows.Forms.TextBox();
            this.chkEnabled = new System.Windows.Forms.CheckBox();
            this.lblRowPodfolder = new System.Windows.Forms.Label();
            this.cpuGraph = new MojSync.MainForm.CpuGraph();
            this.subPanel = new System.Windows.Forms.FlowLayoutPanel();
            this.chkSubFolder = new System.Windows.Forms.CheckBox();
            this.lblTarget = new System.Windows.Forms.Label();
            this.lblRowTryb = new System.Windows.Forms.Label();
            this.chkMirror = new System.Windows.Forms.CheckBox();
            this.lblRowUsuwane = new System.Windows.Forms.Label();
            this.usuwaniePanel = new System.Windows.Forms.FlowLayoutPanel();
            this.cmbDelete = new System.Windows.Forms.ComboBox();
            this.chkWersje = new System.Windows.Forms.CheckBox();
            this.lblRowWyklucz = new System.Windows.Forms.Label();
            this.txtExcl = new System.Windows.Forms.TextBox();
            this.lblExclHint = new System.Windows.Forms.Label();
            this.lblRowAuto = new System.Windows.Forms.Label();
            this.autoPanel = new System.Windows.Forms.FlowLayoutPanel();
            this.chkInterval = new System.Windows.Forms.CheckBox();
            this.numInterval = new System.Windows.Forms.NumericUpDown();
            this.lblMinut = new System.Windows.Forms.Label();
            this.chkWatch = new System.Windows.Forms.CheckBox();
            this.lblPrzez = new System.Windows.Forms.Label();
            this.cmbWatchDelay = new System.Windows.Forms.ComboBox();
            this.lblRowPusty = new System.Windows.Forms.Label();
            this.savePanel = new System.Windows.Forms.FlowLayoutPanel();
            this.btnSave = new System.Windows.Forms.Button();
            this.btnDiscard = new System.Windows.Forms.Button();
            this.linkStalaDecyzja = new System.Windows.Forms.LinkLabel();
            this.lblInfo = new System.Windows.Forms.Label();
            this.lblStatusBar = new System.Windows.Forms.Label();
            this.folderBar = new MojSync.GradientPanel();
            this.fbTable = new System.Windows.Forms.TableLayoutPanel();
            this.panelLewy = new System.Windows.Forms.TableLayoutPanel();
            this.btnFolderLewy = new System.Windows.Forms.Button();
            this.lblLewyTytul = new System.Windows.Forms.Label();
            this.btnSprawdzLewy = new System.Windows.Forms.Button();
            this.txtLeft = new System.Windows.Forms.TextBox();
            this.centerPanel = new System.Windows.Forms.TableLayoutPanel();
            this.arrow = new MojSync.DirectionArrow();
            this.lblDirMode = new System.Windows.Forms.Label();
            this.lnkSwap = new System.Windows.Forms.LinkLabel();
            this.cmbDir = new System.Windows.Forms.ComboBox();
            this.panelPrawy = new System.Windows.Forms.TableLayoutPanel();
            this.btnFolderPrawy = new System.Windows.Forms.Button();
            this.lblPrawyTytul = new System.Windows.Forms.Label();
            this.btnSprawdzPrawy = new System.Windows.Forms.Button();
            this.txtRight = new System.Windows.Forms.TextBox();
            this.panelDecyzji = new System.Windows.Forms.Panel();
            this.lblDecyzji = new System.Windows.Forms.Label();
            this.btnDecyduj = new System.Windows.Forms.Button();
            this.headerPanel = new MojSync.GradientPanel();
            ((System.ComponentModel.ISupportInitialize)(this.splitMain)).BeginInit();
            this.splitMain.Panel1.SuspendLayout();
            this.splitMain.Panel2.SuspendLayout();
            this.splitMain.SuspendLayout();
            this.taskBtns.SuspendLayout();
            this.searchTasksPanel.SuspendLayout();
            this.tabs.SuspendLayout();
            this.tabPrev.SuspendLayout();
            this.lvMenu.SuspendLayout();
            this.bottomBar.SuspendLayout();
            this.searchPanel.SuspendLayout();
            this.tabLog.SuspendLayout();
            this.progPanel.SuspendLayout();
            this.statusRow.SuspendLayout();
            this.runBtns.SuspendLayout();
            this.grpEdit.SuspendLayout();
            this.tlEdit.SuspendLayout();
            this.subPanel.SuspendLayout();
            this.usuwaniePanel.SuspendLayout();
            this.autoPanel.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numInterval)).BeginInit();
            this.savePanel.SuspendLayout();
            this.folderBar.SuspendLayout();
            this.fbTable.SuspendLayout();
            this.panelLewy.SuspendLayout();
            this.centerPanel.SuspendLayout();
            this.panelPrawy.SuspendLayout();
            this.panelDecyzji.SuspendLayout();
            this.SuspendLayout();
            // 
            // splitMain
            // 
            this.splitMain.Dock = System.Windows.Forms.DockStyle.Fill;
            this.splitMain.FixedPanel = System.Windows.Forms.FixedPanel.Panel1;
            this.splitMain.Location = new System.Drawing.Point(0, 60);
            this.splitMain.Name = "splitMain";
            // 
            // splitMain.Panel1
            // 
            this.splitMain.Panel1.Controls.Add(this.lstTasks);
            this.splitMain.Panel1.Controls.Add(this.taskBtns);
            this.splitMain.Panel1.Controls.Add(this.searchTasksPanel);
            this.splitMain.Panel1.Controls.Add(this.lblTasksHeader);
            // 
            // splitMain.Panel2
            // 
            this.splitMain.Panel2.Controls.Add(this.tabs);
            this.splitMain.Panel2.Controls.Add(this.progPanel);
            this.splitMain.Panel2.Controls.Add(this.runBtns);
            this.splitMain.Panel2.Controls.Add(this.grpEdit);
            this.splitMain.Panel2.Padding = new System.Windows.Forms.Padding(6, 0, 6, 6);
            this.splitMain.Size = new System.Drawing.Size(1364, 781);
            this.splitMain.TabIndex = 0;
            // 
            // lstTasks
            // 
            this.lstTasks.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lstTasks.DrawMode = System.Windows.Forms.DrawMode.OwnerDrawFixed;
            this.lstTasks.Font = new System.Drawing.Font("Segoe UI", 10F);
            this.lstTasks.IntegralHeight = false;
            this.lstTasks.ItemHeight = 22;
            this.lstTasks.Location = new System.Drawing.Point(0, 51);
            this.lstTasks.Name = "lstTasks";
            this.lstTasks.Size = new System.Drawing.Size(50, 550);
            this.lstTasks.TabIndex = 0;
            // 
            // taskBtns
            // 
            this.taskBtns.Controls.Add(this.btnNoweZadanie);
            this.taskBtns.Controls.Add(this.btnDuplikuj);
            this.taskBtns.Controls.Add(this.btnUsunZadanie);
            this.taskBtns.Controls.Add(this.btnTaskUp);
            this.taskBtns.Controls.Add(this.btnTaskDown);
            this.taskBtns.Controls.Add(this.btnDeleteFiles);
            this.taskBtns.Controls.Add(this.btnDecyzjeLista);
            this.taskBtns.Controls.Add(this.btnArchiwum);
            this.taskBtns.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.taskBtns.Location = new System.Drawing.Point(0, 601);
            this.taskBtns.Name = "taskBtns";
            this.taskBtns.Padding = new System.Windows.Forms.Padding(2);
            this.taskBtns.Size = new System.Drawing.Size(50, 180);
            this.taskBtns.TabIndex = 1;
            // 
            // btnNoweZadanie
            // 
            this.btnNoweZadanie.Location = new System.Drawing.Point(5, 5);
            this.btnNoweZadanie.Name = "btnNoweZadanie";
            this.btnNoweZadanie.Size = new System.Drawing.Size(70, 28);
            this.btnNoweZadanie.TabIndex = 0;
            this.btnNoweZadanie.Text = "Nowe";
            // 
            // btnDuplikuj
            // 
            this.btnDuplikuj.Location = new System.Drawing.Point(5, 39);
            this.btnDuplikuj.Name = "btnDuplikuj";
            this.btnDuplikuj.Size = new System.Drawing.Size(70, 28);
            this.btnDuplikuj.TabIndex = 1;
            this.btnDuplikuj.Text = "Duplikuj";
            // 
            // btnUsunZadanie
            // 
            this.btnUsunZadanie.Location = new System.Drawing.Point(5, 73);
            this.btnUsunZadanie.Name = "btnUsunZadanie";
            this.btnUsunZadanie.Size = new System.Drawing.Size(70, 28);
            this.btnUsunZadanie.TabIndex = 2;
            this.btnUsunZadanie.Text = "Usuń";
            // 
            // btnTaskUp
            // 
            this.btnTaskUp.Location = new System.Drawing.Point(5, 107);
            this.btnTaskUp.Name = "btnTaskUp";
            this.btnTaskUp.Size = new System.Drawing.Size(34, 28);
            this.btnTaskUp.TabIndex = 3;
            this.btnTaskUp.Text = "▲";
            // 
            // btnTaskDown
            // 
            this.taskBtns.SetFlowBreak(this.btnTaskDown, true);
            this.btnTaskDown.Location = new System.Drawing.Point(5, 141);
            this.btnTaskDown.Name = "btnTaskDown";
            this.btnTaskDown.Size = new System.Drawing.Size(34, 28);
            this.btnTaskDown.TabIndex = 4;
            this.btnTaskDown.Text = "▼";
            // 
            // btnDeleteFiles
            // 
            this.btnDeleteFiles.Enabled = false;
            this.taskBtns.SetFlowBreak(this.btnDeleteFiles, true);
            this.btnDeleteFiles.Location = new System.Drawing.Point(5, 175);
            this.btnDeleteFiles.Name = "btnDeleteFiles";
            this.btnDeleteFiles.Size = new System.Drawing.Size(226, 28);
            this.btnDeleteFiles.TabIndex = 5;
            this.btnDeleteFiles.Text = "🗑 Usuń wybrane pliki";
            // 
            // btnDecyzjeLista
            // 
            this.btnDecyzjeLista.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(255)))), ((int)(((byte)(210)))), ((int)(((byte)(120)))));
            this.taskBtns.SetFlowBreak(this.btnDecyzjeLista, true);
            this.btnDecyzjeLista.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold);
            this.btnDecyzjeLista.Location = new System.Drawing.Point(5, 209);
            this.btnDecyzjeLista.Name = "btnDecyzjeLista";
            this.btnDecyzjeLista.Size = new System.Drawing.Size(226, 28);
            this.btnDecyzjeLista.TabIndex = 6;
            this.btnDecyzjeLista.Text = "❗ Decyzje do podjęcia";
            this.btnDecyzjeLista.UseVisualStyleBackColor = false;
            this.btnDecyzjeLista.Visible = false;
            // 
            // btnArchiwum
            // 
            this.btnArchiwum.Location = new System.Drawing.Point(5, 243);
            this.btnArchiwum.Name = "btnArchiwum";
            this.btnArchiwum.Size = new System.Drawing.Size(226, 28);
            this.btnArchiwum.TabIndex = 7;
            this.btnArchiwum.Text = "🧹 Archiwum kopii";
            // 
            // searchTasksPanel
            // 
            this.searchTasksPanel.Controls.Add(this.lblSearchTasksIcon);
            this.searchTasksPanel.Controls.Add(this.txtSearchTasks);
            this.searchTasksPanel.Dock = System.Windows.Forms.DockStyle.Top;
            this.searchTasksPanel.Location = new System.Drawing.Point(0, 24);
            this.searchTasksPanel.Name = "searchTasksPanel";
            this.searchTasksPanel.Padding = new System.Windows.Forms.Padding(3, 0, 3, 0);
            this.searchTasksPanel.Size = new System.Drawing.Size(50, 27);
            this.searchTasksPanel.TabIndex = 2;
            // 
            // lblSearchTasksIcon
            // 
            this.lblSearchTasksIcon.AutoSize = true;
            this.lblSearchTasksIcon.Location = new System.Drawing.Point(3, 4);
            this.lblSearchTasksIcon.Margin = new System.Windows.Forms.Padding(0, 4, 2, 0);
            this.lblSearchTasksIcon.Name = "lblSearchTasksIcon";
            this.lblSearchTasksIcon.Size = new System.Drawing.Size(19, 15);
            this.lblSearchTasksIcon.TabIndex = 0;
            this.lblSearchTasksIcon.Text = "🔎";
            // 
            // txtSearchTasks
            // 
            this.txtSearchTasks.Location = new System.Drawing.Point(3, 21);
            this.txtSearchTasks.Margin = new System.Windows.Forms.Padding(0, 2, 0, 0);
            this.txtSearchTasks.Name = "txtSearchTasks";
            this.txtSearchTasks.Size = new System.Drawing.Size(150, 23);
            this.txtSearchTasks.TabIndex = 1;
            // 
            // lblTasksHeader
            // 
            this.lblTasksHeader.Dock = System.Windows.Forms.DockStyle.Top;
            this.lblTasksHeader.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold);
            this.lblTasksHeader.Location = new System.Drawing.Point(0, 0);
            this.lblTasksHeader.Name = "lblTasksHeader";
            this.lblTasksHeader.Padding = new System.Windows.Forms.Padding(3, 4, 0, 0);
            this.lblTasksHeader.Size = new System.Drawing.Size(50, 24);
            this.lblTasksHeader.TabIndex = 3;
            this.lblTasksHeader.Text = "Zadania";
            // 
            // tabs
            // 
            this.tabs.Controls.Add(this.tabPrev);
            this.tabs.Controls.Add(this.tabLog);
            this.tabs.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tabs.Location = new System.Drawing.Point(6, 540);
            this.tabs.Name = "tabs";
            this.tabs.SelectedIndex = 0;
            this.tabs.Size = new System.Drawing.Size(1298, 235);
            this.tabs.TabIndex = 0;
            // 
            // tabPrev
            // 
            this.tabPrev.Controls.Add(this.lvActions);
            this.tabPrev.Controls.Add(this.bottomBar);
            this.tabPrev.Controls.Add(this.searchPanel);
            this.tabPrev.Location = new System.Drawing.Point(4, 24);
            this.tabPrev.Name = "tabPrev";
            this.tabPrev.Size = new System.Drawing.Size(1290, 207);
            this.tabPrev.TabIndex = 0;
            this.tabPrev.Text = "Podgląd zmian";
            this.tabPrev.UseVisualStyleBackColor = true;
            // 
            // lvActions
            // 
            this.lvActions.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.colAkcja,
            this.colSciezka,
            this.colRozmiar,
            this.colDataZrodlo,
            this.colDataCel,
            this.colPowod});
            this.lvActions.ContextMenuStrip = this.lvMenu;
            this.lvActions.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lvActions.FullRowSelect = true;
            this.lvActions.GridLines = true;
            this.lvActions.HideSelection = false;
            this.lvActions.Location = new System.Drawing.Point(0, 30);
            this.lvActions.Name = "lvActions";
            this.lvActions.Size = new System.Drawing.Size(1290, 153);
            this.lvActions.TabIndex = 0;
            this.lvActions.UseCompatibleStateImageBehavior = false;
            this.lvActions.View = System.Windows.Forms.View.Details;
            // 
            // colAkcja
            // 
            this.colAkcja.Text = "Akcja";
            this.colAkcja.Width = 120;
            // 
            // colSciezka
            // 
            this.colSciezka.Text = "Ścieżka";
            this.colSciezka.Width = 400;
            // 
            // colRozmiar
            // 
            this.colRozmiar.Text = "Rozmiar";
            this.colRozmiar.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.colRozmiar.Width = 85;
            // 
            // colDataZrodlo
            // 
            this.colDataZrodlo.Text = "Data w źródle";
            this.colDataZrodlo.Width = 125;
            // 
            // colDataCel
            // 
            this.colDataCel.Text = "Data w celu";
            this.colDataCel.Width = 125;
            // 
            // colPowod
            // 
            this.colPowod.Text = "Powód";
            this.colPowod.Width = 190;
            // 
            // lvMenu
            // 
            this.lvMenu.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.miExpandAll,
            this.miCollapseAll});
            this.lvMenu.Name = "lvMenu";
            this.lvMenu.Size = new System.Drawing.Size(205, 48);
            // 
            // miExpandAll
            // 
            this.miExpandAll.Name = "miExpandAll";
            this.miExpandAll.Size = new System.Drawing.Size(204, 22);
            this.miExpandAll.Text = "Rozwiń wszystkie foldery";
            // 
            // miCollapseAll
            // 
            this.miCollapseAll.Name = "miCollapseAll";
            this.miCollapseAll.Size = new System.Drawing.Size(204, 22);
            this.miCollapseAll.Text = "Zwiń wszystkie foldery";
            // 
            // bottomBar
            // 
            this.bottomBar.Controls.Add(this.lblSummary);
            this.bottomBar.Controls.Add(this.chkShowSame);
            this.bottomBar.Controls.Add(this.lblSelection);
            this.bottomBar.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.bottomBar.Location = new System.Drawing.Point(0, 183);
            this.bottomBar.Name = "bottomBar";
            this.bottomBar.Size = new System.Drawing.Size(1290, 24);
            this.bottomBar.TabIndex = 1;
            // 
            // lblSummary
            // 
            this.lblSummary.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblSummary.Location = new System.Drawing.Point(0, 0);
            this.lblSummary.Name = "lblSummary";
            this.lblSummary.Size = new System.Drawing.Size(850, 24);
            this.lblSummary.TabIndex = 0;
            this.lblSummary.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // chkShowSame
            // 
            this.chkShowSame.Checked = true;
            this.chkShowSame.CheckState = System.Windows.Forms.CheckState.Checked;
            this.chkShowSame.Dock = System.Windows.Forms.DockStyle.Right;
            this.chkShowSame.Location = new System.Drawing.Point(850, 0);
            this.chkShowSame.Name = "chkShowSame";
            this.chkShowSame.Size = new System.Drawing.Size(170, 24);
            this.chkShowSame.TabIndex = 1;
            this.chkShowSame.Text = "Pokaż pliki bez zmian";
            // 
            // lblSelection
            // 
            this.lblSelection.Dock = System.Windows.Forms.DockStyle.Right;
            this.lblSelection.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold);
            this.lblSelection.ForeColor = System.Drawing.Color.DarkBlue;
            this.lblSelection.Location = new System.Drawing.Point(1020, 0);
            this.lblSelection.Name = "lblSelection";
            this.lblSelection.Size = new System.Drawing.Size(270, 24);
            this.lblSelection.TabIndex = 2;
            this.lblSelection.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            // 
            // searchPanel
            // 
            this.searchPanel.Controls.Add(this.lblSearchIcon);
            this.searchPanel.Controls.Add(this.txtSearchTree);
            this.searchPanel.Controls.Add(this.btnSearchTree);
            this.searchPanel.Controls.Add(this.lblSearchInfo);
            this.searchPanel.Dock = System.Windows.Forms.DockStyle.Top;
            this.searchPanel.Location = new System.Drawing.Point(0, 0);
            this.searchPanel.Name = "searchPanel";
            this.searchPanel.Padding = new System.Windows.Forms.Padding(3, 3, 3, 0);
            this.searchPanel.Size = new System.Drawing.Size(1290, 30);
            this.searchPanel.TabIndex = 2;
            // 
            // lblSearchIcon
            // 
            this.lblSearchIcon.AutoSize = true;
            this.lblSearchIcon.Location = new System.Drawing.Point(5, 8);
            this.lblSearchIcon.Margin = new System.Windows.Forms.Padding(2, 5, 2, 0);
            this.lblSearchIcon.Name = "lblSearchIcon";
            this.lblSearchIcon.Size = new System.Drawing.Size(19, 15);
            this.lblSearchIcon.TabIndex = 0;
            this.lblSearchIcon.Text = "🔎";
            // 
            // txtSearchTree
            // 
            this.txtSearchTree.Location = new System.Drawing.Point(26, 6);
            this.txtSearchTree.Margin = new System.Windows.Forms.Padding(0, 3, 4, 0);
            this.txtSearchTree.Name = "txtSearchTree";
            this.txtSearchTree.Size = new System.Drawing.Size(220, 23);
            this.txtSearchTree.TabIndex = 1;
            // 
            // btnSearchTree
            // 
            this.btnSearchTree.Location = new System.Drawing.Point(253, 6);
            this.btnSearchTree.Name = "btnSearchTree";
            this.btnSearchTree.Size = new System.Drawing.Size(90, 28);
            this.btnSearchTree.TabIndex = 2;
            this.btnSearchTree.Text = "Znajdź";
            // 
            // lblSearchInfo
            // 
            this.lblSearchInfo.AutoSize = true;
            this.lblSearchInfo.ForeColor = System.Drawing.Color.DimGray;
            this.lblSearchInfo.Location = new System.Drawing.Point(352, 10);
            this.lblSearchInfo.Margin = new System.Windows.Forms.Padding(6, 7, 0, 0);
            this.lblSearchInfo.Name = "lblSearchInfo";
            this.lblSearchInfo.Size = new System.Drawing.Size(0, 15);
            this.lblSearchInfo.TabIndex = 3;
            // 
            // tabLog
            // 
            this.tabLog.Controls.Add(this.txtLog);
            this.tabLog.Location = new System.Drawing.Point(4, 22);
            this.tabLog.Name = "tabLog";
            this.tabLog.Size = new System.Drawing.Size(1290, 209);
            this.tabLog.TabIndex = 1;
            this.tabLog.Text = "Dziennik";
            this.tabLog.UseVisualStyleBackColor = true;
            // 
            // txtLog
            // 
            this.txtLog.BackColor = System.Drawing.Color.White;
            this.txtLog.DetectUrls = false;
            this.txtLog.Dock = System.Windows.Forms.DockStyle.Fill;
            this.txtLog.Font = new System.Drawing.Font("Consolas", 9F);
            this.txtLog.Location = new System.Drawing.Point(0, 0);
            this.txtLog.Name = "txtLog";
            this.txtLog.ReadOnly = true;
            this.txtLog.Size = new System.Drawing.Size(1290, 209);
            this.txtLog.TabIndex = 0;
            this.txtLog.Text = "";
            this.txtLog.WordWrap = false;
            // 
            // progPanel
            // 
            this.progPanel.Controls.Add(this.statusRow);
            this.progPanel.Controls.Add(this.progress);
            this.progPanel.Dock = System.Windows.Forms.DockStyle.Top;
            this.progPanel.Location = new System.Drawing.Point(6, 494);
            this.progPanel.Name = "progPanel";
            this.progPanel.Size = new System.Drawing.Size(1298, 46);
            this.progPanel.TabIndex = 1;
            // 
            // statusRow
            // 
            this.statusRow.Controls.Add(this.lblStatus);
            this.statusRow.Controls.Add(this.lblSpeed);
            this.statusRow.Dock = System.Windows.Forms.DockStyle.Top;
            this.statusRow.Location = new System.Drawing.Point(0, 18);
            this.statusRow.Name = "statusRow";
            this.statusRow.Size = new System.Drawing.Size(1298, 22);
            this.statusRow.TabIndex = 0;
            // 
            // lblStatus
            // 
            this.lblStatus.AutoEllipsis = true;
            this.lblStatus.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblStatus.Location = new System.Drawing.Point(0, 0);
            this.lblStatus.Name = "lblStatus";
            this.lblStatus.Size = new System.Drawing.Size(1188, 22);
            this.lblStatus.TabIndex = 0;
            this.lblStatus.Text = "Gotowy.";
            this.lblStatus.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // lblSpeed
            // 
            this.lblSpeed.Dock = System.Windows.Forms.DockStyle.Right;
            this.lblSpeed.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold);
            this.lblSpeed.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(90)))), ((int)(((byte)(180)))));
            this.lblSpeed.Location = new System.Drawing.Point(1188, 0);
            this.lblSpeed.Name = "lblSpeed";
            this.lblSpeed.Size = new System.Drawing.Size(110, 22);
            this.lblSpeed.TabIndex = 1;
            this.lblSpeed.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            // 
            // progress
            // 
            this.progress.Animate = false;
            this.progress.Dock = System.Windows.Forms.DockStyle.Top;
            this.progress.Location = new System.Drawing.Point(0, 0);
            this.progress.Maximum = 1000;
            this.progress.Name = "progress";
            this.progress.Size = new System.Drawing.Size(1298, 18);
            this.progress.TabIndex = 1;
            this.progress.Value = 0;
            // 
            // runBtns
            // 
            this.runBtns.AutoScroll = true;
            this.runBtns.Controls.Add(this.btnPreview);
            this.runBtns.Controls.Add(this.btnSync);
            this.runBtns.Controls.Add(this.btnRunAll);
            this.runBtns.Controls.Add(this.btnCancel);
            this.runBtns.Controls.Add(this.btnRobo);
            this.runBtns.Controls.Add(this.chkAutomation);
            this.runBtns.Controls.Add(this.chkAutostart);
            this.runBtns.Dock = System.Windows.Forms.DockStyle.Top;
            this.runBtns.Location = new System.Drawing.Point(6, 418);
            this.runBtns.Name = "runBtns";
            this.runBtns.Padding = new System.Windows.Forms.Padding(0, 6, 0, 0);
            this.runBtns.Size = new System.Drawing.Size(1298, 76);
            this.runBtns.TabIndex = 2;
            // 
            // btnPreview
            // 
            this.btnPreview.Location = new System.Drawing.Point(3, 9);
            this.btnPreview.Name = "btnPreview";
            this.btnPreview.Size = new System.Drawing.Size(110, 28);
            this.btnPreview.TabIndex = 0;
            this.btnPreview.Text = "🔍 Analizuj";
            // 
            // btnSync
            // 
            this.btnSync.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold);
            this.btnSync.Location = new System.Drawing.Point(119, 9);
            this.btnSync.Name = "btnSync";
            this.btnSync.Size = new System.Drawing.Size(130, 28);
            this.btnSync.TabIndex = 1;
            this.btnSync.Text = "▶ Synchronizuj";
            // 
            // btnRunAll
            // 
            this.btnRunAll.Location = new System.Drawing.Point(255, 9);
            this.btnRunAll.Name = "btnRunAll";
            this.btnRunAll.Size = new System.Drawing.Size(210, 28);
            this.btnRunAll.TabIndex = 2;
            this.btnRunAll.Text = "▶▶ Uruchom wszystkie włączone";
            // 
            // btnCancel
            // 
            this.btnCancel.Enabled = false;
            this.btnCancel.Location = new System.Drawing.Point(471, 9);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Size = new System.Drawing.Size(100, 28);
            this.btnCancel.TabIndex = 3;
            this.btnCancel.Text = "■ Przerwij";
            // 
            // btnRobo
            // 
            this.btnRobo.Location = new System.Drawing.Point(577, 9);
            this.btnRobo.Name = "btnRobo";
            this.btnRobo.Size = new System.Drawing.Size(150, 28);
            this.btnRobo.TabIndex = 4;
            this.btnRobo.Text = "📦 Kopiuj / Przenieś";
            // 
            // chkAutomation
            // 
            this.chkAutomation.AutoSize = true;
            this.chkAutomation.Location = new System.Drawing.Point(746, 13);
            this.chkAutomation.Margin = new System.Windows.Forms.Padding(16, 7, 3, 3);
            this.chkAutomation.Name = "chkAutomation";
            this.chkAutomation.Size = new System.Drawing.Size(143, 19);
            this.chkAutomation.TabIndex = 5;
            this.chkAutomation.Text = "Automatyka włączona";
            // 
            // chkAutostart
            // 
            this.chkAutostart.AutoSize = true;
            this.chkAutostart.Location = new System.Drawing.Point(900, 13);
            this.chkAutostart.Margin = new System.Windows.Forms.Padding(8, 7, 3, 3);
            this.chkAutostart.Name = "chkAutostart";
            this.chkAutostart.Size = new System.Drawing.Size(190, 19);
            this.chkAutostart.TabIndex = 6;
            this.chkAutostart.Text = "Uruchamiaj z Windows (w tray)";
            // 
            // grpEdit
            // 
            this.grpEdit.Controls.Add(this.tlEdit);
            this.grpEdit.Controls.Add(this.lblStatusBar);
            this.grpEdit.Controls.Add(this.folderBar);
            this.grpEdit.Dock = System.Windows.Forms.DockStyle.Top;
            this.grpEdit.Location = new System.Drawing.Point(6, 0);
            this.grpEdit.Name = "grpEdit";
            this.grpEdit.Padding = new System.Windows.Forms.Padding(6);
            this.grpEdit.Size = new System.Drawing.Size(1298, 418);
            this.grpEdit.TabIndex = 3;
            this.grpEdit.TabStop = false;
            this.grpEdit.Text = "Ustawienia zadania";
            // 
            // tlEdit
            // 
            this.tlEdit.ColumnCount = 4;
            this.tlEdit.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 120F));
            this.tlEdit.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.tlEdit.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 40F));
            this.tlEdit.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 125F));
            this.tlEdit.Controls.Add(this.lblRowNazwa, 0, 0);
            this.tlEdit.Controls.Add(this.txtName, 1, 0);
            this.tlEdit.Controls.Add(this.chkEnabled, 3, 0);
            this.tlEdit.Controls.Add(this.lblRowPodfolder, 0, 1);
            this.tlEdit.Controls.Add(this.subPanel, 1, 1);
            this.tlEdit.Controls.Add(this.lblRowTryb, 0, 2);
            this.tlEdit.Controls.Add(this.chkMirror, 1, 2);
            this.tlEdit.Controls.Add(this.lblRowUsuwane, 0, 3);
            this.tlEdit.Controls.Add(this.usuwaniePanel, 1, 3);
            this.tlEdit.Controls.Add(this.lblRowWyklucz, 0, 4);
            this.tlEdit.Controls.Add(this.txtExcl, 1, 4);
            this.tlEdit.Controls.Add(this.lblExclHint, 3, 4);
            this.tlEdit.Controls.Add(this.lblRowAuto, 0, 5);
            this.tlEdit.Controls.Add(this.autoPanel, 1, 5);
            this.tlEdit.Controls.Add(this.lblRowPusty, 0, 6);
            this.tlEdit.Controls.Add(this.savePanel, 1, 6);
            this.tlEdit.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tlEdit.Location = new System.Drawing.Point(6, 164);
            this.tlEdit.Name = "tlEdit";
            this.tlEdit.Padding = new System.Windows.Forms.Padding(0, 6, 0, 0);
            this.tlEdit.RowCount = 7;
            this.tlEdit.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 32F));
            this.tlEdit.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 32F));
            this.tlEdit.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 32F));
            this.tlEdit.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 32F));
            this.tlEdit.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 32F));
            this.tlEdit.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 32F));
            this.tlEdit.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 36F));
            this.tlEdit.Size = new System.Drawing.Size(1286, 248);
            this.tlEdit.TabIndex = 0;
            // 
            // lblRowNazwa
            // 
            this.lblRowNazwa.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblRowNazwa.Location = new System.Drawing.Point(3, 6);
            this.lblRowNazwa.Name = "lblRowNazwa";
            this.lblRowNazwa.Size = new System.Drawing.Size(114, 32);
            this.lblRowNazwa.TabIndex = 0;
            this.lblRowNazwa.Text = "Nazwa:";
            this.lblRowNazwa.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // txtName
            // 
            this.tlEdit.SetColumnSpan(this.txtName, 2);
            this.txtName.Dock = System.Windows.Forms.DockStyle.Fill;
            this.txtName.Location = new System.Drawing.Point(123, 9);
            this.txtName.Name = "txtName";
            this.txtName.Size = new System.Drawing.Size(1035, 23);
            this.txtName.TabIndex = 1;
            // 
            // chkEnabled
            // 
            this.chkEnabled.Dock = System.Windows.Forms.DockStyle.Fill;
            this.chkEnabled.Location = new System.Drawing.Point(1164, 9);
            this.chkEnabled.Name = "chkEnabled";
            this.chkEnabled.Size = new System.Drawing.Size(119, 26);
            this.chkEnabled.TabIndex = 2;
            this.chkEnabled.Text = "Włączone";
            // 
            // lblRowPodfolder
            // 
            this.lblRowPodfolder.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblRowPodfolder.Location = new System.Drawing.Point(3, 38);
            this.lblRowPodfolder.Name = "lblRowPodfolder";
            this.lblRowPodfolder.Size = new System.Drawing.Size(114, 32);
            this.lblRowPodfolder.TabIndex = 3;
            this.lblRowPodfolder.Text = "Podfolder:";
            this.lblRowPodfolder.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // cpuGraph
            // 
            this.cpuGraph.BackColor = System.Drawing.Color.White;
            this.cpuGraph.Location = new System.Drawing.Point(60, 66);
            this.cpuGraph.Margin = new System.Windows.Forms.Padding(8, 2, 2, 2);
            this.cpuGraph.Name = "cpuGraph";
            this.cpuGraph.Size = new System.Drawing.Size(111, 38);
            this.cpuGraph.TabIndex = 5;
            // 
            // subPanel
            // 
            this.subPanel.Controls.Add(this.chkSubFolder);
            this.subPanel.Controls.Add(this.lblTarget);
            this.subPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.subPanel.Location = new System.Drawing.Point(120, 38);
            this.subPanel.Margin = new System.Windows.Forms.Padding(0);
            this.subPanel.Name = "subPanel";
            this.subPanel.Size = new System.Drawing.Size(1001, 32);
            this.subPanel.TabIndex = 4;
            this.subPanel.WrapContents = false;
            // 
            // chkSubFolder
            // 
            this.chkSubFolder.AutoSize = true;
            this.chkSubFolder.Location = new System.Drawing.Point(0, 6);
            this.chkSubFolder.Margin = new System.Windows.Forms.Padding(0, 6, 0, 0);
            this.chkSubFolder.Name = "chkSubFolder";
            this.chkSubFolder.Size = new System.Drawing.Size(184, 19);
            this.chkSubFolder.TabIndex = 0;
            this.chkSubFolder.Text = "Twórz podfolder źródła w celu";
            // 
            // lblTarget
            // 
            this.lblTarget.AutoSize = true;
            this.lblTarget.ForeColor = System.Drawing.Color.DarkBlue;
            this.lblTarget.Location = new System.Drawing.Point(200, 7);
            this.lblTarget.Margin = new System.Windows.Forms.Padding(16, 7, 0, 0);
            this.lblTarget.Name = "lblTarget";
            this.lblTarget.Size = new System.Drawing.Size(0, 15);
            this.lblTarget.TabIndex = 1;
            // 
            // lblRowTryb
            // 
            this.lblRowTryb.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblRowTryb.Location = new System.Drawing.Point(3, 70);
            this.lblRowTryb.Name = "lblRowTryb";
            this.lblRowTryb.Size = new System.Drawing.Size(114, 32);
            this.lblRowTryb.TabIndex = 6;
            this.lblRowTryb.Text = "Tryb:";
            this.lblRowTryb.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // chkMirror
            // 
            this.chkMirror.Dock = System.Windows.Forms.DockStyle.Fill;
            this.chkMirror.Location = new System.Drawing.Point(123, 73);
            this.chkMirror.Name = "chkMirror";
            this.chkMirror.Size = new System.Drawing.Size(995, 26);
            this.chkMirror.TabIndex = 7;
            this.chkMirror.Text = "Kopia lustrzana (usuwa z celu elementy nieobecne w źródle)";
            // 
            // lblRowUsuwane
            // 
            this.lblRowUsuwane.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblRowUsuwane.Location = new System.Drawing.Point(3, 102);
            this.lblRowUsuwane.Name = "lblRowUsuwane";
            this.lblRowUsuwane.Size = new System.Drawing.Size(114, 32);
            this.lblRowUsuwane.TabIndex = 8;
            this.lblRowUsuwane.Text = "Usuwane pliki:";
            this.lblRowUsuwane.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // usuwaniePanel
            // 
            this.tlEdit.SetColumnSpan(this.usuwaniePanel, 3);
            this.usuwaniePanel.Controls.Add(this.cmbDelete);
            this.usuwaniePanel.Controls.Add(this.chkWersje);
            this.usuwaniePanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.usuwaniePanel.Location = new System.Drawing.Point(120, 102);
            this.usuwaniePanel.Margin = new System.Windows.Forms.Padding(0);
            this.usuwaniePanel.Name = "usuwaniePanel";
            this.usuwaniePanel.Size = new System.Drawing.Size(1166, 32);
            this.usuwaniePanel.TabIndex = 9;
            this.usuwaniePanel.WrapContents = false;
            // 
            // cmbDelete
            // 
            this.cmbDelete.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbDelete.Items.AddRange(new object[] {
            "Archiwum _SyncArchive",
            "Kosz Windows",
            "Usuń trwale"});
            this.cmbDelete.Location = new System.Drawing.Point(0, 2);
            this.cmbDelete.Margin = new System.Windows.Forms.Padding(0, 2, 0, 0);
            this.cmbDelete.Name = "cmbDelete";
            this.cmbDelete.Size = new System.Drawing.Size(190, 23);
            this.cmbDelete.TabIndex = 0;
            // 
            // chkWersje
            // 
            this.chkWersje.AutoSize = true;
            this.chkWersje.Location = new System.Drawing.Point(202, 6);
            this.chkWersje.Margin = new System.Windows.Forms.Padding(12, 6, 0, 0);
            this.chkWersje.Name = "chkWersje";
            this.chkWersje.Size = new System.Drawing.Size(284, 19);
            this.chkWersje.TabIndex = 1;
            this.chkWersje.Text = "Zachowuj poprzednie wersje nadpisanych plików";
            // 
            // lblRowWyklucz
            // 
            this.lblRowWyklucz.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblRowWyklucz.Location = new System.Drawing.Point(3, 134);
            this.lblRowWyklucz.Name = "lblRowWyklucz";
            this.lblRowWyklucz.Size = new System.Drawing.Size(114, 32);
            this.lblRowWyklucz.TabIndex = 10;
            this.lblRowWyklucz.Text = "Wykluczenia:";
            this.lblRowWyklucz.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // txtExcl
            // 
            this.txtExcl.Dock = System.Windows.Forms.DockStyle.Fill;
            this.txtExcl.Location = new System.Drawing.Point(123, 137);
            this.txtExcl.Name = "txtExcl";
            this.txtExcl.Size = new System.Drawing.Size(995, 23);
            this.txtExcl.TabIndex = 11;
            // 
            // lblExclHint
            // 
            this.lblExclHint.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblExclHint.ForeColor = System.Drawing.Color.Gray;
            this.lblExclHint.Location = new System.Drawing.Point(1164, 134);
            this.lblExclHint.Name = "lblExclHint";
            this.lblExclHint.Size = new System.Drawing.Size(119, 32);
            this.lblExclHint.TabIndex = 12;
            this.lblExclHint.Text = "np. *.tmp;cache\\*";
            this.lblExclHint.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // lblRowAuto
            // 
            this.lblRowAuto.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblRowAuto.Location = new System.Drawing.Point(3, 166);
            this.lblRowAuto.Name = "lblRowAuto";
            this.lblRowAuto.Size = new System.Drawing.Size(114, 32);
            this.lblRowAuto.TabIndex = 13;
            this.lblRowAuto.Text = "Automatycznie:";
            this.lblRowAuto.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // autoPanel
            // 
            this.tlEdit.SetColumnSpan(this.autoPanel, 3);
            this.autoPanel.Controls.Add(this.chkInterval);
            this.autoPanel.Controls.Add(this.numInterval);
            this.autoPanel.Controls.Add(this.lblMinut);
            this.autoPanel.Controls.Add(this.chkWatch);
            this.autoPanel.Controls.Add(this.lblPrzez);
            this.autoPanel.Controls.Add(this.cmbWatchDelay);
            this.autoPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.autoPanel.Location = new System.Drawing.Point(120, 166);
            this.autoPanel.Margin = new System.Windows.Forms.Padding(0);
            this.autoPanel.Name = "autoPanel";
            this.autoPanel.Size = new System.Drawing.Size(1166, 32);
            this.autoPanel.TabIndex = 14;
            this.autoPanel.WrapContents = false;
            // 
            // chkInterval
            // 
            this.chkInterval.AutoSize = true;
            this.chkInterval.Location = new System.Drawing.Point(0, 6);
            this.chkInterval.Margin = new System.Windows.Forms.Padding(0, 6, 0, 0);
            this.chkInterval.Name = "chkInterval";
            this.chkInterval.Size = new System.Drawing.Size(41, 19);
            this.chkInterval.TabIndex = 0;
            this.chkInterval.Text = "Co";
            // 
            // numInterval
            // 
            this.numInterval.Location = new System.Drawing.Point(43, 3);
            this.numInterval.Margin = new System.Windows.Forms.Padding(2, 3, 2, 0);
            this.numInterval.Maximum = new decimal(new int[] {
            10080,
            0,
            0,
            0});
            this.numInterval.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.numInterval.Name = "numInterval";
            this.numInterval.Size = new System.Drawing.Size(70, 23);
            this.numInterval.TabIndex = 1;
            this.numInterval.Value = new decimal(new int[] {
            15,
            0,
            0,
            0});
            // 
            // lblMinut
            // 
            this.lblMinut.AutoSize = true;
            this.lblMinut.Location = new System.Drawing.Point(115, 7);
            this.lblMinut.Margin = new System.Windows.Forms.Padding(0, 7, 0, 0);
            this.lblMinut.Name = "lblMinut";
            this.lblMinut.Size = new System.Drawing.Size(39, 15);
            this.lblMinut.TabIndex = 2;
            this.lblMinut.Text = "minut";
            // 
            // chkWatch
            // 
            this.chkWatch.AutoSize = true;
            this.chkWatch.Location = new System.Drawing.Point(178, 6);
            this.chkWatch.Margin = new System.Windows.Forms.Padding(24, 6, 0, 0);
            this.chkWatch.Name = "chkWatch";
            this.chkWatch.Size = new System.Drawing.Size(220, 19);
            this.chkWatch.TabIndex = 3;
            this.chkWatch.Text = "Po wykryciu zmian w źródle, po ciszy";
            // 
            // lblPrzez
            // 
            this.lblPrzez.AutoSize = true;
            this.lblPrzez.Location = new System.Drawing.Point(404, 7);
            this.lblPrzez.Margin = new System.Windows.Forms.Padding(6, 7, 0, 0);
            this.lblPrzez.Name = "lblPrzez";
            this.lblPrzez.Size = new System.Drawing.Size(34, 15);
            this.lblPrzez.TabIndex = 4;
            this.lblPrzez.Text = "przez";
            // 
            // cmbWatchDelay
            // 
            this.cmbWatchDelay.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbWatchDelay.Location = new System.Drawing.Point(444, 3);
            this.cmbWatchDelay.Margin = new System.Windows.Forms.Padding(6, 3, 0, 0);
            this.cmbWatchDelay.Name = "cmbWatchDelay";
            this.cmbWatchDelay.Size = new System.Drawing.Size(100, 23);
            this.cmbWatchDelay.TabIndex = 5;
            // 
            // lblRowPusty
            // 
            this.lblRowPusty.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblRowPusty.Location = new System.Drawing.Point(3, 198);
            this.lblRowPusty.Name = "lblRowPusty";
            this.lblRowPusty.Size = new System.Drawing.Size(114, 50);
            this.lblRowPusty.TabIndex = 15;
            // 
            // savePanel
            // 
            this.savePanel.Controls.Add(this.btnSave);
            this.savePanel.Controls.Add(this.btnDiscard);
            this.savePanel.Controls.Add(this.linkStalaDecyzja);
            this.savePanel.Controls.Add(this.lblInfo);
            this.savePanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.savePanel.Location = new System.Drawing.Point(120, 198);
            this.savePanel.Margin = new System.Windows.Forms.Padding(0);
            this.savePanel.Name = "savePanel";
            this.savePanel.Size = new System.Drawing.Size(1001, 50);
            this.savePanel.TabIndex = 16;
            this.savePanel.WrapContents = false;
            // 
            // btnSave
            // 
            this.btnSave.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold);
            this.btnSave.Location = new System.Drawing.Point(3, 3);
            this.btnSave.Name = "btnSave";
            this.btnSave.Size = new System.Drawing.Size(100, 28);
            this.btnSave.TabIndex = 0;
            this.btnSave.Text = "💾 Zapisz";
            // 
            // btnDiscard
            // 
            this.btnDiscard.Location = new System.Drawing.Point(109, 3);
            this.btnDiscard.Name = "btnDiscard";
            this.btnDiscard.Size = new System.Drawing.Size(110, 28);
            this.btnDiscard.TabIndex = 1;
            this.btnDiscard.Text = "Odrzuć zmiany";
            // 
            // linkStalaDecyzja
            // 
            this.linkStalaDecyzja.AutoSize = true;
            this.linkStalaDecyzja.Location = new System.Drawing.Point(236, 8);
            this.linkStalaDecyzja.Margin = new System.Windows.Forms.Padding(14, 8, 0, 0);
            this.linkStalaDecyzja.Name = "linkStalaDecyzja";
            this.linkStalaDecyzja.Size = new System.Drawing.Size(0, 15);
            this.linkStalaDecyzja.TabIndex = 2;
            this.linkStalaDecyzja.Visible = false;
            // 
            // lblInfo
            // 
            this.lblInfo.AutoSize = true;
            this.lblInfo.ForeColor = System.Drawing.Color.DimGray;
            this.lblInfo.Location = new System.Drawing.Point(250, 8);
            this.lblInfo.Margin = new System.Windows.Forms.Padding(14, 8, 0, 0);
            this.lblInfo.Name = "lblInfo";
            this.lblInfo.Size = new System.Drawing.Size(0, 15);
            this.lblInfo.TabIndex = 3;
            // 
            // lblStatusBar
            // 
            this.lblStatusBar.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(10)))), ((int)(((byte)(90)))), ((int)(((byte)(190)))));
            this.lblStatusBar.Dock = System.Windows.Forms.DockStyle.Top;
            this.lblStatusBar.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold);
            this.lblStatusBar.ForeColor = System.Drawing.Color.White;
            this.lblStatusBar.Location = new System.Drawing.Point(6, 140);
            this.lblStatusBar.Name = "lblStatusBar";
            this.lblStatusBar.Size = new System.Drawing.Size(1286, 24);
            this.lblStatusBar.TabIndex = 1;
            this.lblStatusBar.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // folderBar
            // 
            this.folderBar.Controls.Add(this.fbTable);
            this.folderBar.Dock = System.Windows.Forms.DockStyle.Top;
            this.folderBar.GradientBottom = System.Drawing.Color.FromArgb(((int)(((byte)(90)))), ((int)(((byte)(170)))), ((int)(((byte)(235)))));
            this.folderBar.GradientTop = System.Drawing.Color.FromArgb(((int)(((byte)(150)))), ((int)(((byte)(210)))), ((int)(((byte)(250)))));
            this.folderBar.Location = new System.Drawing.Point(6, 22);
            this.folderBar.Name = "folderBar";
            this.folderBar.Size = new System.Drawing.Size(1286, 118);
            this.folderBar.TabIndex = 2;
            // 
            // fbTable
            // 
            this.fbTable.BackColor = System.Drawing.Color.Transparent;
            this.fbTable.ColumnCount = 3;
            this.fbTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50F));
            this.fbTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 210F));
            this.fbTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50F));
            this.fbTable.Controls.Add(this.panelLewy, 0, 0);
            this.fbTable.Controls.Add(this.centerPanel, 1, 0);
            this.fbTable.Controls.Add(this.panelPrawy, 2, 0);
            this.fbTable.Dock = System.Windows.Forms.DockStyle.Fill;
            this.fbTable.Location = new System.Drawing.Point(0, 0);
            this.fbTable.Name = "fbTable";
            this.fbTable.Padding = new System.Windows.Forms.Padding(4);
            this.fbTable.RowCount = 1;
            this.fbTable.Size = new System.Drawing.Size(1286, 118);
            this.fbTable.TabIndex = 0;
            // 
            // panelLewy
            // 
            this.panelLewy.BackColor = System.Drawing.Color.Transparent;
            this.panelLewy.ColumnCount = 3;
            this.panelLewy.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 52F));
            this.panelLewy.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.panelLewy.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 90F));
            this.panelLewy.Controls.Add(this.btnFolderLewy, 0, 0);
            this.panelLewy.Controls.Add(this.lblLewyTytul, 1, 0);
            this.panelLewy.Controls.Add(this.btnSprawdzLewy, 2, 0);
            this.panelLewy.Controls.Add(this.txtLeft, 1, 1);
            this.panelLewy.Controls.Add(this.cpuGraph, 1, 2);
            this.panelLewy.Dock = System.Windows.Forms.DockStyle.Fill;
            this.panelLewy.Location = new System.Drawing.Point(6, 6);
            this.panelLewy.Margin = new System.Windows.Forms.Padding(2);
            this.panelLewy.Name = "panelLewy";
            this.panelLewy.RowCount = 3;
            this.panelLewy.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 34F));
            this.panelLewy.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 30F));
            this.panelLewy.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.panelLewy.Size = new System.Drawing.Size(530, 106);
            this.panelLewy.TabIndex = 0;
            // 
            // btnFolderLewy
            // 
            this.btnFolderLewy.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(235)))), ((int)(((byte)(245)))), ((int)(((byte)(255)))));
            this.btnFolderLewy.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnFolderLewy.Dock = System.Windows.Forms.DockStyle.Fill;
            this.btnFolderLewy.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(70)))), ((int)(((byte)(130)))), ((int)(((byte)(200)))));
            this.btnFolderLewy.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnFolderLewy.Location = new System.Drawing.Point(0, 4);
            this.btnFolderLewy.Margin = new System.Windows.Forms.Padding(0, 4, 4, 0);
            this.btnFolderLewy.Name = "btnFolderLewy";
            this.panelLewy.SetRowSpan(this.btnFolderLewy, 2);
            this.btnFolderLewy.Size = new System.Drawing.Size(48, 60);
            this.btnFolderLewy.TabIndex = 0;
            this.btnFolderLewy.UseVisualStyleBackColor = false;
            // 
            // lblLewyTytul
            // 
            this.lblLewyTytul.AutoEllipsis = true;
            this.lblLewyTytul.BackColor = System.Drawing.Color.Transparent;
            this.lblLewyTytul.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblLewyTytul.Font = new System.Drawing.Font("Segoe UI", 11F);
            this.lblLewyTytul.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(35)))), ((int)(((byte)(90)))));
            this.lblLewyTytul.Location = new System.Drawing.Point(55, 0);
            this.lblLewyTytul.Name = "lblLewyTytul";
            this.lblLewyTytul.Size = new System.Drawing.Size(382, 34);
            this.lblLewyTytul.TabIndex = 1;
            this.lblLewyTytul.Text = "Folder LEWY";
            this.lblLewyTytul.TextAlign = System.Drawing.ContentAlignment.BottomLeft;
            // 
            // btnSprawdzLewy
            // 
            this.btnSprawdzLewy.Dock = System.Windows.Forms.DockStyle.Fill;
            this.btnSprawdzLewy.Location = new System.Drawing.Point(443, 6);
            this.btnSprawdzLewy.Margin = new System.Windows.Forms.Padding(3, 6, 0, 2);
            this.btnSprawdzLewy.Name = "btnSprawdzLewy";
            this.btnSprawdzLewy.Size = new System.Drawing.Size(87, 26);
            this.btnSprawdzLewy.TabIndex = 2;
            this.btnSprawdzLewy.Text = "Sprawdź";
            this.btnSprawdzLewy.UseVisualStyleBackColor = true;
            this.btnSprawdzLewy.Click += new System.EventHandler(this.btnSprawdzLewy_Click);
            // 
            // txtLeft
            // 
            this.panelLewy.SetColumnSpan(this.txtLeft, 2);
            this.txtLeft.Dock = System.Windows.Forms.DockStyle.Fill;
            this.txtLeft.Font = new System.Drawing.Font("Segoe UI", 10F);
            this.txtLeft.Location = new System.Drawing.Point(52, 38);
            this.txtLeft.Margin = new System.Windows.Forms.Padding(0, 4, 0, 0);
            this.txtLeft.Name = "txtLeft";
            this.txtLeft.Size = new System.Drawing.Size(478, 25);
            this.txtLeft.TabIndex = 3;
            // 
            // centerPanel
            // 
            this.centerPanel.BackColor = System.Drawing.Color.Transparent;
            this.centerPanel.ColumnCount = 1;
            this.centerPanel.Controls.Add(this.arrow, 0, 0);
            this.centerPanel.Controls.Add(this.lblDirMode, 0, 1);
            this.centerPanel.Controls.Add(this.lnkSwap, 0, 2);
            this.centerPanel.Controls.Add(this.cmbDir);
            this.centerPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.centerPanel.Location = new System.Drawing.Point(538, 4);
            this.centerPanel.Margin = new System.Windows.Forms.Padding(0);
            this.centerPanel.Name = "centerPanel";
            this.centerPanel.RowCount = 3;
            this.centerPanel.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.centerPanel.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 20F));
            this.centerPanel.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 20F));
            this.centerPanel.Size = new System.Drawing.Size(210, 110);
            this.centerPanel.TabIndex = 1;
            // 
            // arrow
            // 
            this.arrow.BackColor = System.Drawing.Color.Transparent;
            this.arrow.Cursor = System.Windows.Forms.Cursors.Hand;
            this.arrow.Dock = System.Windows.Forms.DockStyle.Fill;
            this.arrow.Font = new System.Drawing.Font("Segoe UI", 9.5F, ((System.Drawing.FontStyle)((System.Drawing.FontStyle.Bold | System.Drawing.FontStyle.Underline))));
            this.arrow.Location = new System.Drawing.Point(3, 3);
            this.arrow.Mode = 0;
            this.arrow.Name = "arrow";
            this.arrow.Size = new System.Drawing.Size(204, 64);
            this.arrow.TabIndex = 0;
            // 
            // lblDirMode
            // 
            this.lblDirMode.BackColor = System.Drawing.Color.Transparent;
            this.lblDirMode.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblDirMode.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold);
            this.lblDirMode.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(50)))), ((int)(((byte)(120)))));
            this.lblDirMode.Location = new System.Drawing.Point(3, 70);
            this.lblDirMode.Name = "lblDirMode";
            this.lblDirMode.Size = new System.Drawing.Size(204, 20);
            this.lblDirMode.TabIndex = 1;
            this.lblDirMode.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // lnkSwap
            // 
            this.lnkSwap.BackColor = System.Drawing.Color.Transparent;
            this.lnkSwap.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lnkSwap.LinkColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(50)))), ((int)(((byte)(120)))));
            this.lnkSwap.Location = new System.Drawing.Point(3, 90);
            this.lnkSwap.Name = "lnkSwap";
            this.lnkSwap.Size = new System.Drawing.Size(204, 20);
            this.lnkSwap.TabIndex = 2;
            this.lnkSwap.TabStop = true;
            this.lnkSwap.Text = "Zamień foldery miejscami";
            this.lnkSwap.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // cmbDir
            // 
            this.cmbDir.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbDir.Items.AddRange(new object[] {
            "Lewy → Prawy",
            "Prawy → Lewy",
            "Dwukierunkowo"});
            this.cmbDir.Location = new System.Drawing.Point(3, 113);
            this.cmbDir.Name = "cmbDir";
            this.cmbDir.Size = new System.Drawing.Size(121, 23);
            this.cmbDir.TabIndex = 3;
            this.cmbDir.Visible = false;
            // 
            // panelPrawy
            // 
            this.panelPrawy.BackColor = System.Drawing.Color.Transparent;
            this.panelPrawy.ColumnCount = 3;
            this.panelPrawy.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 52F));
            this.panelPrawy.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.panelPrawy.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 90F));
            this.panelPrawy.Controls.Add(this.btnFolderPrawy, 0, 0);
            this.panelPrawy.Controls.Add(this.lblPrawyTytul, 1, 0);
            this.panelPrawy.Controls.Add(this.btnSprawdzPrawy, 2, 0);
            this.panelPrawy.Controls.Add(this.txtRight, 1, 1);
            this.panelPrawy.Dock = System.Windows.Forms.DockStyle.Fill;
            this.panelPrawy.Location = new System.Drawing.Point(750, 6);
            this.panelPrawy.Margin = new System.Windows.Forms.Padding(2);
            this.panelPrawy.Name = "panelPrawy";
            this.panelPrawy.RowCount = 4;
            this.panelPrawy.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 34F));
            this.panelPrawy.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 30F));
            this.panelPrawy.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.panelPrawy.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 20F));
            this.panelPrawy.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 20F));
            this.panelPrawy.Size = new System.Drawing.Size(530, 106);
            this.panelPrawy.TabIndex = 2;
            // 
            // btnFolderPrawy
            // 
            this.btnFolderPrawy.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(235)))), ((int)(((byte)(245)))), ((int)(((byte)(255)))));
            this.btnFolderPrawy.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnFolderPrawy.Dock = System.Windows.Forms.DockStyle.Fill;
            this.btnFolderPrawy.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(70)))), ((int)(((byte)(130)))), ((int)(((byte)(200)))));
            this.btnFolderPrawy.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnFolderPrawy.Location = new System.Drawing.Point(0, 4);
            this.btnFolderPrawy.Margin = new System.Windows.Forms.Padding(0, 4, 4, 0);
            this.btnFolderPrawy.Name = "btnFolderPrawy";
            this.panelPrawy.SetRowSpan(this.btnFolderPrawy, 2);
            this.btnFolderPrawy.Size = new System.Drawing.Size(48, 60);
            this.btnFolderPrawy.TabIndex = 0;
            this.btnFolderPrawy.UseVisualStyleBackColor = false;
            // 
            // lblPrawyTytul
            // 
            this.lblPrawyTytul.AutoEllipsis = true;
            this.lblPrawyTytul.BackColor = System.Drawing.Color.Transparent;
            this.lblPrawyTytul.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblPrawyTytul.Font = new System.Drawing.Font("Segoe UI", 11F);
            this.lblPrawyTytul.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(35)))), ((int)(((byte)(90)))));
            this.lblPrawyTytul.Location = new System.Drawing.Point(55, 0);
            this.lblPrawyTytul.Name = "lblPrawyTytul";
            this.lblPrawyTytul.Size = new System.Drawing.Size(382, 34);
            this.lblPrawyTytul.TabIndex = 1;
            this.lblPrawyTytul.Text = "Folder PRAWY";
            this.lblPrawyTytul.TextAlign = System.Drawing.ContentAlignment.BottomLeft;
            // 
            // btnSprawdzPrawy
            // 
            this.btnSprawdzPrawy.Dock = System.Windows.Forms.DockStyle.Fill;
            this.btnSprawdzPrawy.Location = new System.Drawing.Point(443, 6);
            this.btnSprawdzPrawy.Margin = new System.Windows.Forms.Padding(3, 6, 0, 2);
            this.btnSprawdzPrawy.Name = "btnSprawdzPrawy";
            this.btnSprawdzPrawy.Size = new System.Drawing.Size(87, 26);
            this.btnSprawdzPrawy.TabIndex = 2;
            this.btnSprawdzPrawy.Text = "Sprawdź";
            this.btnSprawdzPrawy.UseVisualStyleBackColor = true;
            // 
            // txtRight
            // 
            this.panelPrawy.SetColumnSpan(this.txtRight, 2);
            this.txtRight.Dock = System.Windows.Forms.DockStyle.Fill;
            this.txtRight.Font = new System.Drawing.Font("Segoe UI", 10F);
            this.txtRight.Location = new System.Drawing.Point(52, 38);
            this.txtRight.Margin = new System.Windows.Forms.Padding(0, 4, 0, 0);
            this.txtRight.Name = "txtRight";
            this.txtRight.Size = new System.Drawing.Size(478, 25);
            this.txtRight.TabIndex = 3;
            // 
            // panelDecyzji
            // 
            this.panelDecyzji.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(255)))), ((int)(((byte)(244)))), ((int)(((byte)(205)))));
            this.panelDecyzji.Controls.Add(this.lblDecyzji);
            this.panelDecyzji.Controls.Add(this.btnDecyduj);
            this.panelDecyzji.Dock = System.Windows.Forms.DockStyle.Top;
            this.panelDecyzji.Location = new System.Drawing.Point(0, 60);
            this.panelDecyzji.Name = "panelDecyzji";
            this.panelDecyzji.Size = new System.Drawing.Size(1364, 0);
            this.panelDecyzji.TabIndex = 1;
            this.panelDecyzji.Visible = false;
            // 
            // lblDecyzji
            // 
            this.lblDecyzji.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblDecyzji.Font = new System.Drawing.Font("Segoe UI", 9.5F, System.Drawing.FontStyle.Bold);
            this.lblDecyzji.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(120)))), ((int)(((byte)(70)))), ((int)(((byte)(0)))));
            this.lblDecyzji.Location = new System.Drawing.Point(0, 0);
            this.lblDecyzji.Name = "lblDecyzji";
            this.lblDecyzji.Padding = new System.Windows.Forms.Padding(14, 0, 0, 0);
            this.lblDecyzji.Size = new System.Drawing.Size(1204, 0);
            this.lblDecyzji.TabIndex = 0;
            this.lblDecyzji.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // btnDecyduj
            // 
            this.btnDecyduj.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(255)))), ((int)(((byte)(210)))), ((int)(((byte)(120)))));
            this.btnDecyduj.Dock = System.Windows.Forms.DockStyle.Right;
            this.btnDecyduj.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnDecyduj.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold);
            this.btnDecyduj.Location = new System.Drawing.Point(1204, 0);
            this.btnDecyduj.Margin = new System.Windows.Forms.Padding(0);
            this.btnDecyduj.Name = "btnDecyduj";
            this.btnDecyduj.Size = new System.Drawing.Size(160, 0);
            this.btnDecyduj.TabIndex = 1;
            this.btnDecyduj.Text = "Zdecyduj teraz";
            this.btnDecyduj.UseVisualStyleBackColor = false;
            // 
            // headerPanel
            // 
            this.headerPanel.Dock = System.Windows.Forms.DockStyle.Top;
            this.headerPanel.GradientBottom = System.Drawing.Color.FromArgb(((int)(((byte)(5)))), ((int)(((byte)(60)))), ((int)(((byte)(160)))));
            this.headerPanel.GradientTop = System.Drawing.Color.FromArgb(((int)(((byte)(20)))), ((int)(((byte)(120)))), ((int)(((byte)(220)))));
            this.headerPanel.Location = new System.Drawing.Point(0, 0);
            this.headerPanel.Name = "headerPanel";
            this.headerPanel.Size = new System.Drawing.Size(1364, 60);
            this.headerPanel.TabIndex = 2;
            // 
            // MainForm
            // 
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
            this.ClientSize = new System.Drawing.Size(1364, 841);
            this.Controls.Add(this.splitMain);
            this.Controls.Add(this.panelDecyzji);
            this.Controls.Add(this.headerPanel);
            this.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.MinimumSize = new System.Drawing.Size(900, 640);
            this.Name = "MainForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "Echo Sync";
            this.splitMain.Panel1.ResumeLayout(false);
            this.splitMain.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.splitMain)).EndInit();
            this.splitMain.ResumeLayout(false);
            this.taskBtns.ResumeLayout(false);
            this.searchTasksPanel.ResumeLayout(false);
            this.searchTasksPanel.PerformLayout();
            this.tabs.ResumeLayout(false);
            this.tabPrev.ResumeLayout(false);
            this.lvMenu.ResumeLayout(false);
            this.bottomBar.ResumeLayout(false);
            this.searchPanel.ResumeLayout(false);
            this.searchPanel.PerformLayout();
            this.tabLog.ResumeLayout(false);
            this.progPanel.ResumeLayout(false);
            this.statusRow.ResumeLayout(false);
            this.runBtns.ResumeLayout(false);
            this.runBtns.PerformLayout();
            this.grpEdit.ResumeLayout(false);
            this.tlEdit.ResumeLayout(false);
            this.tlEdit.PerformLayout();
            this.subPanel.ResumeLayout(false);
            this.subPanel.PerformLayout();
            this.usuwaniePanel.ResumeLayout(false);
            this.usuwaniePanel.PerformLayout();
            this.autoPanel.ResumeLayout(false);
            this.autoPanel.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numInterval)).EndInit();
            this.savePanel.ResumeLayout(false);
            this.savePanel.PerformLayout();
            this.folderBar.ResumeLayout(false);
            this.fbTable.ResumeLayout(false);
            this.panelLewy.ResumeLayout(false);
            this.panelLewy.PerformLayout();
            this.centerPanel.ResumeLayout(false);
            this.panelPrawy.ResumeLayout(false);
            this.panelPrawy.PerformLayout();
            this.panelDecyzji.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.SplitContainer splitMain;
        private System.Windows.Forms.FlowLayoutPanel taskBtns;
        private System.Windows.Forms.Button btnNoweZadanie;
        private System.Windows.Forms.Button btnDuplikuj;
        private System.Windows.Forms.Button btnUsunZadanie;
        private System.Windows.Forms.Button btnTaskUp;
        private System.Windows.Forms.Button btnTaskDown;
        private System.Windows.Forms.Button btnArchiwum;
        private System.Windows.Forms.Button btnRobo;
        private System.Windows.Forms.FlowLayoutPanel searchTasksPanel;
        private System.Windows.Forms.Label lblSearchTasksIcon;
        private System.Windows.Forms.Label lblTasksHeader;
        private System.Windows.Forms.TabPage tabPrev;
        private System.Windows.Forms.TabPage tabLog;
        private System.Windows.Forms.ColumnHeader colAkcja;
        private System.Windows.Forms.ColumnHeader colSciezka;
        private System.Windows.Forms.ColumnHeader colRozmiar;
        private System.Windows.Forms.ColumnHeader colDataZrodlo;
        private System.Windows.Forms.ColumnHeader colDataCel;
        private System.Windows.Forms.ColumnHeader colPowod;
        private System.Windows.Forms.ContextMenuStrip lvMenu;
        private System.Windows.Forms.ToolStripMenuItem miExpandAll;
        private System.Windows.Forms.ToolStripMenuItem miCollapseAll;
        private System.Windows.Forms.Panel bottomBar;
        private System.Windows.Forms.FlowLayoutPanel searchPanel;
        private System.Windows.Forms.Label lblSearchIcon;
        private System.Windows.Forms.Button btnSearchTree;
        private System.Windows.Forms.Panel progPanel;
        private System.Windows.Forms.Panel statusRow;
        private System.Windows.Forms.FlowLayoutPanel runBtns;
        private System.Windows.Forms.TableLayoutPanel tlEdit;
        private System.Windows.Forms.Label lblRowNazwa;
        private System.Windows.Forms.Label lblRowPodfolder;
        private System.Windows.Forms.Label lblRowTryb;
        private System.Windows.Forms.Label lblRowUsuwane;
        private System.Windows.Forms.Label lblRowWyklucz;
        private System.Windows.Forms.Label lblRowAuto;
        private System.Windows.Forms.Label lblRowPusty;
        private System.Windows.Forms.Label lblExclHint;
        private System.Windows.Forms.Label lblMinut;
        private System.Windows.Forms.Label lblPrzez;
        private System.Windows.Forms.FlowLayoutPanel subPanel;
        private System.Windows.Forms.FlowLayoutPanel usuwaniePanel;
        private System.Windows.Forms.FlowLayoutPanel autoPanel;
        private System.Windows.Forms.FlowLayoutPanel savePanel;
        private MojSync.GradientPanel folderBar;
        private System.Windows.Forms.TableLayoutPanel fbTable;
        private System.Windows.Forms.TableLayoutPanel panelLewy;
        private System.Windows.Forms.Button btnFolderLewy;
        private System.Windows.Forms.Button btnSprawdzLewy;
        private System.Windows.Forms.TableLayoutPanel panelPrawy;
        private System.Windows.Forms.Button btnFolderPrawy;
        private System.Windows.Forms.Button btnSprawdzPrawy;
        private System.Windows.Forms.TableLayoutPanel centerPanel;
        private System.Windows.Forms.LinkLabel lnkSwap;
        private MojSync.MainForm.CpuGraph cpuGraph;
    }
}
