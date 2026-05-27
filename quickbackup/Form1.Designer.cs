namespace quickbackup
{
    partial class Form1
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
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
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Form1));
            btnAdd = new Button();
            btnStart = new Button();
            btnStop = new Button();
            btnLog = new Button();
            lblLogKeepDays = new Label();
            numLogKeepDays = new NumericUpDown();
            lblLogKeepDaysUnit = new Label();
            lblBackupKeepDays = new Label();
            numBackupKeepDays = new NumericUpDown();
            lblBackupKeepDaysUnit = new Label();
            chkAutorun = new CheckBox();
            lblStatus = new Label();
            topPanel = new FlowLayoutPanel();
            rowsPanel = new FlowLayoutPanel();
            trayIcon = new NotifyIcon(components);
            trayMenu = new ContextMenuStrip(components);
            menuOpen = new ToolStripMenuItem();
            menuExit = new ToolStripMenuItem();
            ((System.ComponentModel.ISupportInitialize)numLogKeepDays).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numBackupKeepDays).BeginInit();
            topPanel.SuspendLayout();
            trayMenu.SuspendLayout();
            SuspendLayout();
            // 
            // btnAdd
            // 
            btnAdd.Location = new Point(3, 3);
            btnAdd.Name = "btnAdd";
            btnAdd.Size = new Size(42, 32);
            btnAdd.TabIndex = 0;
            btnAdd.Text = "+";
            btnAdd.UseVisualStyleBackColor = true;
            // 
            // btnStart
            // 
            btnStart.Location = new Point(51, 3);
            btnStart.Name = "btnStart";
            btnStart.Size = new Size(78, 32);
            btnStart.TabIndex = 1;
            btnStart.Text = "START";
            btnStart.UseVisualStyleBackColor = true;
            // 
            // btnStop
            // 
            btnStop.Enabled = false;
            btnStop.Location = new Point(135, 3);
            btnStop.Name = "btnStop";
            btnStop.Size = new Size(78, 32);
            btnStop.TabIndex = 2;
            btnStop.Text = "STOP";
            btnStop.UseVisualStyleBackColor = true;
            // 
            // btnLog
            // 
            btnLog.Location = new Point(219, 3);
            btnLog.Name = "btnLog";
            btnLog.Size = new Size(60, 32);
            btnLog.TabIndex = 3;
            btnLog.Text = "log";
            btnLog.UseVisualStyleBackColor = true;
            // 
            // lblLogKeepDays
            // 
            lblLogKeepDays.AutoSize = true;
            lblLogKeepDays.Location = new Point(285, 10);
            lblLogKeepDays.Margin = new Padding(3, 10, 0, 0);
            lblLogKeepDays.Name = "lblLogKeepDays";
            lblLogKeepDays.Size = new Size(89, 25);
            lblLogKeepDays.TabIndex = 4;
            lblLogKeepDays.Text = "Log keep:";
            // 
            // numLogKeepDays
            // 
            numLogKeepDays.Location = new Point(377, 4);
            numLogKeepDays.Margin = new Padding(3, 4, 8, 0);
            numLogKeepDays.Maximum = new decimal(new int[] { 14, 0, 0, 0 });
            numLogKeepDays.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            numLogKeepDays.Name = "numLogKeepDays";
            numLogKeepDays.Size = new Size(56, 31);
            numLogKeepDays.TabIndex = 5;
            numLogKeepDays.Value = new decimal(new int[] { 7, 0, 0, 0 });
            // 
            // lblLogKeepDaysUnit
            // 
            lblLogKeepDaysUnit.AutoSize = true;
            lblLogKeepDaysUnit.Location = new Point(444, 10);
            lblLogKeepDaysUnit.Margin = new Padding(3, 10, 8, 0);
            lblLogKeepDaysUnit.Name = "lblLogKeepDaysUnit";
            lblLogKeepDaysUnit.Size = new Size(45, 25);
            lblLogKeepDaysUnit.TabIndex = 6;
            lblLogKeepDaysUnit.Text = "days";
            // 
            // lblBackupKeepDays
            // 
            lblBackupKeepDays.AutoSize = true;
            lblBackupKeepDays.Location = new Point(500, 10);
            lblBackupKeepDays.Margin = new Padding(3, 10, 0, 0);
            lblBackupKeepDays.Name = "lblBackupKeepDays";
            lblBackupKeepDays.Size = new Size(126, 25);
            lblBackupKeepDays.TabIndex = 7;
            lblBackupKeepDays.Text = "Backup keep:";
            // 
            // numBackupKeepDays
            // 
            numBackupKeepDays.Location = new Point(629, 4);
            numBackupKeepDays.Margin = new Padding(3, 4, 8, 0);
            numBackupKeepDays.Maximum = new decimal(new int[] { 14, 0, 0, 0 });
            numBackupKeepDays.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            numBackupKeepDays.Name = "numBackupKeepDays";
            numBackupKeepDays.Size = new Size(56, 31);
            numBackupKeepDays.TabIndex = 8;
            numBackupKeepDays.Value = new decimal(new int[] { 3, 0, 0, 0 });
            // 
            // lblBackupKeepDaysUnit
            // 
            lblBackupKeepDaysUnit.AutoSize = true;
            lblBackupKeepDaysUnit.Location = new Point(696, 10);
            lblBackupKeepDaysUnit.Margin = new Padding(3, 10, 8, 0);
            lblBackupKeepDaysUnit.Name = "lblBackupKeepDaysUnit";
            lblBackupKeepDaysUnit.Size = new Size(45, 25);
            lblBackupKeepDaysUnit.TabIndex = 9;
            lblBackupKeepDaysUnit.Text = "days";
            // 
            // chkAutorun
            // 
            chkAutorun.AutoSize = true;
            chkAutorun.Location = new Point(752, 8);
            chkAutorun.Margin = new Padding(3, 8, 3, 0);
            chkAutorun.Name = "chkAutorun";
            chkAutorun.Size = new Size(181, 29);
            chkAutorun.TabIndex = 10;
            chkAutorun.Text = "autorun at startup";
            chkAutorun.UseVisualStyleBackColor = true;
            // 
            // lblStatus
            // 
            lblStatus.AutoSize = true;
            lblStatus.Location = new Point(720, 10);
            lblStatus.Margin = new Padding(3, 10, 3, 0);
            lblStatus.Name = "lblStatus";
            lblStatus.Size = new Size(64, 25);
            lblStatus.TabIndex = 11;
            lblStatus.Text = "Status:";
            // 
            // topPanel
            // 
            topPanel.Controls.Add(btnAdd);
            topPanel.Controls.Add(btnStart);
            topPanel.Controls.Add(btnStop);
            topPanel.Controls.Add(btnLog);
            topPanel.Controls.Add(lblLogKeepDays);
            topPanel.Controls.Add(numLogKeepDays);
            topPanel.Controls.Add(lblLogKeepDaysUnit);
            topPanel.Controls.Add(lblBackupKeepDays);
            topPanel.Controls.Add(numBackupKeepDays);
            topPanel.Controls.Add(lblBackupKeepDaysUnit);
            topPanel.Controls.Add(chkAutorun);
            topPanel.Controls.Add(lblStatus);
            topPanel.SetFlowBreak(chkAutorun, true);
            topPanel.Dock = DockStyle.Top;
            topPanel.Location = new Point(0, 0);
            topPanel.Name = "topPanel";
            topPanel.Padding = new Padding(0, 0, 0, 6);
            topPanel.Size = new Size(1038, 82);
            topPanel.TabIndex = 0;
            // 
            // rowsPanel
            // 
            rowsPanel.AutoScroll = true;
            rowsPanel.Dock = DockStyle.Fill;
            rowsPanel.FlowDirection = FlowDirection.TopDown;
            rowsPanel.Location = new Point(0, 82);
            rowsPanel.Name = "rowsPanel";
            rowsPanel.Padding = new Padding(8, 6, 8, 8);
            rowsPanel.Size = new Size(1038, 418);
            rowsPanel.TabIndex = 1;
            rowsPanel.WrapContents = false;
            // 
            // trayIcon
            // 
            trayIcon.ContextMenuStrip = trayMenu;
            trayIcon.Icon = (Icon)resources.GetObject("trayIcon.Icon");
            trayIcon.Text = "QuickBackup";
            trayIcon.Visible = true;
            // 
            // trayMenu
            // 
            trayMenu.ImageScalingSize = new Size(24, 24);
            trayMenu.Items.AddRange(new ToolStripItem[] { menuOpen, menuExit });
            trayMenu.Name = "trayMenu";
            trayMenu.Size = new Size(129, 68);
            // 
            // menuOpen
            // 
            menuOpen.Name = "menuOpen";
            menuOpen.Size = new Size(128, 32);
            menuOpen.Text = "Open";
            // 
            // menuExit
            // 
            menuExit.Name = "menuExit";
            menuExit.Size = new Size(128, 32);
            menuExit.Text = "Exit";
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(10F, 25F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1038, 500);
            Controls.Add(rowsPanel);
            Controls.Add(topPanel);
            Icon = (Icon)resources.GetObject("$this.Icon");
            MaximizeBox = false;
            MinimumSize = new Size(760, 260);
            Name = "Form1";
            Text = "QuickBackup 0.1";
            ((System.ComponentModel.ISupportInitialize)numLogKeepDays).EndInit();
            ((System.ComponentModel.ISupportInitialize)numBackupKeepDays).EndInit();
            topPanel.ResumeLayout(false);
            topPanel.PerformLayout();
            trayMenu.ResumeLayout(false);
            ResumeLayout(false);
        }

        #endregion

        private Button btnAdd;
        private Button btnStart;
        private Button btnStop;
        private Button btnLog;
        private Label lblLogKeepDays;
        private NumericUpDown numLogKeepDays;
        private Label lblLogKeepDaysUnit;
        private Label lblBackupKeepDays;
        private NumericUpDown numBackupKeepDays;
        private Label lblBackupKeepDaysUnit;
        private CheckBox chkAutorun;
        private Label lblStatus;
        private FlowLayoutPanel topPanel;
        private FlowLayoutPanel rowsPanel;
        private NotifyIcon trayIcon;
        private ContextMenuStrip trayMenu;
        private ToolStripMenuItem menuOpen;
        private ToolStripMenuItem menuExit;
    }
}
