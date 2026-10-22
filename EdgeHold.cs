namespace Clothesline;

/// <summary>
/// 顶边热区要停够 <see cref="Milliseconds"/> 才把绳子拉下来。提前离开就取消。
/// 热键和「新截图瞄一眼」不走这里，仍然马上显示。
/// </summary>
internal sealed class EdgeHold
{
    public const int Milliseconds = 500;

    public bool Pending { get; private set; }

    public void Enter() => Pending = true;

    public void Leave() => Pending = false;

    /// <summary>停够时间。指针已经离开时返回 false，并且这次等待作废。</summary>
    public bool Due()
    {
        bool reveal = Pending;
        Pending = false;
        return reveal;
    }
}
