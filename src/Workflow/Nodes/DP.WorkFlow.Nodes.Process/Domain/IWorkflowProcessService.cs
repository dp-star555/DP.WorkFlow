namespace DP.WorkFlow;

/// <summary>产品流字段来源。</summary>
public enum E_ProductFlowValueSource { Literal = 0, Binding = 1 }

/// <summary>产品创建请求，字段与旧版节点语义一致。</summary>
public sealed record ProductCreateRequest(string? ProductId, bool AutoGenerateProductId, string Recipe, string? Reason, string StationId, string? SlotId);

/// <summary>产品创建结果，字段与旧版保持一致。</summary>
public sealed record ProductCreateNodeResult(string ProductId, string LotId, string Recipe, string StationId, string? SlotId, string State, int BatchIndex, int BatchTotalCount);

/// <summary>提供产品创建所需的宿主领域能力。</summary>
public interface IWorkflowProcessService
{
    /// <summary>创建产品并放入指定工站/槽位。</summary>
    ValueTask<ProductCreateNodeResult> CreateProductAsync(ProductCreateRequest request, CancellationToken cancellationToken);
}
