namespace Clothesline;

/// <summary>
/// 照片右键菜单是独立弹层。打开时晾衣绳窗口会收到 MouseLeave，不能据此马上收回。
/// 菜单开着保持展开。关掉之后，指针还在绳子上就不收，已经离开才允许原来的收回计时。
/// </summary>
internal sealed class MenuHold
{
    public bool Open { get; private set; }

    public void Opened() => Open = true;

    /// <summary>菜单关掉。返回 true 表示应启动原来的离开收回计时。</summary>
    public bool Closed(bool pointerInside)
    {
        Open = false;
        return !pointerInside;
    }

    /// <summary>鼠标离开或瞄一眼到点时能否收回。菜单开着一律不能。</summary>
    public bool AllowHide(bool pointerInside) => !Open && !pointerInside;
}
