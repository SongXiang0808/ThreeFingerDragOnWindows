using System;
using ThreeFingerDragOnWindows.utils;

namespace ThreeFingerDragOnWindows.mouselike;

/// <summary>
/// 滚轮处理器，负责处理双指滚动手势
/// 移植自MouseLikeTouchPad_I2C驱动的滚轮算法
/// </summary>
public class ScrollProcessor
{
    private readonly GestureSettings _settings;
    private Point _totalScrollDistance;
    private DateTime _lastScrollTime;
    private bool _isScrolling;

    // 滚轮灵敏度参数
    private const float SCROLL_SENSITIVITY = 0.5f;
    private const float SCROLL_THRESHOLD = 15f; // 最小滚动距离阈值
    private const int SCROLL_COOLDOWN_MS = 50; // 滚动冷却时间

    public ScrollProcessor(GestureSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        Reset();
    }

    /// <summary>
    /// 重置滚轮处理器状态
    /// </summary>
    public void Reset()
    {
        _totalScrollDistance = Point.Zero;
        _lastScrollTime = DateTime.Now;
        _isScrolling = false;
        Logger.Log("ScrollProcessor: Reset completed");
    }

    /// <summary>
    /// 处理滚轮手势
    /// </summary>
    /// <param name="fingerPos1">手指1位置</param>
    /// <param name="fingerPos2">手指2位置</param>
    /// <param name="lastFingerPos1">上一帧手指1位置</param>
    /// <param name="lastFingerPos2">上一帧手指2位置</param>
    /// <returns>滚轮增量</returns>
    public Point ProcessScroll(Point fingerPos1, Point fingerPos2, Point lastFingerPos1, Point lastFingerPos2)
    {
        // 计算两手指的中心点移动
        var currentCenter = new Point((fingerPos1.x + fingerPos2.x) / 2, (fingerPos1.y + fingerPos2.y) / 2);
        var lastCenter = new Point((lastFingerPos1.x + lastFingerPos2.x) / 2, (lastFingerPos1.y + lastFingerPos2.y) / 2);

        var deltaX = currentCenter.x - lastCenter.x;
        var deltaY = currentCenter.y - lastCenter.y;

        // 累积滚动距离
        _totalScrollDistance.x += deltaX;
        _totalScrollDistance.y += deltaY;

        var now = DateTime.Now;
        var timeSinceLastScroll = (now - _lastScrollTime).TotalMilliseconds;

        // 检查是否应该发送滚轮事件
        Point scrollOutput = Point.Zero;

        // 垂直滚动
        if (Math.Abs(_totalScrollDistance.y) > SCROLL_THRESHOLD && timeSinceLastScroll > SCROLL_COOLDOWN_MS)
        {
            float scrollAmount = _totalScrollDistance.y * SCROLL_SENSITIVITY / _settings.ThumbScale;
            int scrollTicks = (int)(scrollAmount / 120); // Windows滚轮标准单位

            if (scrollTicks != 0)
            {
                scrollOutput.y = scrollTicks;
                _totalScrollDistance.y = 0; // 重置累积距离
                _lastScrollTime = now;
                _isScrolling = true;

                Logger.Log($"ScrollProcessor: Vertical scroll ticks: {scrollTicks}");
            }
        }

        // 水平滚动
        if (Math.Abs(_totalScrollDistance.x) > SCROLL_THRESHOLD && timeSinceLastScroll > SCROLL_COOLDOWN_MS)
        {
            float scrollAmount = _totalScrollDistance.x * SCROLL_SENSITIVITY / _settings.ThumbScale;
            int scrollTicks = (int)(scrollAmount / 120);

            if (scrollTicks != 0)
            {
                scrollOutput.x = scrollTicks;
                _totalScrollDistance.x = 0;
                _lastScrollTime = now;
                _isScrolling = true;

                Logger.Log($"ScrollProcessor: Horizontal scroll ticks: {scrollTicks}");
            }
        }

        // 如果一段时间没有滚动，重置状态
        if (timeSinceLastScroll > 500) // 500ms超时
        {
            if (_isScrolling)
            {
                _isScrolling = false;
                Logger.Log("ScrollProcessor: Scroll session ended");
            }
        }

        return scrollOutput;
    }

    /// <summary>
    /// 检查是否正在滚动
    /// </summary>
    public bool IsScrolling => _isScrolling;

    /// <summary>
    /// 发送滚轮事件到系统
    /// </summary>
    /// <param name="scrollDelta">滚轮增量</param>
    public static void SendScrollEvent(Point scrollDelta)
    {
        if (scrollDelta.y != 0)
        {
            // 垂直滚轮
            MouseOperations.SendWheelEvent(0, (int)(scrollDelta.y * 120));
        }

        if (scrollDelta.x != 0)
        {
            // 水平滚轮
            MouseOperations.SendWheelEvent((int)(scrollDelta.x * 120), 0);
        }
    }
}