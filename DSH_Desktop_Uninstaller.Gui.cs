using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Drawing;
using System.Windows.Forms;
using System.Windows.Forms.VisualStyles;

partial class DSHDesktopUninstaller
{

#region GUI (RetentionForm)
    private class ConfirmForm : Form
    {
        private Label lblCurrentOp;
        private ProgressBar progress;
        private TextBox txtLog;

        public ConfirmForm(string message)
        {
            Text = "\u786e\u8ba4\u5378\u8f7d";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(580, 420);
            Font = new Font("Microsoft YaHei UI", 9F);

            TextBox txt = new TextBox();
            txt.Multiline = true;
            txt.ReadOnly = true;
            txt.ScrollBars = ScrollBars.Both;
            txt.WordWrap = true;
            txt.Text = message;
            txt.SetBounds(12, 12, 556, 336);
            txt.SelectionStart = 0;
            Controls.Add(txt);

            Button btnOk = new Button();
            btnOk.Text = "\u786e\u5b9a";
            btnOk.DialogResult = DialogResult.OK;
            btnOk.SetBounds(350, 360, 100, 30);
            Controls.Add(btnOk);

            Button btnCancel = new Button();
            btnCancel.Text = "\u53d6\u6d88";
            btnCancel.DialogResult = DialogResult.Cancel;
            btnCancel.SetBounds(458, 360, 100, 30);
            Controls.Add(btnCancel);

            AcceptButton = btnOk;
            CancelButton = btnCancel;
        }

        public void SwitchToProgress()
        {
            Controls.Clear();
            Text = "DSH \u5378\u8f7d\u8fdb\u5ea6";
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = true;
            ClientSize = new Size(580, 420);

            lblCurrentOp = new Label();
            lblCurrentOp.Text = "\u5f53\u524d\u64cd\u4f5c\uff1a\u51c6\u5907\u5f00\u59cb...";
            lblCurrentOp.AutoSize = false;
            lblCurrentOp.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            lblCurrentOp.SetBounds(12, 12, 556, 24);
            Controls.Add(lblCurrentOp);

            progress = new ProgressBar();
            progress.Style = ProgressBarStyle.Marquee;
            progress.MarqueeAnimationSpeed = 30;
            progress.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            progress.SetBounds(12, 42, 556, 20);
            Controls.Add(progress);

            txtLog = new TextBox();
            txtLog.Multiline = true;
            txtLog.ReadOnly = true;
            txtLog.ScrollBars = ScrollBars.Both;
            txtLog.WordWrap = false;
            txtLog.BackColor = System.Drawing.Color.Black;
            txtLog.ForeColor = System.Drawing.Color.Lime;
            txtLog.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            txtLog.SetBounds(12, 70, 556, 336);
            Controls.Add(txtLog);
        }

        public void SetCurrentOp(string op)
        {
            try
            {
                if (lblCurrentOp != null) lblCurrentOp.Text = "\u5f53\u524d\u64cd\u4f5c\uff1a" + op;
            }
            catch
            {
            }
        }

        public void Append(string message)
        {
            try
            {
                if (txtLog == null) return;
                txtLog.AppendText(message + Environment.NewLine);
                txtLog.SelectionStart = txtLog.TextLength;
                txtLog.ScrollToCaret();
                string t = message.Trim();
                if (t.StartsWith("[") || t.StartsWith("Command:") || t.StartsWith("Command result:"))
                {
                    SetCurrentOp(t);
                }
            }
            catch
            {
            }
        }
    }
    class RetentionForm : Form
    {
        private class PresetListItem
        {
            public string Folder;
            public string Display;
            public string Label;

            public PresetListItem(string folder, string display, string label)
            {
                Folder = folder;
                Display = display;
                Label = label;
            }

            public override string ToString()
            {
                return Label;
            }
        }

        private class PluginListItem
        {
            public string Package;
            public string Label;

            public PluginListItem(string package, string label)
            {
                Package = package;
                Label = label;
            }

            public override string ToString()
            {
                return Label;
            }
        }
        private class SkillListItem
        {
            public string Name;
            public string Label;

