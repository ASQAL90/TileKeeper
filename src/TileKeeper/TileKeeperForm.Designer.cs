namespace TileKeeper
{
    partial class TileKeeperForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.tabMain = new System.Windows.Forms.TabControl();
            this.tabPrograms = new System.Windows.Forms.TabPage();
            this.lblPgmHint = new System.Windows.Forms.Label();
            this.btnRemove = new System.Windows.Forms.Button();
            this.btnEdit = new System.Windows.Forms.Button();
            this.btnAdd = new System.Windows.Forms.Button();
            this.listPrograms = new System.Windows.Forms.ListView();
            this.txtCycle = new System.Windows.Forms.TextBox();
            this.lblDelay = new System.Windows.Forms.Label();
            this.chkEnablePersist = new System.Windows.Forms.CheckBox();
            this.tabPosition = new System.Windows.Forms.TabPage();
            this.lblPosHint = new System.Windows.Forms.Label();
            this.txtCycleScreen = new System.Windows.Forms.TextBox();
            this.lblCycle = new System.Windows.Forms.Label();
            this.chkEnableCustPos = new System.Windows.Forms.CheckBox();
            this.grpPositionList = new System.Windows.Forms.GroupBox();
            this.listPositionPrograms = new System.Windows.Forms.ListView();
            this.btnAddPosition = new System.Windows.Forms.Button();
            this.btnEditPosition = new System.Windows.Forms.Button();
            this.btnRemovePosition = new System.Windows.Forms.Button();
            this.grpScreen1 = new System.Windows.Forms.GroupBox();
            this.checkedScreen1 = new TileKeeper.ColoredCheckedListBox();
            this.lblGridRows1 = new System.Windows.Forms.Label();
            this.txtGridRows1 = new System.Windows.Forms.TextBox();
            this.lblGridCols1 = new System.Windows.Forms.Label();
            this.txtGridCols1 = new System.Windows.Forms.TextBox();
            this.chkAutoFit1 = new System.Windows.Forms.CheckBox();
            this.chkBorderless1 = new System.Windows.Forms.CheckBox();
            this.gridMap1 = new TileKeeper.Class.GridMapPanel();
            this.grpScreen2 = new System.Windows.Forms.GroupBox();
            this.checkedScreen2 = new TileKeeper.ColoredCheckedListBox();
            this.lblGridRows2 = new System.Windows.Forms.Label();
            this.txtGridRows2 = new System.Windows.Forms.TextBox();
            this.lblGridCols2 = new System.Windows.Forms.Label();
            this.txtGridCols2 = new System.Windows.Forms.TextBox();
            this.chkAutoFit2 = new System.Windows.Forms.CheckBox();
            this.chkBorderless2 = new System.Windows.Forms.CheckBox();
            this.gridMap2 = new TileKeeper.Class.GridMapPanel();
            this.grpScreen3 = new System.Windows.Forms.GroupBox();
            this.checkedScreen3 = new TileKeeper.ColoredCheckedListBox();
            this.lblGridRows3 = new System.Windows.Forms.Label();
            this.txtGridRows3 = new System.Windows.Forms.TextBox();
            this.lblGridCols3 = new System.Windows.Forms.Label();
            this.txtGridCols3 = new System.Windows.Forms.TextBox();
            this.chkAutoFit3 = new System.Windows.Forms.CheckBox();
            this.chkBorderless3 = new System.Windows.Forms.CheckBox();
            this.gridMap3 = new TileKeeper.Class.GridMapPanel();
            this.grpScreen4 = new System.Windows.Forms.GroupBox();
            this.checkedScreen4 = new TileKeeper.ColoredCheckedListBox();
            this.lblGridRows4 = new System.Windows.Forms.Label();
            this.txtGridRows4 = new System.Windows.Forms.TextBox();
            this.lblGridCols4 = new System.Windows.Forms.Label();
            this.txtGridCols4 = new System.Windows.Forms.TextBox();
            this.chkAutoFit4 = new System.Windows.Forms.CheckBox();
            this.chkBorderless4 = new System.Windows.Forms.CheckBox();
            this.gridMap4 = new TileKeeper.Class.GridMapPanel();
            this.tabSystem = new System.Windows.Forms.TabPage();
            this.btnAbout = new System.Windows.Forms.Button();
            this.grpResolution = new System.Windows.Forms.GroupBox();
            this.lblResStatus = new System.Windows.Forms.Label();
            this.btnCaptureRes = new System.Windows.Forms.Button();
            this.txtResCycle = new System.Windows.Forms.TextBox();
            this.lblResCycle = new System.Windows.Forms.Label();
            this.txtResHeight = new System.Windows.Forms.TextBox();
            this.lblResHeight = new System.Windows.Forms.Label();
            this.txtResWidth = new System.Windows.Forms.TextBox();
            this.lblResWidth = new System.Windows.Forms.Label();
            this.chkEnableRes = new System.Windows.Forms.CheckBox();
            this.btnClear = new System.Windows.Forms.Button();
            this.btnSave = new System.Windows.Forms.Button();
            this.tabMain.SuspendLayout();
            this.tabPrograms.SuspendLayout();
            this.tabPosition.SuspendLayout();
            this.grpPositionList.SuspendLayout();
            this.grpScreen1.SuspendLayout();
            this.grpScreen2.SuspendLayout();
            this.grpScreen3.SuspendLayout();
            this.grpScreen4.SuspendLayout();
            this.tabSystem.SuspendLayout();
            this.grpResolution.SuspendLayout();
            this.SuspendLayout();
            // 
            // tabMain
            // 
            this.tabMain.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.tabMain.Controls.Add(this.tabPrograms);
            this.tabMain.Controls.Add(this.tabPosition);
            this.tabMain.Controls.Add(this.tabSystem);
            this.tabMain.Location = new System.Drawing.Point(9, 10);
            this.tabMain.Margin = new System.Windows.Forms.Padding(2);
            this.tabMain.Name = "tabMain";
            this.tabMain.SelectedIndex = 0;
            this.tabMain.Size = new System.Drawing.Size(477, 606);
            this.tabMain.TabIndex = 0;
            // 
            // tabPrograms
            // 
            this.tabPrograms.Controls.Add(this.lblPgmHint);
            this.tabPrograms.Controls.Add(this.btnRemove);
            this.tabPrograms.Controls.Add(this.btnEdit);
            this.tabPrograms.Controls.Add(this.btnAdd);
            this.tabPrograms.Controls.Add(this.listPrograms);
            this.tabPrograms.Controls.Add(this.txtCycle);
            this.tabPrograms.Controls.Add(this.lblDelay);
            this.tabPrograms.Controls.Add(this.chkEnablePersist);
            this.tabPrograms.Location = new System.Drawing.Point(4, 22);
            this.tabPrograms.Margin = new System.Windows.Forms.Padding(2);
            this.tabPrograms.Name = "tabPrograms";
            this.tabPrograms.Padding = new System.Windows.Forms.Padding(2);
            this.tabPrograms.Size = new System.Drawing.Size(469, 580);
            this.tabPrograms.TabIndex = 0;
            this.tabPrograms.Text = "Programs";
            this.tabPrograms.UseVisualStyleBackColor = true;
            // 
            // lblPgmHint
            // 
            this.lblPgmHint.Location = new System.Drawing.Point(9, 545);
            this.lblPgmHint.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.lblPgmHint.Name = "lblPgmHint";
            this.lblPgmHint.Size = new System.Drawing.Size(450, 41);
            this.lblPgmHint.TabIndex = 9;
            this.lblPgmHint.Text = "Add: pick executables, shortcuts, ClickOnce (.appref-ms/.lnk) or .rdp files.\r\nEac" +
    "h row = one program (first column is used to match the running window).";
            // 
            // btnRemove
            // 
            this.btnRemove.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(181)))), ((int)(((byte)(0)))), ((int)(((byte)(0)))));
            this.btnRemove.ForeColor = System.Drawing.Color.White;
            this.btnRemove.Location = new System.Drawing.Point(137, 512);
            this.btnRemove.Margin = new System.Windows.Forms.Padding(2);
            this.btnRemove.Name = "btnRemove";
            this.btnRemove.Size = new System.Drawing.Size(62, 23);
            this.btnRemove.TabIndex = 6;
            this.btnRemove.Text = "Remove";
            this.btnRemove.UseVisualStyleBackColor = false;
            this.btnRemove.Click += new System.EventHandler(this.BtnRemove_Click);
            // 
            // btnEdit
            // 
            this.btnEdit.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(87)))), ((int)(((byte)(184)))));
            this.btnEdit.ForeColor = System.Drawing.Color.White;
            this.btnEdit.Location = new System.Drawing.Point(78, 512);
            this.btnEdit.Margin = new System.Windows.Forms.Padding(2);
            this.btnEdit.Name = "btnEdit";
            this.btnEdit.Size = new System.Drawing.Size(54, 23);
            this.btnEdit.TabIndex = 5;
            this.btnEdit.Text = "Edit...";
            this.btnEdit.UseVisualStyleBackColor = false;
            this.btnEdit.Click += new System.EventHandler(this.BtnEdit_Click);
            // 
            // btnAdd
            // 
            this.btnAdd.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(87)))), ((int)(((byte)(184)))));
            this.btnAdd.ForeColor = System.Drawing.Color.White;
            this.btnAdd.Location = new System.Drawing.Point(6, 512);
            this.btnAdd.Margin = new System.Windows.Forms.Padding(2);
            this.btnAdd.Name = "btnAdd";
            this.btnAdd.Size = new System.Drawing.Size(68, 23);
            this.btnAdd.TabIndex = 4;
            this.btnAdd.Text = "Add...";
            this.btnAdd.UseVisualStyleBackColor = false;
            this.btnAdd.Click += new System.EventHandler(this.BtnAdd_Click);
            // 
            // listPrograms
            // 
            this.listPrograms.FullRowSelect = true;
            this.listPrograms.GridLines = true;
            this.listPrograms.HideSelection = false;
            this.listPrograms.Location = new System.Drawing.Point(9, 37);
            this.listPrograms.Margin = new System.Windows.Forms.Padding(2);
            this.listPrograms.Name = "listPrograms";
            this.listPrograms.Size = new System.Drawing.Size(451, 471);
            this.listPrograms.TabIndex = 3;
            this.listPrograms.UseCompatibleStateImageBehavior = false;
            this.listPrograms.View = System.Windows.Forms.View.Details;
            // 
            // txtCycle
            // 
            this.txtCycle.Location = new System.Drawing.Point(264, 10);
            this.txtCycle.Margin = new System.Windows.Forms.Padding(2);
            this.txtCycle.Name = "txtCycle";
            this.txtCycle.Size = new System.Drawing.Size(46, 20);
            this.txtCycle.TabIndex = 2;
            // 
            // lblDelay
            // 
            this.lblDelay.AutoSize = true;
            this.lblDelay.Location = new System.Drawing.Point(172, 13);
            this.lblDelay.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.lblDelay.Name = "lblDelay";
            this.lblDelay.Size = new System.Drawing.Size(88, 13);
            this.lblDelay.TabIndex = 1;
            this.lblDelay.Text = "Cycle (seconds) :";
            // 
            // chkEnablePersist
            // 
            this.chkEnablePersist.AutoSize = true;
            this.chkEnablePersist.Location = new System.Drawing.Point(9, 12);
            this.chkEnablePersist.Margin = new System.Windows.Forms.Padding(2);
            this.chkEnablePersist.Name = "chkEnablePersist";
            this.chkEnablePersist.Size = new System.Drawing.Size(130, 17);
            this.chkEnablePersist.TabIndex = 0;
            this.chkEnablePersist.Text = "Enable Keep Running";
            this.chkEnablePersist.UseVisualStyleBackColor = true;
            // 
            // tabPosition
            // 
            this.tabPosition.Controls.Add(this.lblPosHint);
            this.tabPosition.Controls.Add(this.txtCycleScreen);
            this.tabPosition.Controls.Add(this.lblCycle);
            this.tabPosition.Controls.Add(this.chkEnableCustPos);
            this.tabPosition.Controls.Add(this.grpPositionList);
            this.tabPosition.Controls.Add(this.grpScreen1);
            this.tabPosition.Controls.Add(this.grpScreen2);
            this.tabPosition.Controls.Add(this.grpScreen3);
            this.tabPosition.Controls.Add(this.grpScreen4);
            this.tabPosition.Location = new System.Drawing.Point(4, 22);
            this.tabPosition.Margin = new System.Windows.Forms.Padding(2);
            this.tabPosition.Name = "tabPosition";
            this.tabPosition.Padding = new System.Windows.Forms.Padding(2);
            this.tabPosition.Size = new System.Drawing.Size(469, 580);
            this.tabPosition.TabIndex = 1;
            this.tabPosition.Text = "Position";
            this.tabPosition.UseVisualStyleBackColor = true;
            // 
            // lblPosHint
            // 
            this.lblPosHint.Location = new System.Drawing.Point(9, 545);
            this.lblPosHint.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.lblPosHint.Name = "lblPosHint";
            this.lblPosHint.Size = new System.Drawing.Size(453, 34);
            this.lblPosHint.TabIndex = 19;
            this.lblPosHint.Text = "Check the programs assigned to each screen, then click a grid cell to pin that pr" +
    "ogram to that segment (up to 3x3 = 9 cells).  Unpinned programs auto-fill in lis" +
    "t order.";
            // 
            // txtCycleScreen
            // 
            this.txtCycleScreen.Location = new System.Drawing.Point(264, 8);
            this.txtCycleScreen.Margin = new System.Windows.Forms.Padding(2);
            this.txtCycleScreen.Name = "txtCycleScreen";
            this.txtCycleScreen.Size = new System.Drawing.Size(46, 20);
            this.txtCycleScreen.TabIndex = 2;
            // 
            // lblCycle
            // 
            this.lblCycle.AutoSize = true;
            this.lblCycle.Location = new System.Drawing.Point(172, 11);
            this.lblCycle.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.lblCycle.Name = "lblCycle";
            this.lblCycle.Size = new System.Drawing.Size(88, 13);
            this.lblCycle.TabIndex = 1;
            this.lblCycle.Text = "Cycle (seconds) :";
            // 
            // chkEnableCustPos
            // 
            this.chkEnableCustPos.AutoSize = true;
            this.chkEnableCustPos.Location = new System.Drawing.Point(9, 10);
            this.chkEnableCustPos.Margin = new System.Windows.Forms.Padding(2);
            this.chkEnableCustPos.Name = "chkEnableCustPos";
            this.chkEnableCustPos.Size = new System.Drawing.Size(137, 17);
            this.chkEnableCustPos.TabIndex = 0;
            this.chkEnableCustPos.Text = "Enable Custom Position";
            this.chkEnableCustPos.UseVisualStyleBackColor = true;
            // 
            // grpPositionList
            // 
            this.grpPositionList.Controls.Add(this.listPositionPrograms);
            this.grpPositionList.Controls.Add(this.btnAddPosition);
            this.grpPositionList.Controls.Add(this.btnEditPosition);
            this.grpPositionList.Controls.Add(this.btnRemovePosition);
            this.grpPositionList.Location = new System.Drawing.Point(9, 34);
            this.grpPositionList.Margin = new System.Windows.Forms.Padding(2);
            this.grpPositionList.Name = "grpPositionList";
            this.grpPositionList.Padding = new System.Windows.Forms.Padding(2);
            this.grpPositionList.Size = new System.Drawing.Size(453, 130);
            this.grpPositionList.TabIndex = 3;
            this.grpPositionList.TabStop = false;
            this.grpPositionList.Text = "Programs to Position (added here are not launched by Keep Running)";
            // 
            // listPositionPrograms
            // 
            this.listPositionPrograms.FullRowSelect = true;
            this.listPositionPrograms.GridLines = true;
            this.listPositionPrograms.HideSelection = false;
            this.listPositionPrograms.Location = new System.Drawing.Point(9, 16);
            this.listPositionPrograms.Margin = new System.Windows.Forms.Padding(2);
            this.listPositionPrograms.Name = "listPositionPrograms";
            this.listPositionPrograms.Size = new System.Drawing.Size(435, 78);
            this.listPositionPrograms.TabIndex = 0;
            this.listPositionPrograms.UseCompatibleStateImageBehavior = false;
            this.listPositionPrograms.View = System.Windows.Forms.View.Details;
            // 
            // btnAddPosition
            // 
            this.btnAddPosition.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(87)))), ((int)(((byte)(184)))));
            this.btnAddPosition.ForeColor = System.Drawing.Color.White;
            this.btnAddPosition.Location = new System.Drawing.Point(9, 100);
            this.btnAddPosition.Margin = new System.Windows.Forms.Padding(2);
            this.btnAddPosition.Name = "btnAddPosition";
            this.btnAddPosition.Size = new System.Drawing.Size(60, 23);
            this.btnAddPosition.TabIndex = 1;
            this.btnAddPosition.Text = "Add...";
            this.btnAddPosition.UseVisualStyleBackColor = false;
            this.btnAddPosition.Click += new System.EventHandler(this.BtnAddPosition_Click);
            // 
            // btnEditPosition
            // 
            this.btnEditPosition.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(87)))), ((int)(((byte)(184)))));
            this.btnEditPosition.ForeColor = System.Drawing.Color.White;
            this.btnEditPosition.Location = new System.Drawing.Point(73, 100);
            this.btnEditPosition.Margin = new System.Windows.Forms.Padding(2);
            this.btnEditPosition.Name = "btnEditPosition";
            this.btnEditPosition.Size = new System.Drawing.Size(52, 23);
            this.btnEditPosition.TabIndex = 2;
            this.btnEditPosition.Text = "Edit...";
            this.btnEditPosition.UseVisualStyleBackColor = false;
            this.btnEditPosition.Click += new System.EventHandler(this.BtnEditPosition_Click);
            // 
            // btnRemovePosition
            // 
            this.btnRemovePosition.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(181)))), ((int)(((byte)(0)))), ((int)(((byte)(0)))));
            this.btnRemovePosition.ForeColor = System.Drawing.Color.White;
            this.btnRemovePosition.Location = new System.Drawing.Point(129, 100);
            this.btnRemovePosition.Margin = new System.Windows.Forms.Padding(2);
            this.btnRemovePosition.Name = "btnRemovePosition";
            this.btnRemovePosition.Size = new System.Drawing.Size(62, 23);
            this.btnRemovePosition.TabIndex = 3;
            this.btnRemovePosition.Text = "Remove";
            this.btnRemovePosition.UseVisualStyleBackColor = false;
            this.btnRemovePosition.Click += new System.EventHandler(this.BtnRemovePosition_Click);
            // 
            // grpScreen1
            // 
            this.grpScreen1.Controls.Add(this.checkedScreen1);
            this.grpScreen1.Controls.Add(this.lblGridRows1);
            this.grpScreen1.Controls.Add(this.txtGridRows1);
            this.grpScreen1.Controls.Add(this.lblGridCols1);
            this.grpScreen1.Controls.Add(this.txtGridCols1);
            this.grpScreen1.Controls.Add(this.chkAutoFit1);
            this.grpScreen1.Controls.Add(this.chkBorderless1);
            this.grpScreen1.Controls.Add(this.gridMap1);
            this.grpScreen1.Location = new System.Drawing.Point(9, 168);
            this.grpScreen1.Margin = new System.Windows.Forms.Padding(2);
            this.grpScreen1.Name = "grpScreen1";
            this.grpScreen1.Padding = new System.Windows.Forms.Padding(2);
            this.grpScreen1.Size = new System.Drawing.Size(222, 186);
            this.grpScreen1.TabIndex = 4;
            this.grpScreen1.TabStop = false;
            this.grpScreen1.Text = "Screen 1";
            // 
            // checkedScreen1
            // 
            this.checkedScreen1.CheckOnClick = true;
            this.checkedScreen1.ColorOf = null;
            this.checkedScreen1.HorizontalScrollbar = true;
            this.checkedScreen1.Location = new System.Drawing.Point(6, 18);
            this.checkedScreen1.Margin = new System.Windows.Forms.Padding(2);
            this.checkedScreen1.Name = "checkedScreen1";
            this.checkedScreen1.Size = new System.Drawing.Size(120, 109);
            this.checkedScreen1.TabIndex = 4;
            // 
            // lblGridRows1
            // 
            this.lblGridRows1.AutoSize = true;
            this.lblGridRows1.Location = new System.Drawing.Point(130, 20);
            this.lblGridRows1.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.lblGridRows1.Name = "lblGridRows1";
            this.lblGridRows1.Size = new System.Drawing.Size(40, 13);
            this.lblGridRows1.TabIndex = 0;
            this.lblGridRows1.Text = "Rows :";
            // 
            // txtGridRows1
            // 
            this.txtGridRows1.Location = new System.Drawing.Point(172, 18);
            this.txtGridRows1.Margin = new System.Windows.Forms.Padding(2);
            this.txtGridRows1.Name = "txtGridRows1";
            this.txtGridRows1.Size = new System.Drawing.Size(28, 20);
            this.txtGridRows1.TabIndex = 1;
            this.txtGridRows1.Text = "0";
            this.txtGridRows1.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            // 
            // lblGridCols1
            // 
            this.lblGridCols1.AutoSize = true;
            this.lblGridCols1.Location = new System.Drawing.Point(130, 44);
            this.lblGridCols1.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.lblGridCols1.Name = "lblGridCols1";
            this.lblGridCols1.Size = new System.Drawing.Size(33, 13);
            this.lblGridCols1.TabIndex = 2;
            this.lblGridCols1.Text = "Cols :";
            // 
            // txtGridCols1
            // 
            this.txtGridCols1.Location = new System.Drawing.Point(172, 42);
            this.txtGridCols1.Margin = new System.Windows.Forms.Padding(2);
            this.txtGridCols1.Name = "txtGridCols1";
            this.txtGridCols1.Size = new System.Drawing.Size(28, 20);
            this.txtGridCols1.TabIndex = 3;
            this.txtGridCols1.Text = "0";
            this.txtGridCols1.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            // 
            // chkAutoFit1
            // 
            this.chkAutoFit1.AutoSize = true;
            this.chkAutoFit1.Location = new System.Drawing.Point(130, 66);
            this.chkAutoFit1.Margin = new System.Windows.Forms.Padding(2);
            this.chkAutoFit1.Name = "chkAutoFit1";
            this.chkAutoFit1.Size = new System.Drawing.Size(59, 17);
            this.chkAutoFit1.TabIndex = 5;
            this.chkAutoFit1.Text = "AutoFit";
            this.chkAutoFit1.UseVisualStyleBackColor = true;
            // 
            // chkBorderless1
            // 
            this.chkBorderless1.AutoSize = true;
            this.chkBorderless1.Checked = true;
            this.chkBorderless1.CheckState = System.Windows.Forms.CheckState.Checked;
            this.chkBorderless1.Location = new System.Drawing.Point(130, 85);
            this.chkBorderless1.Margin = new System.Windows.Forms.Padding(2);
            this.chkBorderless1.Name = "chkBorderless1";
            this.chkBorderless1.Size = new System.Drawing.Size(75, 17);
            this.chkBorderless1.TabIndex = 6;
            this.chkBorderless1.Text = "Borderless";
            this.chkBorderless1.UseVisualStyleBackColor = true;
            this.chkBorderless1.CheckedChanged += new System.EventHandler(this.ChkBorderless_CheckedChanged);
            // 
            // gridMap1
            // 
            this.gridMap1.BackColor = System.Drawing.SystemColors.Control;
            this.gridMap1.Location = new System.Drawing.Point(130, 106);
            this.gridMap1.Margin = new System.Windows.Forms.Padding(2);
            this.gridMap1.Name = "gridMap1";
            this.gridMap1.Size = new System.Drawing.Size(86, 66);
            this.gridMap1.TabIndex = 7;
            this.gridMap1.Text = "gridMap1";
            this.gridMap1.CellClicked += new System.Action<int>(this.GridMap1_CellClicked);
            // 
            // grpScreen2
            // 
            this.grpScreen2.Controls.Add(this.checkedScreen2);
            this.grpScreen2.Controls.Add(this.lblGridRows2);
            this.grpScreen2.Controls.Add(this.txtGridRows2);
            this.grpScreen2.Controls.Add(this.lblGridCols2);
            this.grpScreen2.Controls.Add(this.txtGridCols2);
            this.grpScreen2.Controls.Add(this.chkAutoFit2);
            this.grpScreen2.Controls.Add(this.chkBorderless2);
            this.grpScreen2.Controls.Add(this.gridMap2);
            this.grpScreen2.Location = new System.Drawing.Point(235, 168);
            this.grpScreen2.Margin = new System.Windows.Forms.Padding(2);
            this.grpScreen2.Name = "grpScreen2";
            this.grpScreen2.Padding = new System.Windows.Forms.Padding(2);
            this.grpScreen2.Size = new System.Drawing.Size(222, 186);
            this.grpScreen2.TabIndex = 5;
            this.grpScreen2.TabStop = false;
            this.grpScreen2.Text = "Screen 2";
            // 
            // checkedScreen2
            // 
            this.checkedScreen2.CheckOnClick = true;
            this.checkedScreen2.ColorOf = null;
            this.checkedScreen2.HorizontalScrollbar = true;
            this.checkedScreen2.Location = new System.Drawing.Point(6, 18);
            this.checkedScreen2.Margin = new System.Windows.Forms.Padding(2);
            this.checkedScreen2.Name = "checkedScreen2";
            this.checkedScreen2.Size = new System.Drawing.Size(120, 109);
            this.checkedScreen2.TabIndex = 4;
            // 
            // lblGridRows2
            // 
            this.lblGridRows2.AutoSize = true;
            this.lblGridRows2.Location = new System.Drawing.Point(130, 20);
            this.lblGridRows2.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.lblGridRows2.Name = "lblGridRows2";
            this.lblGridRows2.Size = new System.Drawing.Size(40, 13);
            this.lblGridRows2.TabIndex = 0;
            this.lblGridRows2.Text = "Rows :";
            // 
            // txtGridRows2
            // 
            this.txtGridRows2.Location = new System.Drawing.Point(172, 18);
            this.txtGridRows2.Margin = new System.Windows.Forms.Padding(2);
            this.txtGridRows2.Name = "txtGridRows2";
            this.txtGridRows2.Size = new System.Drawing.Size(28, 20);
            this.txtGridRows2.TabIndex = 1;
            this.txtGridRows2.Text = "0";
            this.txtGridRows2.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            // 
            // lblGridCols2
            // 
            this.lblGridCols2.AutoSize = true;
            this.lblGridCols2.Location = new System.Drawing.Point(130, 44);
            this.lblGridCols2.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.lblGridCols2.Name = "lblGridCols2";
            this.lblGridCols2.Size = new System.Drawing.Size(33, 13);
            this.lblGridCols2.TabIndex = 2;
            this.lblGridCols2.Text = "Cols :";
            // 
            // txtGridCols2
            // 
            this.txtGridCols2.Location = new System.Drawing.Point(172, 42);
            this.txtGridCols2.Margin = new System.Windows.Forms.Padding(2);
            this.txtGridCols2.Name = "txtGridCols2";
            this.txtGridCols2.Size = new System.Drawing.Size(28, 20);
            this.txtGridCols2.TabIndex = 3;
            this.txtGridCols2.Text = "0";
            this.txtGridCols2.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            // 
            // chkAutoFit2
            // 
            this.chkAutoFit2.AutoSize = true;
            this.chkAutoFit2.Location = new System.Drawing.Point(130, 66);
            this.chkAutoFit2.Margin = new System.Windows.Forms.Padding(2);
            this.chkAutoFit2.Name = "chkAutoFit2";
            this.chkAutoFit2.Size = new System.Drawing.Size(59, 17);
            this.chkAutoFit2.TabIndex = 5;
            this.chkAutoFit2.Text = "AutoFit";
            this.chkAutoFit2.UseVisualStyleBackColor = true;
            // 
            // chkBorderless2
            // 
            this.chkBorderless2.AutoSize = true;
            this.chkBorderless2.Checked = true;
            this.chkBorderless2.CheckState = System.Windows.Forms.CheckState.Checked;
            this.chkBorderless2.Location = new System.Drawing.Point(130, 85);
            this.chkBorderless2.Margin = new System.Windows.Forms.Padding(2);
            this.chkBorderless2.Name = "chkBorderless2";
            this.chkBorderless2.Size = new System.Drawing.Size(75, 17);
            this.chkBorderless2.TabIndex = 6;
            this.chkBorderless2.Text = "Borderless";
            this.chkBorderless2.UseVisualStyleBackColor = true;
            this.chkBorderless2.CheckedChanged += new System.EventHandler(this.ChkBorderless_CheckedChanged);
            // 
            // gridMap2
            // 
            this.gridMap2.BackColor = System.Drawing.SystemColors.Control;
            this.gridMap2.Location = new System.Drawing.Point(130, 106);
            this.gridMap2.Margin = new System.Windows.Forms.Padding(2);
            this.gridMap2.Name = "gridMap2";
            this.gridMap2.Size = new System.Drawing.Size(86, 66);
            this.gridMap2.TabIndex = 7;
            this.gridMap2.Text = "gridMap2";
            this.gridMap2.CellClicked += new System.Action<int>(this.GridMap2_CellClicked);
            // 
            // grpScreen3
            // 
            this.grpScreen3.Controls.Add(this.checkedScreen3);
            this.grpScreen3.Controls.Add(this.lblGridRows3);
            this.grpScreen3.Controls.Add(this.txtGridRows3);
            this.grpScreen3.Controls.Add(this.lblGridCols3);
            this.grpScreen3.Controls.Add(this.txtGridCols3);
            this.grpScreen3.Controls.Add(this.chkAutoFit3);
            this.grpScreen3.Controls.Add(this.chkBorderless3);
            this.grpScreen3.Controls.Add(this.gridMap3);
            this.grpScreen3.Location = new System.Drawing.Point(9, 356);
            this.grpScreen3.Margin = new System.Windows.Forms.Padding(2);
            this.grpScreen3.Name = "grpScreen3";
            this.grpScreen3.Padding = new System.Windows.Forms.Padding(2);
            this.grpScreen3.Size = new System.Drawing.Size(222, 186);
            this.grpScreen3.TabIndex = 6;
            this.grpScreen3.TabStop = false;
            this.grpScreen3.Text = "Screen 3";
            // 
            // checkedScreen3
            // 
            this.checkedScreen3.CheckOnClick = true;
            this.checkedScreen3.ColorOf = null;
            this.checkedScreen3.HorizontalScrollbar = true;
            this.checkedScreen3.Location = new System.Drawing.Point(6, 18);
            this.checkedScreen3.Margin = new System.Windows.Forms.Padding(2);
            this.checkedScreen3.Name = "checkedScreen3";
            this.checkedScreen3.Size = new System.Drawing.Size(120, 109);
            this.checkedScreen3.TabIndex = 4;
            // 
            // lblGridRows3
            // 
            this.lblGridRows3.AutoSize = true;
            this.lblGridRows3.Location = new System.Drawing.Point(130, 20);
            this.lblGridRows3.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.lblGridRows3.Name = "lblGridRows3";
            this.lblGridRows3.Size = new System.Drawing.Size(40, 13);
            this.lblGridRows3.TabIndex = 0;
            this.lblGridRows3.Text = "Rows :";
            // 
            // txtGridRows3
            // 
            this.txtGridRows3.Location = new System.Drawing.Point(172, 18);
            this.txtGridRows3.Margin = new System.Windows.Forms.Padding(2);
            this.txtGridRows3.Name = "txtGridRows3";
            this.txtGridRows3.Size = new System.Drawing.Size(28, 20);
            this.txtGridRows3.TabIndex = 1;
            this.txtGridRows3.Text = "0";
            this.txtGridRows3.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            // 
            // lblGridCols3
            // 
            this.lblGridCols3.AutoSize = true;
            this.lblGridCols3.Location = new System.Drawing.Point(130, 44);
            this.lblGridCols3.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.lblGridCols3.Name = "lblGridCols3";
            this.lblGridCols3.Size = new System.Drawing.Size(33, 13);
            this.lblGridCols3.TabIndex = 2;
            this.lblGridCols3.Text = "Cols :";
            // 
            // txtGridCols3
            // 
            this.txtGridCols3.Location = new System.Drawing.Point(172, 42);
            this.txtGridCols3.Margin = new System.Windows.Forms.Padding(2);
            this.txtGridCols3.Name = "txtGridCols3";
            this.txtGridCols3.Size = new System.Drawing.Size(28, 20);
            this.txtGridCols3.TabIndex = 3;
            this.txtGridCols3.Text = "0";
            this.txtGridCols3.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            // 
            // chkAutoFit3
            // 
            this.chkAutoFit3.AutoSize = true;
            this.chkAutoFit3.Location = new System.Drawing.Point(130, 66);
            this.chkAutoFit3.Margin = new System.Windows.Forms.Padding(2);
            this.chkAutoFit3.Name = "chkAutoFit3";
            this.chkAutoFit3.Size = new System.Drawing.Size(59, 17);
            this.chkAutoFit3.TabIndex = 5;
            this.chkAutoFit3.Text = "AutoFit";
            this.chkAutoFit3.UseVisualStyleBackColor = true;
            // 
            // chkBorderless3
            // 
            this.chkBorderless3.AutoSize = true;
            this.chkBorderless3.Checked = true;
            this.chkBorderless3.CheckState = System.Windows.Forms.CheckState.Checked;
            this.chkBorderless3.Location = new System.Drawing.Point(130, 85);
            this.chkBorderless3.Margin = new System.Windows.Forms.Padding(2);
            this.chkBorderless3.Name = "chkBorderless3";
            this.chkBorderless3.Size = new System.Drawing.Size(75, 17);
            this.chkBorderless3.TabIndex = 6;
            this.chkBorderless3.Text = "Borderless";
            this.chkBorderless3.UseVisualStyleBackColor = true;
            this.chkBorderless3.CheckedChanged += new System.EventHandler(this.ChkBorderless_CheckedChanged);
            // 
            // gridMap3
            // 
            this.gridMap3.BackColor = System.Drawing.SystemColors.Control;
            this.gridMap3.Location = new System.Drawing.Point(130, 106);
            this.gridMap3.Margin = new System.Windows.Forms.Padding(2);
            this.gridMap3.Name = "gridMap3";
            this.gridMap3.Size = new System.Drawing.Size(86, 66);
            this.gridMap3.TabIndex = 7;
            this.gridMap3.Text = "gridMap3";
            this.gridMap3.CellClicked += new System.Action<int>(this.GridMap3_CellClicked);
            // 
            // grpScreen4
            // 
            this.grpScreen4.Controls.Add(this.checkedScreen4);
            this.grpScreen4.Controls.Add(this.lblGridRows4);
            this.grpScreen4.Controls.Add(this.txtGridRows4);
            this.grpScreen4.Controls.Add(this.lblGridCols4);
            this.grpScreen4.Controls.Add(this.txtGridCols4);
            this.grpScreen4.Controls.Add(this.chkAutoFit4);
            this.grpScreen4.Controls.Add(this.chkBorderless4);
            this.grpScreen4.Controls.Add(this.gridMap4);
            this.grpScreen4.Location = new System.Drawing.Point(235, 356);
            this.grpScreen4.Margin = new System.Windows.Forms.Padding(2);
            this.grpScreen4.Name = "grpScreen4";
            this.grpScreen4.Padding = new System.Windows.Forms.Padding(2);
            this.grpScreen4.Size = new System.Drawing.Size(222, 186);
            this.grpScreen4.TabIndex = 7;
            this.grpScreen4.TabStop = false;
            this.grpScreen4.Text = "Screen 4";
            // 
            // checkedScreen4
            // 
            this.checkedScreen4.CheckOnClick = true;
            this.checkedScreen4.ColorOf = null;
            this.checkedScreen4.HorizontalScrollbar = true;
            this.checkedScreen4.Location = new System.Drawing.Point(6, 18);
            this.checkedScreen4.Margin = new System.Windows.Forms.Padding(2);
            this.checkedScreen4.Name = "checkedScreen4";
            this.checkedScreen4.Size = new System.Drawing.Size(120, 109);
            this.checkedScreen4.TabIndex = 4;
            // 
            // lblGridRows4
            // 
            this.lblGridRows4.AutoSize = true;
            this.lblGridRows4.Location = new System.Drawing.Point(130, 20);
            this.lblGridRows4.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.lblGridRows4.Name = "lblGridRows4";
            this.lblGridRows4.Size = new System.Drawing.Size(40, 13);
            this.lblGridRows4.TabIndex = 0;
            this.lblGridRows4.Text = "Rows :";
            // 
            // txtGridRows4
            // 
            this.txtGridRows4.Location = new System.Drawing.Point(172, 18);
            this.txtGridRows4.Margin = new System.Windows.Forms.Padding(2);
            this.txtGridRows4.Name = "txtGridRows4";
            this.txtGridRows4.Size = new System.Drawing.Size(28, 20);
            this.txtGridRows4.TabIndex = 1;
            this.txtGridRows4.Text = "0";
            this.txtGridRows4.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            // 
            // lblGridCols4
            // 
            this.lblGridCols4.AutoSize = true;
            this.lblGridCols4.Location = new System.Drawing.Point(130, 44);
            this.lblGridCols4.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.lblGridCols4.Name = "lblGridCols4";
            this.lblGridCols4.Size = new System.Drawing.Size(33, 13);
            this.lblGridCols4.TabIndex = 2;
            this.lblGridCols4.Text = "Cols :";
            // 
            // txtGridCols4
            // 
            this.txtGridCols4.Location = new System.Drawing.Point(172, 42);
            this.txtGridCols4.Margin = new System.Windows.Forms.Padding(2);
            this.txtGridCols4.Name = "txtGridCols4";
            this.txtGridCols4.Size = new System.Drawing.Size(28, 20);
            this.txtGridCols4.TabIndex = 3;
            this.txtGridCols4.Text = "0";
            this.txtGridCols4.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            // 
            // chkAutoFit4
            // 
            this.chkAutoFit4.AutoSize = true;
            this.chkAutoFit4.Location = new System.Drawing.Point(130, 66);
            this.chkAutoFit4.Margin = new System.Windows.Forms.Padding(2);
            this.chkAutoFit4.Name = "chkAutoFit4";
            this.chkAutoFit4.Size = new System.Drawing.Size(59, 17);
            this.chkAutoFit4.TabIndex = 5;
            this.chkAutoFit4.Text = "AutoFit";
            this.chkAutoFit4.UseVisualStyleBackColor = true;
            // 
            // chkBorderless4
            // 
            this.chkBorderless4.AutoSize = true;
            this.chkBorderless4.Checked = true;
            this.chkBorderless4.CheckState = System.Windows.Forms.CheckState.Checked;
            this.chkBorderless4.Location = new System.Drawing.Point(130, 85);
            this.chkBorderless4.Margin = new System.Windows.Forms.Padding(2);
            this.chkBorderless4.Name = "chkBorderless4";
            this.chkBorderless4.Size = new System.Drawing.Size(75, 17);
            this.chkBorderless4.TabIndex = 6;
            this.chkBorderless4.Text = "Borderless";
            this.chkBorderless4.UseVisualStyleBackColor = true;
            this.chkBorderless4.CheckedChanged += new System.EventHandler(this.ChkBorderless_CheckedChanged);
            // 
            // gridMap4
            // 
            this.gridMap4.BackColor = System.Drawing.SystemColors.Control;
            this.gridMap4.Location = new System.Drawing.Point(130, 106);
            this.gridMap4.Margin = new System.Windows.Forms.Padding(2);
            this.gridMap4.Name = "gridMap4";
            this.gridMap4.Size = new System.Drawing.Size(86, 66);
            this.gridMap4.TabIndex = 7;
            this.gridMap4.Text = "gridMap4";
            this.gridMap4.CellClicked += new System.Action<int>(this.GridMap4_CellClicked);
            // 
            // tabSystem
            // 
            this.tabSystem.Controls.Add(this.btnAbout);
            this.tabSystem.Controls.Add(this.grpResolution);
            this.tabSystem.Location = new System.Drawing.Point(4, 22);
            this.tabSystem.Margin = new System.Windows.Forms.Padding(2);
            this.tabSystem.Name = "tabSystem";
            this.tabSystem.Padding = new System.Windows.Forms.Padding(2);
            this.tabSystem.Size = new System.Drawing.Size(469, 580);
            this.tabSystem.TabIndex = 2;
            this.tabSystem.Text = "System";
            this.tabSystem.UseVisualStyleBackColor = true;
            //
            // btnAbout
            //
            this.btnAbout.Location = new System.Drawing.Point(9, 105);
            this.btnAbout.Margin = new System.Windows.Forms.Padding(2);
            this.btnAbout.Name = "btnAbout";
            this.btnAbout.Size = new System.Drawing.Size(93, 27);
            this.btnAbout.TabIndex = 4;
            this.btnAbout.Text = "About";
            this.btnAbout.UseVisualStyleBackColor = true;
            this.btnAbout.Click += new System.EventHandler(this.OnAbout);
            // 
            // grpResolution
            // 
            this.grpResolution.Controls.Add(this.lblResStatus);
            this.grpResolution.Controls.Add(this.btnCaptureRes);
            this.grpResolution.Controls.Add(this.txtResCycle);
            this.grpResolution.Controls.Add(this.lblResCycle);
            this.grpResolution.Controls.Add(this.txtResHeight);
            this.grpResolution.Controls.Add(this.lblResHeight);
            this.grpResolution.Controls.Add(this.txtResWidth);
            this.grpResolution.Controls.Add(this.lblResWidth);
            this.grpResolution.Controls.Add(this.chkEnableRes);
            this.grpResolution.Location = new System.Drawing.Point(9, 10);
            this.grpResolution.Margin = new System.Windows.Forms.Padding(2);
            this.grpResolution.Name = "grpResolution";
            this.grpResolution.Padding = new System.Windows.Forms.Padding(2);
            this.grpResolution.Size = new System.Drawing.Size(453, 81);
            this.grpResolution.TabIndex = 3;
            this.grpResolution.TabStop = false;
            this.grpResolution.Text = "Display Resolution Hold (clamp oversized displays)";
            // 
            // lblResStatus
            // 
            this.lblResStatus.Location = new System.Drawing.Point(9, 60);
            this.lblResStatus.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.lblResStatus.Name = "lblResStatus";
            this.lblResStatus.Size = new System.Drawing.Size(435, 16);
            this.lblResStatus.TabIndex = 8;
            this.lblResStatus.Text = "Status: idle";
            // 
            // btnCaptureRes
            // 
            this.btnCaptureRes.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(87)))), ((int)(((byte)(184)))));
            this.btnCaptureRes.ForeColor = System.Drawing.Color.White;
            this.btnCaptureRes.Location = new System.Drawing.Point(351, 33);
            this.btnCaptureRes.Margin = new System.Windows.Forms.Padding(2);
            this.btnCaptureRes.Name = "btnCaptureRes";
            this.btnCaptureRes.Size = new System.Drawing.Size(93, 21);
            this.btnCaptureRes.TabIndex = 7;
            this.btnCaptureRes.Text = "Capture current";
            this.btnCaptureRes.UseVisualStyleBackColor = false;
            this.btnCaptureRes.Click += new System.EventHandler(this.BtnCaptureRes_Click);
            // 
            // txtResCycle
            // 
            this.txtResCycle.Location = new System.Drawing.Point(290, 34);
            this.txtResCycle.Margin = new System.Windows.Forms.Padding(2);
            this.txtResCycle.Name = "txtResCycle";
            this.txtResCycle.Size = new System.Drawing.Size(38, 20);
            this.txtResCycle.TabIndex = 6;
            this.txtResCycle.Text = "10";
            this.txtResCycle.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            // 
            // lblResCycle
            // 
            this.lblResCycle.AutoSize = true;
            this.lblResCycle.Location = new System.Drawing.Point(225, 37);
            this.lblResCycle.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.lblResCycle.Name = "lblResCycle";
            this.lblResCycle.Size = new System.Drawing.Size(70, 13);
            this.lblResCycle.TabIndex = 5;
            this.lblResCycle.Text = "Check (sec) :";
            // 
            // txtResHeight
            // 
            this.txtResHeight.Location = new System.Drawing.Point(171, 34);
            this.txtResHeight.Margin = new System.Windows.Forms.Padding(2);
            this.txtResHeight.Name = "txtResHeight";
            this.txtResHeight.Size = new System.Drawing.Size(38, 20);
            this.txtResHeight.TabIndex = 4;
            this.txtResHeight.Text = "1080";
            this.txtResHeight.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            // 
            // lblResHeight
            // 
            this.lblResHeight.AutoSize = true;
            this.lblResHeight.Location = new System.Drawing.Point(128, 37);
            this.lblResHeight.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.lblResHeight.Name = "lblResHeight";
            this.lblResHeight.Size = new System.Drawing.Size(44, 13);
            this.lblResHeight.TabIndex = 3;
            this.lblResHeight.Text = "Height :";
            // 
            // txtResWidth
            // 
            this.txtResWidth.Location = new System.Drawing.Point(46, 34);
            this.txtResWidth.Margin = new System.Windows.Forms.Padding(2);
            this.txtResWidth.Name = "txtResWidth";
            this.txtResWidth.Size = new System.Drawing.Size(38, 20);
            this.txtResWidth.TabIndex = 2;
            this.txtResWidth.Text = "1920";
            this.txtResWidth.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            // 
            // lblResWidth
            // 
            this.lblResWidth.AutoSize = true;
            this.lblResWidth.Location = new System.Drawing.Point(9, 37);
            this.lblResWidth.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.lblResWidth.Name = "lblResWidth";
            this.lblResWidth.Size = new System.Drawing.Size(41, 13);
            this.lblResWidth.TabIndex = 1;
            this.lblResWidth.Text = "Width :";
            // 
            // chkEnableRes
            // 
            this.chkEnableRes.AutoSize = true;
            this.chkEnableRes.Location = new System.Drawing.Point(9, 15);
            this.chkEnableRes.Margin = new System.Windows.Forms.Padding(2);
            this.chkEnableRes.Name = "chkEnableRes";
            this.chkEnableRes.Size = new System.Drawing.Size(256, 17);
            this.chkEnableRes.TabIndex = 0;
            this.chkEnableRes.Text = "Hold display resolution (clamp oversized displays)";
            this.chkEnableRes.UseVisualStyleBackColor = true;
            // 
            // btnClear
            // 
            this.btnClear.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.btnClear.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(181)))), ((int)(((byte)(0)))), ((int)(((byte)(0)))));
            this.btnClear.ForeColor = System.Drawing.Color.White;
            this.btnClear.Location = new System.Drawing.Point(95, 620);
            this.btnClear.Margin = new System.Windows.Forms.Padding(2);
            this.btnClear.Name = "btnClear";
            this.btnClear.Size = new System.Drawing.Size(75, 26);
            this.btnClear.TabIndex = 13;
            this.btnClear.Text = "Clear";
            this.btnClear.UseVisualStyleBackColor = false;
            this.btnClear.Click += new System.EventHandler(this.BtnClear_Click);
            // 
            // btnSave
            // 
            this.btnSave.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.btnSave.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(230)))), ((int)(((byte)(138)))), ((int)(((byte)(0)))));
            this.btnSave.ForeColor = System.Drawing.Color.White;
            this.btnSave.Location = new System.Drawing.Point(14, 620);
            this.btnSave.Margin = new System.Windows.Forms.Padding(2);
            this.btnSave.Name = "btnSave";
            this.btnSave.Size = new System.Drawing.Size(75, 26);
            this.btnSave.TabIndex = 12;
            this.btnSave.Text = "Save";
            this.btnSave.UseVisualStyleBackColor = false;
            this.btnSave.Click += new System.EventHandler(this.BtnSave_Click);
            // 
            // TileKeeperForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(495, 651);
            this.Controls.Add(this.btnClear);
            this.Controls.Add(this.tabMain);
            this.Controls.Add(this.btnSave);
            this.Margin = new System.Windows.Forms.Padding(2);
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.MinimumSize = new System.Drawing.Size(495, 690);
            this.Name = "TileKeeperForm";
            this.ShowInTaskbar = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "TileKeeper";
            this.tabMain.ResumeLayout(false);
            this.tabPrograms.ResumeLayout(false);
            this.tabPrograms.PerformLayout();
            this.tabPosition.ResumeLayout(false);
            this.tabPosition.PerformLayout();
            this.grpPositionList.ResumeLayout(false);
            this.grpScreen1.ResumeLayout(false);
            this.grpScreen1.PerformLayout();
            this.grpScreen2.ResumeLayout(false);
            this.grpScreen2.PerformLayout();
            this.grpScreen3.ResumeLayout(false);
            this.grpScreen3.PerformLayout();
            this.grpScreen4.ResumeLayout(false);
            this.grpScreen4.PerformLayout();
            this.tabSystem.ResumeLayout(false);
            this.grpResolution.ResumeLayout(false);
            this.grpResolution.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.TabControl tabMain;
        private System.Windows.Forms.TabPage tabPrograms;
        private System.Windows.Forms.TabPage tabPosition;
        private System.Windows.Forms.TabPage tabSystem;
        private System.Windows.Forms.ListView listPrograms;
        private System.Windows.Forms.Button btnAdd;
        private System.Windows.Forms.Button btnEdit;
