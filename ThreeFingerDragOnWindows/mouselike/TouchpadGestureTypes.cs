using System;
using ThreeFingerDragEngine.utils;
using ThreeFingerDragOnWindows.utils;
using ThreeFingerDragOnWindows.touchpad;

namespace ThreeFingerDragOnWindows.mouselike;

/// <summary>
/// 手指在触摸板上的角色定义，移植自MouseLikeTouchPad_I2C驱动
/// </summary>
public enum FingerRole
{
    /// <summary>未分配角色</summary>
    None = -1,
    /// <summary>指针控制</summary>
    Pointer = 0,
    /// <summary>左键</summary>
    LeftButton = 1,
    /// <summary>右键</summary>
    RightButton = 2,
    /// <summary>中键</summary>
    MiddleButton = 3,
    /// <summary>滚轮</summary>
    ScrollWheel = 4
}

/// <summary>
/// 手指状态信息
/// </summary>
public struct FingerState
{
    public int ContactId { get; set; }
    public Point Position { get; set; }
    public FingerRole Role { get; set; }
    public DateTime LastUpdated { get; set; }
    public bool IsActive { get; set; }

    public FingerState(int contactId, Point position)
    {
        ContactId = contactId;
        Position = position;
        Role = FingerRole.None;
        LastUpdated = DateTime.Now;
        IsActive = true;
    }

    public readonly bool IsValid => ContactId >= 0 && IsActive;
}

/// <summary>
/// 手势识别结果
/// </summary>
public struct GestureResult
{
    /// <summary>指针移动增量</summary>
    public Point PointerDelta { get; set; }

    /// <summary>按键状态</summary>
    public MouseButtonState ButtonStates { get; set; }

    /// <summary>滚轮增量</summary>
    public Point ScrollDelta { get; set; }

    /// <summary>手势是否完成</summary>
    public bool IsGestureCompleted { get; set; }
}

/// <summary>
/// 鼠标按键状态
/// </summary>
public struct MouseButtonState
{
    public bool LeftButton { get; set; }
    public bool RightButton { get; set; }
    public bool MiddleButton { get; set; }

    public readonly bool HasAnyButton => LeftButton || RightButton || MiddleButton;
}

/// <summary>
/// 手势设置参数，完全基于C++驱动中的配置，现已支持触摸板尺寸自适应
/// </summary>
public class GestureSettings
{
    /// <summary>手指间最小有效距离 (基于1600x1200坐标系统优化)</summary>
    public float FingerMinDistance { get; set; } = 50f; // 约占宽度的3%，适合1600坐标范围

    /// <summary>鼠标移动敏感度 (对应C++的MouseSensitivity_Value)</summary>
    public float MouseSensitivity { get; set; } = 1.0f; // 对应驱动中的默认值

    /// <summary>手指尺寸缩放比例 (对应C++的thumb_Scale)</summary>
    public float ThumbScale { get; set; } = 1.0f; // 对应驱动中的默认值

    /// <summary>抖动消除偏移量 (基于1600x1200坐标系统优化)</summary>
    public float JitterOffset { get; set; } = 3f; // 增加到适合1600坐标范围的值

    /// <summary>手指最大有效水平距离 (基于1600x1200坐标系统优化)</summary>
    public float FingerMaxDistance { get; set; } = 250f; // 约占宽度的15%，适合1600坐标范围

    /// <summary>允许的垂直偏移量 (基于1600x1200坐标系统优化)</summary>
    public float FingerVerticalTolerance { get; set; } = 150f; // 约占高度的12%，适合1200坐标范围

    /// <summary>是否启用中键模拟</summary>
    public bool MiddleButtonEnabled { get; set; } = false;

    /// <summary>是否启用指针模拟</summary>
    public bool PointerSimulationEnabled { get; set; } = false;

    /// <summary>触摸板信息（用于自适应参数）</summary>
    public TouchpadInfo? TouchpadInfo { get; set; }

    /// <summary>左右手指位置差异补偿因子</summary>
    public float FingerPositionCompensation { get; set; } = 0.8f;

    /// <summary>拖拽时的最小手指距离降低比例</summary>
    public float DragMinDistanceReduction { get; set; } = 0.6f;

    /// <summary>双指滑动检测的严格度</summary>
    public float ScrollStrictness { get; set; } = 1.2f;

    /// <summary>
    /// 根据触摸板信息更新所有相关参数
    /// </summary>
    public void UpdateFromTouchpadInfo(TouchpadInfo touchpadInfo)
    {
        TouchpadInfo = touchpadInfo;

        if (touchpadInfo.IsValid)
        {
            // 基于真实触摸板尺寸计算参数
            touchpadInfo.CalculateFingerDistanceParameters(ThumbScale, out var minDist, out var maxDist);
            FingerMinDistance = minDist;
            FingerMaxDistance = maxDist;

            // 更新抖动偏移量
            JitterOffset = touchpadInfo.GetJitterOffset(0.4f);

            // 更新垂直容差
            FingerVerticalTolerance = touchpadInfo.GetVerticalTolerance();

            Logger.Log($"GestureSettings: Updated from touchpad info - MinDist: {FingerMinDistance:F1}, " +
                      $"MaxDist: {FingerMaxDistance:F1}, JitterOffset: {JitterOffset:F2}, " +
                      $"VerticalTolerance: {FingerVerticalTolerance:F1}");
        }
    }

    /// <summary>
    /// 获取适应性的手指最小距离（考虑手指位置差异）
    /// </summary>
    public float GetAdaptiveMinDistance(bool isLeftButton = false, bool isDragging = false)
    {
        float baseDistance = FingerMinDistance;

        // 左键时考虑手指位置差异（食指比中指短）
        if (isLeftButton)
        {
            baseDistance *= FingerPositionCompensation;
        }

        // 拖拽时降低最小距离要求
        if (isDragging)
        {
            baseDistance *= DragMinDistanceReduction;
        }

        return baseDistance;
    }
}