            public SkillListItem(string name, string label)
            {
                Name = name;
                Label = label;
            }

            public override string ToString()
            {
                return Label;
            }
        }
        private class GrayableCheckBox : CheckBox
          {
              protected override void OnPaint(PaintEventArgs e)
              {
                  if (Enabled)
                  {
                      base.OnPaint(e);
                      return;
                  }

                  using (SolidBrush bg = new SolidBrush(Parent != null ? Parent.BackColor : BackColor))
                  {
                      e.Graphics.FillRectangle(bg, ClientRectangle);
                  }

                  CheckBoxState state;
                  switch (CheckState)
                  {
                      case CheckState.Checked:
                          state = CheckBoxState.CheckedDisabled;
                          break;
                      case CheckState.Indeterminate:
                          state = CheckBoxState.MixedDisabled;
                          break;
                      default:
                          state = CheckBoxState.UncheckedDisabled;
                          break;
                  }

                  Size glyphSize = CheckBoxRenderer.GetGlyphSize(e.Graphics, state);
                  Point boxLocation = new Point(0, Math.Max(0, (Height - glyphSize.Height) / 2));
                  CheckBoxRenderer.DrawCheckBox(e.Graphics, boxLocation, state);

                  Rectangle textBounds = new Rectangle(
                      glyphSize.Width + 4,
                      0,
                      Math.Max(0, Width - glyphSize.Width - 4),
                      Height);
                  TextRenderer.DrawText(e.Graphics, Text, Font, textBounds, SystemColors.GrayText,
                      TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
              }
          }

        private GrayableCheckBox chkPresets;
        private CheckedListBox clbPresets;
        private CheckBox chkRuntime;
        private GrayableCheckBox chkPlugins;
        private CheckedListBox clbPlugins;
        private GrayableCheckBox chkSkills;
        private CheckedListBox clbSkills;
        private CheckBox chkChatData;
        private CheckBox chkAppSettings;
        private CheckBox chkModelConfig;
        private CheckBox chkOtherUserData;
        private RadioButton rbDetectRunning;
        private RadioButton rbDefault;
        private bool updatingSkillState;
        private bool updatingPresetState;
        private bool hasSkills;
        private bool updatingPluginState;
        private bool hasPresets;
        private bool hasPlugins;

        public bool KeepAgentPresets { get { return chkPresets.CheckState != CheckState.Unchecked; } }
        public bool KeepRuntime { get { return chkRuntime.Checked || chkPlugins.CheckState != CheckState.Unchecked; } }
        public bool KeepChatData { get { return chkChatData.Checked; } }
        public bool KeepAppSettings { get { return chkAppSettings.Checked; } }
        public bool KeepModelConfig { get { return chkModelConfig.Checked; } }
        public bool KeepOtherUserData { get { return chkOtherUserData.Checked; } }
        public bool KeepPlugins { get { return chkPlugins.CheckState != CheckState.Unchecked; } }
        public bool KeepSkills { get { return chkSkills.CheckState != CheckState.Unchecked; } }
        public List<string> KeepPresetNames
        {
            get
            {
                if (chkPresets.CheckState == CheckState.Unchecked)
                {
                    return new List<string>();
                }

                List<string> names = new List<string>();
                foreach (object item in clbPresets.CheckedItems)
                {
                    PresetListItem preset = item as PresetListItem;
                    if (preset != null && !string.IsNullOrEmpty(preset.Folder))
                    {
                        names.Add(preset.Folder);
                    }
                }
                return names;
            }
        }
        public List<string> KeepPluginNames
        {
            get
            {
                if (chkPlugins.CheckState == CheckState.Unchecked)
                {
                    return new List<string>();
                }

                List<string> names = new List<string>();
                foreach (object item in clbPlugins.CheckedItems)
                {
                    PluginListItem plugin = item as PluginListItem;
                    if (plugin != null && !string.IsNullOrEmpty(plugin.Package))
                    {
                        names.Add(plugin.Package);
                    }
                }
                return names;
            }
        }
        public List<string> KeepSkillNames
        {
            get
            {
                if (chkSkills.CheckState == CheckState.Unchecked)
                {
                    return new List<string>();
                }

                List<string> names = new List<string>();
                foreach (object item in clbSkills.CheckedItems)
                {
                    SkillListItem skill = item as SkillListItem;
                    if (skill != null && !string.IsNullOrEmpty(skill.Name))
                    {
                        names.Add(skill.Name);
                    }
                }
                return names;
            }
        }
        public bool UseDetectedRunningDsh
        {
            get { return rbDetectRunning.Checked; }
        }

