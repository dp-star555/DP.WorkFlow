using ModernUI.WinForms;
using DP.WorkFlow.UI;

namespace DP.WorkFlow.UI.WinForms;

/// <summary>
/// Block 输入/输出映射编辑窗口。
/// <para>静态 Tab、表格和命令栏位于 Designer.cs；列定义和候选集合由运行时模型动态生成。</para>
/// </summary>
public sealed partial class WorkflowBlockMappingEditorDialog : Form
{
    /// <summary>共享层映射编辑模型。设计器实例中为空，仅运行时构造函数会赋值。</summary>
    private WorkflowBlockMappingEditorModel? _model;

    /// <summary>供 Visual Studio WinForms 设计器创建窗口。</summary>
    public WorkflowBlockMappingEditorDialog()
    {
        InitializeComponent();
        okButton.Click += (_, _) => Commit();
    }

    /// <summary>使用指定 Block 映射模型初始化窗口。</summary>
    public WorkflowBlockMappingEditorDialog(
        WorkflowBlockMappingEditorModel model,
        string startNodeId)
        : this()
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        Text = $"Block 映射 - {model.Block.Title}";

        ConfigureInputGrid(startNodeId);
        ConfigureOutputGrid();
        LoadRows();
        WorkflowWinFormsStyle.Apply(this);
    }

    /// <summary>配置父流程到子流程的输入映射列。</summary>
    private void ConfigureInputGrid(string startNodeId)
    {
        var model = RequireModel();
        inputGrid.Columns.Clear();
        inputGrid.Columns.Add(TextColumn("Target", "子变量"));
        inputGrid.Columns.Add(EnumColumn<E_BlockInputSource>("Source", "来源"));
        inputGrid.Columns.Add(TextColumn("Literal", "固定值"));
        inputGrid.Columns.Add(TextColumn("Variable", "父变量"));
        var candidates = TryCandidates(() => model.GetParentCandidates(startNodeId));
        inputGrid.Columns.Add(BindingColumn("Binding", "父节点绑定", candidates));
    }

    /// <summary>配置子流程到父流程的输出映射列。</summary>
    private void ConfigureOutputGrid()
    {
        var model = RequireModel();
        outputGrid.Columns.Clear();
        outputGrid.Columns.Add(TextColumn("Target", "父变量"));
        outputGrid.Columns.Add(EnumColumn<E_BlockOutputSource>("Source", "来源"));
        outputGrid.Columns.Add(TextColumn("Variable", "子变量"));
        var candidates = TryCandidates(model.GetChildCandidates);
        outputGrid.Columns.Add(BindingColumn("Binding", "子节点绑定", candidates));
    }

    /// <summary>把共享模型中的映射行加载到两个 DataGridView。</summary>
    private void LoadRows()
    {
        var model = RequireModel();
        foreach (var row in model.Inputs)
            inputGrid.Rows.Add(row.TargetVariableName, row.Source, row.LiteralValue, row.ParentVariableName, row.ParentBinding?.ToString() ?? string.Empty);
        foreach (var row in model.Outputs)
            outputGrid.Rows.Add(row.TargetVariableName, row.Source, row.ChildVariableName, row.ChildBinding?.ToString() ?? string.Empty);
    }

    /// <summary>结束单元格编辑、校验全部映射并一次性提交。</summary>
    private void Commit()
    {
        if (_model is null)
            return;

        try
        {
            inputGrid.EndEdit();
            outputGrid.EndEdit();
            var inputs = inputGrid.Rows.Cast<DataGridViewRow>()
                .Where(row => !row.IsNewRow)
                .Select((row, index) => new WorkflowBlockInputMappingRow(
                    index,
                    Cell(row, 0),
                    Value<E_BlockInputSource>(row, 1),
                    row.Cells[2].Value,
                    Cell(row, 3),
                    Binding(row, 4)))
                .ToArray();
            var outputs = outputGrid.Rows.Cast<DataGridViewRow>()
                .Where(row => !row.IsNewRow)
                .Select((row, index) => new WorkflowBlockOutputMappingRow(
                    index,
                    Cell(row, 0),
                    Value<E_BlockOutputSource>(row, 1),
                    Cell(row, 2),
                    Binding(row, 3)))
                .ToArray();

            _model.ReplaceAll(inputs, outputs);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or FormatException)
        {
            MessageBox.Show(this, exception.Message, "映射配置无效", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    /// <summary>绑定分析失败时返回空候选，使用户仍可打开并修复旧文档。</summary>
    private static IReadOnlyList<WorkflowBindingCandidate> TryCandidates(
        Func<IReadOnlyList<WorkflowBindingCandidate>> factory)
    {
        try
        {
            return factory();
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return Array.Empty<WorkflowBindingCandidate>();
        }
    }

    /// <summary>执行 Require Model 相关处理。</summary>
    private WorkflowBlockMappingEditorModel RequireModel() =>
        _model ?? throw new InvalidOperationException("映射窗口尚未绑定 WorkflowBlockMappingEditorModel。");

    /// <summary>创建可编辑文本列。</summary>
    private static DataGridViewTextBoxColumn TextColumn(string name, string title) => new()
    {
        Name = name,
        HeaderText = title
    };

    /// <summary>创建由枚举值驱动的下拉列。</summary>
    private static ModernDataGridViewComboBoxColumn EnumColumn<T>(string name, string title) where T : struct, Enum => new()
    {
        Name = name,
        HeaderText = title,
        DataSource = Enum.GetValues<T>()
    };

    /// <summary>创建由强类型绑定候选驱动的下拉列。</summary>
    private static ModernDataGridViewComboBoxColumn BindingColumn(
        string name,
        string title,
        IEnumerable<WorkflowBindingCandidate> candidates)
    {
        var values = new[] { string.Empty }
            .Concat(candidates.Select(item => item.ToBindingKey().ToString()))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return new ModernDataGridViewComboBoxColumn
        {
            Name = name,
            HeaderText = title,
            DataSource = values
        };
    }

    /// <summary>执行 Cell 相关处理。</summary>
    /// <param name="row">“row”参数。</param>
    /// <param name="index">目标元素索引。</param>
    private static string Cell(DataGridViewRow row, int index) =>
        Convert.ToString(row.Cells[index].Value)?.Trim() ?? string.Empty;

    /// <summary>执行 Value 相关处理。</summary>
    /// <param name="row">“row”参数。</param>
    /// <param name="index">目标元素索引。</param>
    private static T Value<T>(DataGridViewRow row, int index) where T : struct, Enum =>
        row.Cells[index].Value is T value ? value : Enum.Parse<T>(Cell(row, index), true);

    /// <summary>解析可为空的持久化绑定键。</summary>
    /// <param name="row">“row”参数。</param>
    /// <param name="index">目标元素索引。</param>
    private static WorkflowBindingKey? Binding(DataGridViewRow row, int index)
    {
        var text = Cell(row, index);
        if (string.IsNullOrWhiteSpace(text))
            return null;
        return WorkflowBindingKey.TryParse(text, out var binding)
            ? binding
            : throw new FormatException($"绑定格式无效：{text}。");
    }
}
