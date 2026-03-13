# Naja WinForms Compilation Test
# Task Dashboard - standard WinForms only, no third-party dependencies

from System.Windows.Forms import Form
from System.Windows.Forms import Application
from System.Windows.Forms import MenuStrip
from System.Windows.Forms import ToolStripMenuItem
from System.Windows.Forms import ToolStripSeparator
from System.Windows.Forms import ToolStrip
from System.Windows.Forms import ToolStripButton
from System.Windows.Forms import StatusStrip
from System.Windows.Forms import ToolStripStatusLabel
from System.Windows.Forms import TabControl
from System.Windows.Forms import TabPage
from System.Windows.Forms import Panel
from System.Windows.Forms import Label
from System.Windows.Forms import Button
from System.Windows.Forms import TextBox
from System.Windows.Forms import ProgressBar
from System.Windows.Forms import CheckBox
from System.Windows.Forms import ComboBox
from System.Windows.Forms import GroupBox
from System.Windows.Forms import DataGridView
from System.Windows.Forms import DataGridViewTextBoxColumn
from System.Windows.Forms import MessageBox
from System.Windows.Forms import Clipboard
from System.Drawing import Size
from System.Drawing import Point
from System.Drawing import Color
from System.Drawing import Font

#  Add Task Dialog 

class AddTaskDialog(Form):
    def __init__(self):
        self.Text = "Add New Task"
        self.Size = Size(420, 300)
        self.FormBorderStyle = 3
        self.StartPosition = 1
        self.MaximizeBox = False
        self.MinimizeBox = False

        lbl_title = Label()
        lbl_title.Text = "Task Title:"
        lbl_title.Location = Point(20, 20)
        lbl_title.Size = Size(100, 22)

        self.txt_title = TextBox()
        self.txt_title.Location = Point(20, 45)
        self.txt_title.Size = Size(360, 26)

        lbl_priority = Label()
        lbl_priority.Text = "Priority:"
        lbl_priority.Location = Point(20, 90)
        lbl_priority.Size = Size(100, 22)

        self.cmb_priority = ComboBox()
        self.cmb_priority.Location = Point(20, 115)
        self.cmb_priority.Size = Size(160, 26)
        self.cmb_priority.Items.Add("High")
        self.cmb_priority.Items.Add("Medium")
        self.cmb_priority.Items.Add("Low")
        self.cmb_priority.SelectedIndex = 1

        lbl_progress = Label()
        lbl_progress.Text = "Progress (0-100):"
        lbl_progress.Location = Point(200, 90)
        lbl_progress.Size = Size(140, 22)

        self.txt_progress = TextBox()
        self.txt_progress.Location = Point(200, 115)
        self.txt_progress.Size = Size(80, 26)
        self.txt_progress.Text = "0"

        lbl_notes = Label()
        lbl_notes.Text = "Notes:"
        lbl_notes.Location = Point(20, 160)
        lbl_notes.Size = Size(100, 22)

        self.txt_notes = TextBox()
        self.txt_notes.Location = Point(20, 185)
        self.txt_notes.Size = Size(360, 50)
        self.txt_notes.Multiline = True

        btn_ok = Button()
        btn_ok.Text = "Add Task"
        btn_ok.Location = Point(200, 245)
        btn_ok.Size = Size(90, 28)
        btn_ok.Click += self.handle_ok

        btn_cancel = Button()
        btn_cancel.Text = "Cancel"
        btn_cancel.Location = Point(300, 245)
        btn_cancel.Size = Size(80, 28)
        btn_cancel.Click += self.handle_cancel

        self.Controls.Add(lbl_title)
        self.Controls.Add(self.txt_title)
        self.Controls.Add(lbl_priority)
        self.Controls.Add(self.cmb_priority)
        self.Controls.Add(lbl_progress)
        self.Controls.Add(self.txt_progress)
        self.Controls.Add(lbl_notes)
        self.Controls.Add(self.txt_notes)
        self.Controls.Add(btn_ok)
        self.Controls.Add(btn_cancel)

    def handle_ok(self, sender, e):
        if self.txt_title.Text == "":
            MessageBox.Show("Task title cannot be empty.", "Validation Error")
            return
        self.DialogResult = 1
        self.Close()

    def handle_cancel(self, sender, e):
        self.DialogResult = 2
        self.Close()