        private void SetAllItems(CheckedListBox list, bool isChecked)
        {
            for (int i = 0; i < list.Items.Count; i++)
            {
                list.SetItemChecked(i, isChecked);
            }
        }

        private void UpdateParentState(CheckedListBox list, GrayableCheckBox parent, bool hasItems, ref bool updating, bool autoCheckRuntime)
        {
            if (updating) return;
            updating = true;
            try
            {
                int total = list.Items.Count;
                if (total > 0)
                {
                    int checkedCount = list.CheckedItems.Count;
                    if (checkedCount == 0)
                    {
                        parent.CheckState = CheckState.Unchecked;
                    }
                    else if (checkedCount == total)
                    {
                        parent.CheckState = CheckState.Checked;
                    }
                    else
                    {
                        parent.CheckState = CheckState.Indeterminate;
                    }
                }
                if (autoCheckRuntime && parent.CheckState != CheckState.Unchecked)
                {
                    chkRuntime.Checked = true;
                }
                list.Enabled = parent.CheckState != CheckState.Unchecked && hasItems;
            }
            finally
            {
                updating = false;
            }
        }

        private void SetAllPresetItems(bool isChecked) { SetAllItems(clbPresets, isChecked); }
        private void UpdatePresetParentState() { UpdateParentState(clbPresets, chkPresets, hasPresets, ref updatingPresetState, false); }
        private void SetAllPluginItems(bool isChecked) { SetAllItems(clbPlugins, isChecked); }
        private void UpdatePluginParentState() { UpdateParentState(clbPlugins, chkPlugins, hasPlugins, ref updatingPluginState, true); }
        private void SetAllSkillItems(bool isChecked) { SetAllItems(clbSkills, isChecked); }
        private void UpdateSkillParentState() { UpdateParentState(clbSkills, chkSkills, hasSkills, ref updatingSkillState, false); }

        private void ToggleCheckState(object sender, EventArgs e)
        {
            GrayableCheckBox chk = sender as GrayableCheckBox;
            if (chk == null) return;
            chk.CheckState = chk.CheckState == CheckState.Checked ? CheckState.Unchecked : CheckState.Checked;
        }

        private void WireParentChecked(GrayableCheckBox chk, CheckedListBox clb, Func<bool> hasItems, Func<bool> getUpdating, Action<bool> setUpdating, bool autoCheckRuntime)
        {
            chk.Click += ToggleCheckState;
            chk.CheckStateChanged += delegate
            {
                if (getUpdating()) return;
                setUpdating(true);
                try
                {
                    if (chk.CheckState == CheckState.Checked)
                    {
                        SetAllItems(clb, true);
                        if (autoCheckRuntime) chkRuntime.Checked = true;
                    }
                    else if (chk.CheckState == CheckState.Unchecked)
                    {
                        SetAllItems(clb, false);
                    }
                    clb.Enabled = chk.CheckState != CheckState.Unchecked && hasItems();
                }
                finally
                {
                    setUpdating(false);
                }
            };
        }

