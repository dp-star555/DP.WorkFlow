namespace ModernUI.WinForms;

/// <summary>集中管理选择项的 ImageList、嵌套成员解析与变更通知。</summary>
internal sealed class ModernItemImageSource(Action changed) : IDisposable
{
    private ImageList? _imageList;
    private string _imageKeyMember = string.Empty;
    private string _imageIndexMember = string.Empty;

    public ImageList? ImageList
    {
        get => _imageList;
        set
        {
            if (ReferenceEquals(_imageList, value)) return;
            if (_imageList is not null) _imageList.RecreateHandle -= ImageListChanged;
            _imageList = value;
            if (_imageList is not null) _imageList.RecreateHandle += ImageListChanged;
            changed();
        }
    }

    public string ImageKeyMember
    {
        get => _imageKeyMember;
        set { value ??= string.Empty; if (_imageKeyMember == value) return; _imageKeyMember = value; changed(); }
    }

    public string ImageIndexMember
    {
        get => _imageIndexMember;
        set { value ??= string.Empty; if (_imageIndexMember == value) return; _imageIndexMember = value; changed(); }
    }

    public Image? Resolve(object? item)
    {
        var images = ImageList?.Images;
        if (images is null || item is null) return null;
        if (!string.IsNullOrWhiteSpace(ImageKeyMember))
        {
            var key = Convert.ToString(DataMemberResolver.Resolve(item, ImageKeyMember));
            if (!string.IsNullOrEmpty(key) && images.ContainsKey(key)) return images[key];
        }
        if (!string.IsNullOrWhiteSpace(ImageIndexMember))
        {
            var value = DataMemberResolver.Resolve(item, ImageIndexMember);
            if (value is not null)
            {
                try
                {
                    var index = Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
                    if ((uint)index < (uint)images.Count) return images[index];
                }
                catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException)
                {
                    return null;
                }
            }
        }
        return null;
    }

    public void Dispose()
    {
        if (_imageList is not null) _imageList.RecreateHandle -= ImageListChanged;
    }

    private void ImageListChanged(object? sender, EventArgs e) => changed();
}