private System.Windows.Forms.Button btnRemove;
        private System.Windows.Forms.Label lblPgmHint;
        private System.Windows.Forms.CheckBox chkEnablePersist;
        private System.Windows.Forms.TextBox txtCycle;
        private System.Windows.Forms.Label lblDelay;
private TileKeeper.ColoredCheckedListBox checkedScreen1;
        private TileKeeper.ColoredCheckedListBox checkedScreen2;
        private System.Windows.Forms.CheckBox chkAutoFit1;
        private System.Windows.Forms.CheckBox chkAutoFit2;
        private System.Windows.Forms.CheckBox chkBorderless1;
        private System.Windows.Forms.CheckBox chkBorderless2;
        private System.Windows.Forms.CheckBox chkBorderless3;
        private System.Windows.Forms.CheckBox chkBorderless4;
        private System.Windows.Forms.TextBox txtCycleScreen;
        private System.Windows.Forms.Label lblCycle;
        private System.Windows.Forms.CheckBox chkEnableCustPos;
        private System.Windows.Forms.TextBox txtGridRows1;
        private System.Windows.Forms.Label lblGridRows1;
        private System.Windows.Forms.TextBox txtGridCols1;
        private System.Windows.Forms.Label lblGridCols1;
        private System.Windows.Forms.TextBox txtGridRows2;
        private System.Windows.Forms.Label lblGridRows2;
        private System.Windows.Forms.TextBox txtGridCols2;
        private System.Windows.Forms.Label lblGridCols2;
        private TileKeeper.Class.GridMapPanel gridMap1;
        private TileKeeper.Class.GridMapPanel gridMap2;
        private System.Windows.Forms.Label lblPosHint;
        private System.Windows.Forms.GroupBox grpResolution;
        private System.Windows.Forms.Button btnAbout;
        private System.Windows.Forms.CheckBox chkEnableRes;
        private System.Windows.Forms.Label lblResWidth;
        private System.Windows.Forms.TextBox txtResWidth;
        private System.Windows.Forms.Label lblResHeight;
        private System.Windows.Forms.TextBox txtResHeight;
        private System.Windows.Forms.Label lblResCycle;
        private System.Windows.Forms.TextBox txtResCycle;
        private System.Windows.Forms.Button btnCaptureRes;
        private System.Windows.Forms.Label lblResStatus;
        private System.Windows.Forms.Button btnClear;
        private System.Windows.Forms.Button btnSave;
