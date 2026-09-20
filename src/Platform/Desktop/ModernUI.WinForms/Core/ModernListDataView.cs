using System.Collections;
using System.ComponentModel;

namespace ModernUI.WinForms;

/// <summary>
/// Owns IList/IListSource resolution and IBindingList subscriptions for selection controls. It does
/// not own selection semantics; each control decides how a changed list affects its current value.
/// </summary>
internal sealed class ModernListDataView : IDisposable
{
    private readonly ListChangedEventHandler _changed;
    private IBindingList? _bindingList;

    public ModernListDataView(ListChangedEventHandler changed) =>
        _changed = changed ?? throw new ArgumentNullException(nameof(changed));

    public object? DataSource { get; private set; }
    public IList? List { get; private set; }

    public IList? Bind(object? dataSource)
    {
        if (ReferenceEquals(DataSource, dataSource)) return List;
        Unsubscribe();
        DataSource = dataSource;
        List = Resolve(dataSource);
        _bindingList = List as IBindingList;
        if (_bindingList is not null) _bindingList.ListChanged += _changed;
        return List;
    }

    public void Observe(object? dataSource)
    {
        Unsubscribe();
        DataSource = dataSource;
        List = Resolve(dataSource);
        _bindingList = List as IBindingList;
        if (_bindingList is not null) _bindingList.ListChanged += _changed;
    }

    public static IList? Resolve(object? dataSource)
    {
        if (dataSource is null) return null;
        if (dataSource is IList list) return list;
        if (dataSource is IListSource listSource) return listSource.GetList();
        throw new ArgumentException("DataSource must implement IList or IListSource.", nameof(dataSource));
    }

    private void Unsubscribe()
    {
        if (_bindingList is not null) _bindingList.ListChanged -= _changed;
        _bindingList = null;
    }

    public void Dispose()
    {
        Unsubscribe();
        DataSource = null;
        List = null;
    }
}