#  Settings Panel 

class SettingsPanel(Panel):
    def __init__(self):
        self.Size = Size(820, 510)
        self.BackColor = Color.FromArgb(250, 250, 255)

        lbl_hdr = Label()
        lbl_hdr.Text = "Application Settings"
        lbl_hdr.Location = Point(10, 10)
        lbl_hdr.Size = Size(790, 28)
        lbl_hdr.BackColor = Color.FromArgb(63, 81, 181)
        lbl_hdr.ForeColor = Color.FromArgb(255, 255, 255)
        lbl_hdr.Font = Font("Segoe UI", 10, 1)
        self.Controls.Add(lbl_hdr)

        grp_theme = GroupBox()
        grp_theme.Text = "Theme"
        grp_theme.Location = Point(10, 50)
        grp_theme.Size = Size(370, 110)

        chk_dark = CheckBox()
        chk_dark.Text = "Enable Dark Mode"
        chk_dark.Location = Point(15, 30)
        chk_dark.Size = Size(200, 24)

        chk_anim = CheckBox()
        chk_anim.Text = "Enable Animations"
        chk_anim.Location = Point(15, 58)
        chk_anim.Size = Size(200, 24)
        chk_anim.Checked = True

        chk_compact = CheckBox()
        chk_compact.Text = "Compact Layout"
        chk_compact.Location = Point(15, 86)
        chk_compact.Size = Size(200, 24)

        grp_theme.Controls.Add(chk_dark)
        grp_theme.Controls.Add(chk_anim)
        grp_theme.Controls.Add(chk_compact)
        self.Controls.Add(grp_theme)

        grp_notif = GroupBox()
        grp_notif.Text = "Notifications"
        grp_notif.Location = Point(400, 50)
        grp_notif.Size = Size(370, 110)

        chk_email = CheckBox()
        chk_email.Text = "Email Notifications"
        chk_email.Location = Point(15, 30)
        chk_email.Size = Size(220, 24)
        chk_email.Checked = True

        chk_desktop = CheckBox()
        chk_desktop.Text = "Desktop Alerts"
        chk_desktop.Location = Point(15, 58)
        chk_desktop.Size = Size(220, 24)
        chk_desktop.Checked = True

        chk_sound = CheckBox()
        chk_sound.Text = "Sound Alerts"
        chk_sound.Location = Point(15, 86)
        chk_sound.Size = Size(220, 24)

        grp_notif.Controls.Add(chk_email)
        grp_notif.Controls.Add(chk_desktop)
        grp_notif.Controls.Add(chk_sound)
        self.Controls.Add(grp_notif)

        grp_user = GroupBox()
        grp_user.Text = "User Profile"
        grp_user.Location = Point(10, 175)
        grp_user.Size = Size(760, 120)

        lbl_name = Label()
        lbl_name.Text = "Display Name:"
        lbl_name.Location = Point(15, 30)
        lbl_name.Size = Size(110, 22)

        txt_name = TextBox()
        txt_name.Location = Point(130, 28)
        txt_name.Size = Size(240, 26)
        txt_name.Text = "Naja Developer"

        lbl_email = Label()
        lbl_email.Text = "Email:"
        lbl_email.Location = Point(15, 65)
        lbl_email.Size = Size(110, 22)

        txt_email = TextBox()
        txt_email.Location = Point(130, 63)
        txt_email.Size = Size(240, 26)
        txt_email.Text = "dev@naja-compiler.io"

        btn_save = Button()
        btn_save.Text = "Save Profile"
        btn_save.Location = Point(430, 45)
        btn_save.Size = Size(110, 32)
        btn_save.Click += self.handle_save

        grp_user.Controls.Add(lbl_name)
        grp_user.Controls.Add(txt_name)
        grp_user.Controls.Add(lbl_email)
        grp_user.Controls.Add(txt_email)
        grp_user.Controls.Add(btn_save)
        self.Controls.Add(grp_user)

    def handle_save(self, sender, e):
        MessageBox.Show("Profile saved successfully!", "Settings")