private TileKeeper.ColoredCheckedListBox checkedScreen3;
        private TileKeeper.ColoredCheckedListBox checkedScreen4;
        private System.Windows.Forms.CheckBox chkAutoFit3;
        private System.Windows.Forms.CheckBox chkAutoFit4;
        private System.Windows.Forms.TextBox txtGridRows3;
        private System.Windows.Forms.TextBox txtGridRows4;
        private System.Windows.Forms.Label lblGridRows3;
        private System.Windows.Forms.Label lblGridRows4;
        private System.Windows.Forms.TextBox txtGridCols3;
        private System.Windows.Forms.TextBox txtGridCols4;
        private System.Windows.Forms.Label lblGridCols3;
        private System.Windows.Forms.Label lblGridCols4;
        private TileKeeper.Class.GridMapPanel gridMap3;
        private TileKeeper.Class.GridMapPanel gridMap4;
        private System.Windows.Forms.GroupBox grpPositionList;
        private System.Windows.Forms.ListView listPositionPrograms;
        private System.Windows.Forms.Button btnAddPosition;
        private System.Windows.Forms.Button btnEditPosition;
private System.Windows.Forms.Button btnRemovePosition;
        private System.Windows.Forms.GroupBox grpScreen1;
        private System.Windows.Forms.GroupBox grpScreen2;
        private System.Windows.Forms.GroupBox grpScreen3;
        private System.Windows.Forms.GroupBox grpScreen4;
    }
}
