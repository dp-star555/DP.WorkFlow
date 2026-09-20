using ScintillaNET;

namespace ScriptEngine.WinForms;

partial class RoslynScriptEditorControl
{
    private System.ComponentModel.IContainer components = null!;

    #region 组件设计器生成的代码

    /// <summary>初始化 Scintilla 编辑器的静态布局和基础输入属性。</summary>
    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        _workspace = new TableLayoutPanel();
        _editor = new Scintilla();
        _diagnosticOverview = new DiagnosticOverviewBar();
        SuspendLayout();
        _workspace.SuspendLayout();
        _workspace.ColumnCount = 2;
        _workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 10F));
        _workspace.Controls.Add(_editor, 0, 0);
        _workspace.Controls.Add(_diagnosticOverview, 1, 0);
        _workspace.Dock = DockStyle.Fill;
        _workspace.Margin = Padding.Empty;
        _workspace.Name = "_workspace";
        _workspace.Padding = Padding.Empty;
        _workspace.RowCount = 1;
        _workspace.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        _workspace.Size = new Size(760, 520);
        _workspace.TabIndex = 0;
        _editor.Dock = DockStyle.Fill;
        _editor.HScrollBar = false;
        _editor.IndentWidth = 4;
        _editor.MultipleSelection = false;
        _editor.Name = "_editor";
        _editor.ScrollWidthTracking = true;
        _editor.Size = new Size(760, 520);
        _editor.TabIndex = 0;
        _editor.TabWidth = 4;
        _editor.UseTabs = false;
        _editor.VScrollBar = true;
        _editor.WrapMode = WrapMode.Word;
        _diagnosticOverview.BackColor = Color.FromArgb(30, 30, 30);
        _diagnosticOverview.Dock = DockStyle.Fill;
        _diagnosticOverview.ForeColor = Color.FromArgb(148, 163, 184);
        _diagnosticOverview.Margin = Padding.Empty;
        _diagnosticOverview.TabIndex = 1;
        AutoScaleDimensions = new SizeF(7F, 17F);
        AutoScaleMode = AutoScaleMode.Font;
        Controls.Add(_workspace);
        Name = "RoslynScriptEditorControl";
        Size = new Size(760, 520);
        _workspace.ResumeLayout(false);
        ResumeLayout(false);
    }

    #endregion

    private TableLayoutPanel _workspace = null!;
    private Scintilla _editor = null!;
    private DiagnosticOverviewBar _diagnosticOverview = null!;
}