#  Analytics Panel 

class AnalyticsPanel(Panel):
    def __init__(self):
        self.Size = Size(820, 510)
        self.BackColor = Color.FromArgb(255, 255, 255)

        lbl_hdr = Label()
        lbl_hdr.Text = "Analytics Overview"
        lbl_hdr.Location = Point(10, 10)
        lbl_hdr.Size = Size(790, 28)
        lbl_hdr.BackColor = Color.FromArgb(63, 81, 181)
        lbl_hdr.ForeColor = Color.FromArgb(255, 255, 255)
        lbl_hdr.Font = Font("Segoe UI", 10, 1)
        self.Controls.Add(lbl_hdr)

        self.Controls.Add(self._stat_box("Total Tasks",  "24", Color.FromArgb(63, 81, 181),  Point(10,  50)))
        self.Controls.Add(self._stat_box("Completed",    "18", Color.FromArgb(76, 175, 80),   Point(210, 50)))
        self.Controls.Add(self._stat_box("In Progress",  "4",  Color.FromArgb(255, 152, 0),   Point(410, 50)))
        self.Controls.Add(self._stat_box("Overdue",      "2",  Color.FromArgb(244, 67, 54),   Point(610, 50)))

        lbl_breakdown = Label()
        lbl_breakdown.Text = "Weekly Completion"
        lbl_breakdown.Location = Point(10, 165)
        lbl_breakdown.Size = Size(790, 26)
        lbl_breakdown.BackColor = Color.FromArgb(63, 81, 181)
        lbl_breakdown.ForeColor = Color.FromArgb(255, 255, 255)
        lbl_breakdown.Font = Font("Segoe UI", 10, 1)
        self.Controls.Add(lbl_breakdown)

        weeks = ["Week 1", "Week 2", "Week 3", "Week 4", "Week 5"]
        values = [60, 75, 50, 90, 70]
        i = 0
        while i < 5:
            lbl_w = Label()
            lbl_w.Text = weeks[i]
            lbl_w.Location = Point(10, 205 + i * 40)
            lbl_w.Size = Size(80, 22)
            self.Controls.Add(lbl_w)

            bar = ProgressBar()
            bar.Location = Point(100, 207 + i * 40)
            bar.Size = Size(500, 20)
            bar.Minimum = 0
            bar.Maximum = 100
            bar.Value = values[i]
            self.Controls.Add(bar)

            lbl_pct = Label()
            lbl_pct.Text = str(values[i]) + "%"
            lbl_pct.Location = Point(610, 205 + i * 40)
            lbl_pct.Size = Size(50, 22)
            self.Controls.Add(lbl_pct)

            i = i + 1

    def _stat_box(self, title, value, color, loc):
        pnl = Panel()
        pnl.Location = loc
        pnl.Size = Size(185, 100)
        pnl.BackColor = color

        lbl_val = Label()
        lbl_val.Text = value
        lbl_val.Font = Font("Segoe UI", 26, 1)
        lbl_val.ForeColor = Color.FromArgb(255, 255, 255)
        lbl_val.Location = Point(5, 10)
        lbl_val.Size = Size(175, 50)

        lbl_title = Label()
        lbl_title.Text = title
        lbl_title.ForeColor = Color.FromArgb(255, 255, 255)
        lbl_title.Location = Point(5, 65)
        lbl_title.Size = Size(175, 22)

        pnl.Controls.Add(lbl_val)
        pnl.Controls.Add(lbl_title)
        return pnl

