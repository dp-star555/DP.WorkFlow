namespace ModernUI.WinForms;

/// <summary>补充 net8.0 WinForms 尚未公开命名成员的 WinEvent 常量。</summary>
internal static class ModernAccessibilityEvents
{
    // EVENT_OBJECT_LIVEREGIONCHANGED，供 UIA/MSAA bridge 发布实时区域内容变化。
    public const AccessibleEvents LiveRegionChanged = (AccessibleEvents)0x8019;
}