        private void WireItemCheck(CheckedListBox clb, GrayableCheckBox chk, Func<bool> hasItems, Func<bool> getUpdating, Action<bool> setUpdating, bool autoCheckRuntime)
        {
            clb.ItemCheck += delegate(object sender, ItemCheckEventArgs e)
            {
                if (getUpdating()) return;
                int total = clb.Items.Count;
                if (total == 0) return;

                int checkedCount = clb.CheckedItems.Count;
                if (e.NewValue == CheckState.Checked) checkedCount++;
                else if (e.NewValue == CheckState.Unchecked && clb.CheckedIndices.Contains(e.Index)) checkedCount--;

                CheckState state;
                if (checkedCount == 0) state = CheckState.Unchecked;
                else if (checkedCount == total) state = CheckState.Checked;
                else state = CheckState.Indeterminate;

                if (chk.CheckState != state)
                {
                    setUpdating(true);
                    try { chk.CheckState = state; }
                    finally { setUpdating(false); }
                }
                if (autoCheckRuntime && chk.CheckState != CheckState.Unchecked) chkRuntime.Checked = true;
                clb.Enabled = chk.CheckState != CheckState.Unchecked && hasItems();
            };
        }
        private void DrawCheckedListBoxItem(object sender, DrawItemEventArgs e, CheckedListBox list)
        {
            if (e.Index < 0) return;

            bool enabled = list.Enabled;
            bool isChecked = list.GetItemChecked(e.Index);

            using (SolidBrush bg = new SolidBrush(enabled ? SystemColors.Window : SystemColors.Control))
            {
                e.Graphics.FillRectangle(bg, e.Bounds);
            }

            Rectangle checkRect = new Rectangle(e.Bounds.X + 2, e.Bounds.Y + (e.Bounds.Height - 13) / 2, 13, 13);
            ButtonState state;
            if (!enabled)
            {
                state = isChecked ? (ButtonState.Checked | ButtonState.Inactive) : ButtonState.Inactive;
            }
            else
            {
                state = isChecked ? ButtonState.Checked : ButtonState.Normal;
            }
            ControlPaint.DrawCheckBox(e.Graphics, checkRect, state);

            Rectangle textRect = new Rectangle(e.Bounds.X + 20, e.Bounds.Y, e.Bounds.Width - 22, e.Bounds.Height);
            Color textColor = enabled ? SystemColors.WindowText : SystemColors.GrayText;
            TextRenderer.DrawText(e.Graphics, list.Items[e.Index].ToString(), e.Font, textRect, textColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        public void SetRetentionOptions(RetentionOptions o)
        {
            if (o == null) o = new RetentionOptions();
            chkRuntime.Checked = o.Runtime;
            chkChatData.Checked = o.ChatData;
            chkAppSettings.Checked = o.AppSettings;
            chkModelConfig.Checked = o.ModelConfig;
            chkOtherUserData.Checked = o.OtherUserData;

            updatingPresetState = true;
            try
            {
                if (o.Presets)
                {
                    if (o.PresetNames == null || o.PresetNames.Count == 0)
                    {
                        SetAllPresetItems(true);
                        chkPresets.CheckState = CheckState.Checked;
                    }
                    else
                    {
                        SetAllPresetItems(false);
                        HashSet<string> folderNames = new HashSet<string>(o.PresetNames, StringComparer.OrdinalIgnoreCase);
                        for (int i = 0; i < clbPresets.Items.Count; i++)
                        {
                            PresetListItem item = clbPresets.Items[i] as PresetListItem;
                            if (item != null &&
                            (folderNames.Contains(item.Folder) ||
                             (!string.IsNullOrEmpty(item.Display) && folderNames.Contains(item.Display))))
                            {
                                clbPresets.SetItemChecked(i, true);
                            }
                        }
                    }
                }
                else
                {
                    SetAllPresetItems(false);
                    chkPresets.CheckState = CheckState.Unchecked;
                }
            }
            finally
            {
                updatingPresetState = false;
            }

            updatingPluginState = true;
            try
            {
                if (o.Plugins)
                {
                    if (o.PluginNames == null || o.PluginNames.Count == 0)
                    {
                        SetAllPluginItems(true);
                        chkPlugins.CheckState = CheckState.Checked;
                        chkRuntime.Checked = true;
                    }
                    else
                    {
                        SetAllPluginItems(false);
                        HashSet<string> packageNames = new HashSet<string>(o.PluginNames, StringComparer.OrdinalIgnoreCase);
                        for (int i = 0; i < clbPlugins.Items.Count; i++)
                        {
                            PluginListItem item = clbPlugins.Items[i] as PluginListItem;
                            if (item != null && packageNames.Contains(item.Package))
                            {
                                clbPlugins.SetItemChecked(i, true);
                            }
                        }
                    }
                }
                else
                {
                    SetAllPluginItems(false);
                    chkPlugins.CheckState = CheckState.Unchecked;
                }
            }
            finally
            {
                updatingPluginState = false;
            }

            updatingSkillState = true;
            try
            {
                if (o.Skills)
                {
                    if (o.SkillNames == null || o.SkillNames.Count == 0)
                    {
                        SetAllSkillItems(true);
                        chkSkills.CheckState = CheckState.Checked;
                    }
                    else
                    {
                        SetAllSkillItems(false);
                        HashSet<string> skillSet = new HashSet<string>(o.SkillNames, StringComparer.OrdinalIgnoreCase);
                        for (int i = 0; i < clbSkills.Items.Count; i++)
                        {
                            SkillListItem item = clbSkills.Items[i] as SkillListItem;
                            if (item != null && skillSet.Contains(item.Name))
                            {
                                clbSkills.SetItemChecked(i, true);
                            }
                        }
                    }
                }
                else
                {
                    SetAllSkillItems(false);
                    chkSkills.CheckState = CheckState.Unchecked;
                }
            }
            finally
            {
                updatingSkillState = false;
            }

            UpdatePresetParentState();
            UpdatePluginParentState();
            UpdateSkillParentState();
        }
        public RetentionForm()
        {
            Text = "DSH 桌面端卸载确认";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = true;
            ClientSize = new Size(520, 650);
            Font = new Font("Microsoft YaHei UI", 9F);
            // High-DPI: scale the fixed-pixel layout proportionally on 125%/150%
            // displays (Option A — lightweight; avoids absolute-position drift).
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            int variantCount = DSHDesktopUninstaller.DetectedVariantLabels.Count;
            if (variantCount < 1) variantCount = 1;
            int labelHeight = variantCount * 20;
            int titleY = 10 + labelHeight + 6;
            int descY = titleY + 36;
            int modeY = descY + 54;
            int grpY = modeY + 78;
            int btnY = grpY + 390 + 6;
            ClientSize = new Size(520, btnY + 30 + 20);
            Label lblCurrentDsh = new Label();

            lblCurrentDsh.Text = "当前DSH: " + DSHDesktopUninstaller.DetectedVariantLabel;
            lblCurrentDsh.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            lblCurrentDsh.AutoSize = false;
            lblCurrentDsh.AutoEllipsis = false;
            lblCurrentDsh.SetBounds(22, 10, 476, labelHeight);

            Label lblTitle = new Label();
            lblTitle.Text = "确定要卸载 DSH / DeepSeek Harness 桌面端吗？";
            lblTitle.Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold);
            lblTitle.AutoSize = false;
            lblTitle.SetBounds(22, titleY, 476, 30);

            Label lblDesc = new Label();
            lblDesc.Text = "将删除程序、更新器、缓存、快捷方式、注册表和 DSH 用户数据。\r\n默认不保留用户数据，可在下方勾选需要保留的项目。";
            lblDesc.AutoSize = false;
            lblDesc.SetBounds(22, descY, 476, 48);

            GroupBox grpMode = new GroupBox();
            grpMode.Text = "卸载模式";
            grpMode.SetBounds(22, modeY, 476, 72);

            rbDetectRunning = new RadioButton();
            string runningDir = DSHDesktopUninstaller.DetectedRunningDshDir;
            rbDetectRunning.Text = string.IsNullOrEmpty(runningDir)
                ? "程序识别卸载（未检测到运行中的 DSH，将回退默认定位）"
                : "程序识别卸载（当前运行：" + runningDir + "）";
            rbDetectRunning.SetBounds(14, 20, 448, 24);
            rbDetectRunning.Checked = !string.IsNullOrEmpty(runningDir);

            rbDefault = new RadioButton();
            rbDefault.Text = "默认卸载（按注册表/常见安装路径检测）";
            rbDefault.SetBounds(14, 46, 448, 22);
            rbDefault.Checked = !rbDetectRunning.Checked;

            grpMode.Controls.Add(rbDetectRunning);
            grpMode.Controls.Add(rbDefault);

            GroupBox grp = new GroupBox();
            grp.Text = "可选保留项";
            grp.SetBounds(22, grpY, 476, 390);

            Panel pnlOptions = new Panel();
            pnlOptions.SetBounds(8, 20, 458, 358);
            pnlOptions.AutoScroll = true;

            chkPresets = new GrayableCheckBox();
            chkPresets.ThreeState = true;
            chkPresets.AutoCheck = false;
            chkPresets.Text = "保留预设（按名称保留）";
            chkPresets.SetBounds(18, 24, 440, 24);

              clbPresets = new CheckedListBox();
              WireParentChecked(chkPresets, clbPresets, () => hasPresets, () => updatingPresetState, v => updatingPresetState = v, false);
            clbPresets.SetBounds(38, 50, 420, 70);
            clbPresets.CheckOnClick = true;
            clbPresets.IntegralHeight = false;
            clbPresets.DrawMode = DrawMode.OwnerDrawFixed;
            clbPresets.DrawItem += delegate(object sender, DrawItemEventArgs e)
            {
                DrawCheckedListBoxItem(sender, e, clbPresets);
            };
            WireItemCheck(clbPresets, chkPresets, () => hasPresets, () => updatingPresetState, v => updatingPresetState = v, false);

              List<PresetInfo> detected = DSHDesktopUninstaller.DetectAgentPresets();
            hasPresets = detected.Count > 0;
            if (detected.Count == 0)
            {
                clbPresets.Items.Add(new PresetListItem("", "", "（未检测到预设）"));
                clbPresets.Enabled = false;
                chkPresets.Enabled = false;
            }
            else
            {
                foreach (PresetInfo preset in detected)
                {
                    string label = string.Equals(preset.FolderName, preset.DisplayName, StringComparison.OrdinalIgnoreCase)
                        ? preset.DisplayName
                        : preset.DisplayName + " (" + preset.FolderName + ")";
                    clbPresets.Items.Add(new PresetListItem(preset.FolderName, preset.DisplayName, label));
                }
            }

            chkChatData = new CheckBox();
            chkChatData.Text = "保留聊天数据（.dsh\\sessions 对话记录）";
            chkChatData.SetBounds(18, 126, 440, 24);

            chkPlugins = new GrayableCheckBox();
            chkPlugins.ThreeState = true;
            chkPlugins.AutoCheck = false;
            chkPlugins.Text = "保留插件（按名称保留，自动保留运行时）";
            chkPlugins.SetBounds(18, 154, 440, 24);

              clbPlugins = new CheckedListBox();
              WireParentChecked(chkPlugins, clbPlugins, () => hasPlugins, () => updatingPluginState, v => updatingPluginState = v, true);
            clbPlugins.SetBounds(38, 180, 420, 120);
            clbPlugins.CheckOnClick = true;
            clbPlugins.IntegralHeight = false;
            clbPlugins.HorizontalScrollbar = true;
            clbPlugins.DrawMode = DrawMode.OwnerDrawFixed;
            clbPlugins.DrawItem += delegate(object sender, DrawItemEventArgs e)
            {
                DrawCheckedListBoxItem(sender, e, clbPlugins);
            };
            WireItemCheck(clbPlugins, chkPlugins, () => hasPlugins, () => updatingPluginState, v => updatingPluginState = v, true);

              List<PluginInfo> detectedPlugins = DSHDesktopUninstaller.DetectPlugins();
            hasPlugins = detectedPlugins.Count > 0;
            if (detectedPlugins.Count == 0)
            {
                clbPlugins.Items.Add(new PluginListItem("", "（未检测到插件）"));
                clbPlugins.Enabled = false;
                chkPlugins.Enabled = false;
            }
            else
            {
                foreach (PluginInfo plugin in detectedPlugins)
                {
                    clbPlugins.Items.Add(new PluginListItem(plugin.PackageName, plugin.DisplayName));
                }
            }

            chkSkills = new GrayableCheckBox();
            chkSkills.ThreeState = true;
            chkSkills.AutoCheck = false;
            chkSkills.Text = "保留 skills（按名称保留）";
            chkSkills.SetBounds(18, 310, 440, 24);

              clbSkills = new CheckedListBox();
              WireParentChecked(chkSkills, clbSkills, () => hasSkills, () => updatingSkillState, v => updatingSkillState = v, false);
            clbSkills.SetBounds(38, 336, 420, 90);
            clbSkills.CheckOnClick = true;
            clbSkills.IntegralHeight = false;
            clbSkills.HorizontalScrollbar = true;
            clbSkills.DrawMode = DrawMode.OwnerDrawFixed;
            clbSkills.DrawItem += delegate(object sender, DrawItemEventArgs e)
            {
                DrawCheckedListBoxItem(sender, e, clbSkills);
            };
            WireItemCheck(clbSkills, chkSkills, () => hasSkills, () => updatingSkillState, v => updatingSkillState = v, false);

              List<SkillInfo> detectedSkills = DSHDesktopUninstaller.DetectSkills();
            hasSkills = detectedSkills.Count > 0;
            if (detectedSkills.Count == 0)
            {
                clbSkills.Items.Add(new SkillListItem("", "（未检测到 skills）"));
                clbSkills.Enabled = false;
                chkSkills.Enabled = false;
            }
            else
            {
                foreach (SkillInfo skill in detectedSkills)
                {
                    clbSkills.Items.Add(new SkillListItem(skill.Name, skill.DisplayName));
                }
            }


            chkAppSettings = new CheckBox();
            chkAppSettings.Text = "保留应用设置（settings.yaml）";
            chkAppSettings.SetBounds(18, 432, 440, 24);

            chkModelConfig = new CheckBox();
            chkModelConfig.Text = "保留模型配置与凭据（.credentials.yaml + settings.yaml 模型部分）";
            chkModelConfig.SetBounds(18, 460, 440, 24);

            chkOtherUserData = new CheckBox();
            chkOtherUserData.Text = "保留其他 .dsh 数据（graph-memory/storages/super-injector 等）";
            chkOtherUserData.SetBounds(18, 488, 440, 24);

            chkRuntime = new CheckBox();
            chkRuntime.Text = "保留 .dsh-runtime（DSH CLI 运行时）";
            chkRuntime.SetBounds(18, 516, 440, 24);

            pnlOptions.Controls.Add(chkPresets);
            pnlOptions.Controls.Add(clbPresets);
            pnlOptions.Controls.Add(chkChatData);
            pnlOptions.Controls.Add(chkPlugins);
            pnlOptions.Controls.Add(clbPlugins);
            pnlOptions.Controls.Add(chkSkills);
            pnlOptions.Controls.Add(clbSkills);
            pnlOptions.Controls.Add(chkAppSettings);
            pnlOptions.Controls.Add(chkModelConfig);
            pnlOptions.Controls.Add(chkOtherUserData);
            pnlOptions.Controls.Add(chkRuntime);
            grp.Controls.Add(pnlOptions);

            Button btnOk = new Button();
            btnOk.Text = "卸载";
            btnOk.DialogResult = DialogResult.OK;
            btnOk.SetBounds(260, btnY, 100, 30);

            Button btnCancel = new Button();
            btnCancel.Text = "取消";
            btnCancel.DialogResult = DialogResult.Cancel;
            btnCancel.SetBounds(370, btnY, 100, 30);

            Controls.Add(lblCurrentDsh);
            Controls.Add(lblTitle);
            Controls.Add(lblDesc);
            Controls.Add(grpMode);
            Controls.Add(grp);
            Controls.Add(btnOk);
            Controls.Add(btnCancel);

            AcceptButton = btnOk;
            CancelButton = btnCancel;
        }
    }

#endregion
}