#  Main Dashboard Form 

class DashboardForm(Form):
    def __init__(self):
        self.Text = "Naja Task Dashboard  |  WinForms Test"
        self.Size = Size(860, 640)
        self.StartPosition = 1
        self.MinimumSize = Size(700, 500)
        self.task_count = 0

        menu = MenuStrip()

        m_file = ToolStripMenuItem("File")
        m_new = ToolStripMenuItem("New Task")
        m_new.Click += self.handle_new_task
        m_export = ToolStripMenuItem("Export CSV")
        m_export.Click += self.handle_export
        m_sep = ToolStripSeparator()
        m_exit = ToolStripMenuItem("Exit")
        m_exit.Click += self.handle_exit
        m_file.DropDownItems.Add(m_new)
        m_file.DropDownItems.Add(m_export)
        m_file.DropDownItems.Add(m_sep)
        m_file.DropDownItems.Add(m_exit)

        m_view = ToolStripMenuItem("View")
        m_refresh = ToolStripMenuItem("Refresh")
        m_refresh.Click += self.handle_refresh
        m_view.DropDownItems.Add(m_refresh)

        m_help = ToolStripMenuItem("Help")
        m_about = ToolStripMenuItem("About")
        m_about.Click += self.handle_about
        m_help.DropDownItems.Add(m_about)

        menu.Items.Add(m_file)
        menu.Items.Add(m_view)
        menu.Items.Add(m_help)
        self.Controls.Add(menu)
        self.MainMenuStrip = menu

        toolbar = ToolStrip()
        tb_new = ToolStripButton()
        tb_new.Text = "+ New Task"
        tb_new.Click += self.handle_new_task
        tb_refresh = ToolStripButton()
        tb_refresh.Text = "Refresh"
        tb_refresh.Click += self.handle_refresh
        tb_sep = ToolStripSeparator()
        tb_clear = ToolStripButton()
        tb_clear.Text = "Clear Done"
        tb_clear.Click += self.handle_clear_done
        toolbar.Items.Add(tb_new)
        toolbar.Items.Add(tb_refresh)
        toolbar.Items.Add(tb_sep)
        toolbar.Items.Add(tb_clear)
        self.Controls.Add(toolbar)

        self.tabs = TabControl()
        self.tabs.Location = Point(0, 50)
        self.tabs.Size = Size(844, 545)

        # Tab 1: Task Board
        page_board = TabPage()
        page_board.Text = "Task Board"

        board_panel = Panel()
        board_panel.Size = Size(830, 510)
        board_panel.BackColor = Color.FromArgb(245, 245, 250)

        lbl_board_hdr = Label()
        lbl_board_hdr.Text = "Active Tasks"
        lbl_board_hdr.Location = Point(10, 8)
        lbl_board_hdr.Size = Size(800, 26)
        lbl_board_hdr.BackColor = Color.FromArgb(63, 81, 181)
        lbl_board_hdr.ForeColor = Color.FromArgb(255, 255, 255)
        lbl_board_hdr.Font = Font("Segoe UI", 10, 1)
        board_panel.Controls.Add(lbl_board_hdr)

        self.grid = DataGridView()
        self.grid.Location = Point(10, 44)
        self.grid.Size = Size(810, 260)
        self.grid.AllowUserToAddRows = False
        self.grid.AllowUserToDeleteRows = False
        self.grid.ReadOnly = True
        self.grid.SelectionMode = 1
        self.grid.AutoGenerateColumns = False

        col_id = DataGridViewTextBoxColumn()
        col_id.HeaderText = "ID"
        col_id.Width = 45
        col_title = DataGridViewTextBoxColumn()
        col_title.HeaderText = "Task Title"
        col_title.Width = 270
        col_priority = DataGridViewTextBoxColumn()
        col_priority.HeaderText = "Priority"
        col_priority.Width = 85
        col_progress = DataGridViewTextBoxColumn()
        col_progress.HeaderText = "Progress %"
        col_progress.Width = 95
        col_status = DataGridViewTextBoxColumn()
        col_status.HeaderText = "Status"
        col_status.Width = 110
        col_due = DataGridViewTextBoxColumn()
        col_due.HeaderText = "Due Date"
        col_due.Width = 110

        self.grid.Columns.Add(col_id)
        self.grid.Columns.Add(col_title)
        self.grid.Columns.Add(col_priority)
        self.grid.Columns.Add(col_progress)
        self.grid.Columns.Add(col_status)
        self.grid.Columns.Add(col_due)
        board_panel.Controls.Add(self.grid)

        lbl_prog_hdr = Label()
        lbl_prog_hdr.Text = "Progress Summary"
        lbl_prog_hdr.Location = Point(10, 315)
        lbl_prog_hdr.Size = Size(800, 26)
        lbl_prog_hdr.BackColor = Color.FromArgb(63, 81, 181)
        lbl_prog_hdr.ForeColor = Color.FromArgb(255, 255, 255)
        lbl_prog_hdr.Font = Font("Segoe UI", 10, 1)
        board_panel.Controls.Add(lbl_prog_hdr)

        lbl_total_tasks = Label()
        lbl_total_tasks.Text = "Total Tasks:"
        lbl_total_tasks.Location = Point(10, 352)
        lbl_total_tasks.Size = Size(100, 22)
        board_panel.Controls.Add(lbl_total_tasks)

        self.lbl_task_count = Label()
        self.lbl_task_count.Text = "0"
        self.lbl_task_count.Font = Font("Segoe UI", 12, 1)
        self.lbl_task_count.Location = Point(115, 348)
        self.lbl_task_count.Size = Size(50, 28)
        self.lbl_task_count.ForeColor = Color.FromArgb(63, 81, 181)
        board_panel.Controls.Add(self.lbl_task_count)

        lbl_avg = Label()
        lbl_avg.Text = "Avg Progress:"
        lbl_avg.Location = Point(185, 352)
        lbl_avg.Size = Size(110, 22)
        board_panel.Controls.Add(lbl_avg)

        self.overall_bar = ProgressBar()
        self.overall_bar.Location = Point(300, 352)
        self.overall_bar.Size = Size(320, 22)
        self.overall_bar.Minimum = 0
        self.overall_bar.Maximum = 100
        self.overall_bar.Value = 0
        board_panel.Controls.Add(self.overall_bar)

        self.lbl_pct = Label()
        self.lbl_pct.Text = "0%"
        self.lbl_pct.Location = Point(628, 352)
        self.lbl_pct.Size = Size(50, 22)
        self.lbl_pct.Font = Font("Segoe UI", 9, 1)
        board_panel.Controls.Add(self.lbl_pct)

        btn_add = Button()
        btn_add.Text = "Add Task"
        btn_add.Location = Point(10, 388)
        btn_add.Size = Size(100, 30)
        btn_add.Click += self.handle_new_task
        board_panel.Controls.Add(btn_add)

        btn_complete = Button()
        btn_complete.Text = "Mark Done"
        btn_complete.Location = Point(120, 388)
        btn_complete.Size = Size(100, 30)
        btn_complete.Click += self.handle_complete_task
        board_panel.Controls.Add(btn_complete)

        btn_delete = Button()
        btn_delete.Text = "Delete"
        btn_delete.Location = Point(230, 388)
        btn_delete.Size = Size(90, 30)
        btn_delete.Click += self.handle_delete_task
        board_panel.Controls.Add(btn_delete)

        page_board.Controls.Add(board_panel)

        # Tab 2: Notes
        page_notes = TabPage()
        page_notes.Text = "Notes"

        notes_panel = Panel()
        notes_panel.Size = Size(830, 510)
        notes_panel.BackColor = Color.FromArgb(255, 255, 255)

        lbl_notes_hdr = Label()
        lbl_notes_hdr.Text = "Quick Notes"
        lbl_notes_hdr.Location = Point(10, 8)
        lbl_notes_hdr.Size = Size(800, 26)
        lbl_notes_hdr.BackColor = Color.FromArgb(63, 81, 181)
        lbl_notes_hdr.ForeColor = Color.FromArgb(255, 255, 255)
        lbl_notes_hdr.Font = Font("Segoe UI", 10, 1)
        notes_panel.Controls.Add(lbl_notes_hdr)

        self.txt_notes = TextBox()
        self.txt_notes.Location = Point(10, 44)
        self.txt_notes.Size = Size(810, 380)
        self.txt_notes.Multiline = True
        self.txt_notes.ScrollBars = 2
        self.txt_notes.Font = Font("Consolas", 10, 0)
        self.txt_notes.Text = "Naja Compiler - WinForms Test Notes\n\n- DataGridView tested\n- TabControl tested\n- MenuStrip tested\n- ToolStrip tested\n- ProgressBar tested\n- GroupBox / CheckBox tested\n"
        notes_panel.Controls.Add(self.txt_notes)

        btn_clear_notes = Button()
        btn_clear_notes.Text = "Clear"
        btn_clear_notes.Location = Point(10, 434)
        btn_clear_notes.Size = Size(90, 30)
        btn_clear_notes.Click += self.handle_clear_notes
        notes_panel.Controls.Add(btn_clear_notes)

        btn_copy = Button()
        btn_copy.Text = "Copy All"
        btn_copy.Location = Point(110, 434)
        btn_copy.Size = Size(90, 30)
        btn_copy.Click += self.handle_copy_notes
        notes_panel.Controls.Add(btn_copy)

        page_notes.Controls.Add(notes_panel)

        # Tab 3: Analytics
        page_analytics = TabPage()
        page_analytics.Text = "Analytics"
        page_analytics.Controls.Add(AnalyticsPanel())

        # Tab 4: Settings
        page_settings = TabPage()
        page_settings.Text = "Settings"
        page_settings.Controls.Add(SettingsPanel())

        self.tabs.TabPages.Add(page_board)
        self.tabs.TabPages.Add(page_notes)
        self.tabs.TabPages.Add(page_analytics)
        self.tabs.TabPages.Add(page_settings)
        self.Controls.Add(self.tabs)

        status = StatusStrip()
        self.lbl_status = ToolStripStatusLabel()
        self.lbl_status.Text = "Ready  |  Naja Compiler v1.0"
        self.lbl_status.Spring = True
        lbl_build = ToolStripStatusLabel()
        lbl_build.Text = "Build: 2025.1"
        status.Items.Add(self.lbl_status)
        status.Items.Add(lbl_build)
        self.Controls.Add(status)

        self._seed_tasks()

    def _seed_tasks(self):
        self._add_row(1, "Implement Naja lexer",      "High",   100, "Done",        "2025-01-10")
        self._add_row(2, "Build AST parser",           "High",   100, "Done",        "2025-01-20")
        self._add_row(3, "Semantic analysis pass",     "High",    85, "In Progress", "2025-02-01")
        self._add_row(4, "IL code generation",         "High",    72, "In Progress", "2025-02-15")
        self._add_row(5, "WinForms runtime support",   "Medium",  60, "In Progress", "2025-03-01")
        self._add_row(6, "Unit test suite",            "Low",     20, "Pending",     "2025-04-01")
        self._add_row(7, "Documentation",              "Low",     10, "Pending",     "2025-04-30")
        self.task_count = 7
        self._refresh_summary()

    def _add_row(self, task_id, title, priority, progress, status, due):
        self.grid.Rows.Add(str(task_id), title, priority, str(progress), status, due)

    def _refresh_summary(self):
        row_count = self.grid.Rows.Count
        self.lbl_task_count.Text = str(row_count)
        if row_count == 0:
            self.overall_bar.Value = 0
            self.lbl_pct.Text = "0%"
            return
        total = 0
        i = 0
        while i < row_count:
            try:
                val = self.grid.Rows[i].Cells[3].Value
                if val is not None:
                    total = total + int(str(val))
            except:
                pass
            i = i + 1
        avg = int(total / row_count)
        if avg > 100:
            avg = 100
        if avg < 0:
            avg = 0
        self.overall_bar.Value = avg
        self.lbl_pct.Text = str(avg) + "%"
        self.lbl_status.Text = "Tasks: " + str(row_count) + "  |  Avg Progress: " + str(avg) + "%"

    def handle_new_task(self, sender, e):
        dlg = AddTaskDialog()
        result = dlg.ShowDialog(self)
        if result == 1:
            self.task_count = self.task_count + 1
            title = dlg.txt_title.Text
            priority = str(dlg.cmb_priority.SelectedItem)
            progress = 0
            try:
                progress = int(dlg.txt_progress.Text)
            except:
                progress = 0
            if progress > 100:
                progress = 100
            if progress < 0:
                progress = 0
            status = "Pending"
            if progress == 100:
                status = "Done"
            elif progress > 0:
                status = "In Progress"
            self._add_row(self.task_count, title, priority, progress, status, "TBD")
            self._refresh_summary()

    def handle_complete_task(self, sender, e):
        if self.grid.SelectedRows.Count == 0:
            MessageBox.Show("Please select a task to mark as done.", "No Selection")
            return
        row = self.grid.SelectedRows[0]
        row.Cells[3].Value = "100"
        row.Cells[4].Value = "Done"
        self._refresh_summary()

    def handle_delete_task(self, sender, e):
        if self.grid.SelectedRows.Count == 0:
            MessageBox.Show("Please select a task to delete.", "No Selection")
            return
        answer = MessageBox.Show("Delete the selected task?", "Confirm Delete", 1)
        if answer == 6:
            idx = self.grid.SelectedRows[0].Index
            self.grid.Rows.RemoveAt(idx)
            self._refresh_summary()

    def handle_clear_done(self, sender, e):
        i = 0
        while i < self.grid.Rows.Count:
            val = self.grid.Rows[i].Cells[4].Value
            if val is not None and str(val) == "Done":
                self.grid.Rows.RemoveAt(i)
            else:
                i = i + 1
        self._refresh_summary()

    def handle_refresh(self, sender, e):
        self._refresh_summary()

    def handle_export(self, sender, e):
        MessageBox.Show("CSV export: " + str(self.grid.Rows.Count) + " rows.", "Export")

    def handle_about(self, sender, e):
        msg = "Naja Python-to-.NET Compiler\n"
        msg = msg + "Version 1.0  |  2025\n\n"
        msg = msg + "WinForms controls tested:\n"
        msg = msg + "  Form, MenuStrip, ToolStrip, TabControl\n"
        msg = msg + "  DataGridView, ProgressBar, Panel, GroupBox\n"
        msg = msg + "  Label, Button, TextBox, ComboBox, CheckBox\n"
        msg = msg + "  StatusStrip, ToolStripMenuItem"
        MessageBox.Show(msg, "About Naja")

    def handle_clear_notes(self, sender, e):
        self.txt_notes.Text = ""

    def handle_copy_notes(self, sender, e):
        if self.txt_notes.Text != "":
            Clipboard.SetText(self.txt_notes.Text)
            self.lbl_status.Text = "Notes copied to clipboard."

    def handle_exit(self, sender, e):
        self.Close()

#  Entry Point 

if __name__ == "__main__":
    Application.EnableVisualStyles()
    form = DashboardForm()
    Application.Run(form)