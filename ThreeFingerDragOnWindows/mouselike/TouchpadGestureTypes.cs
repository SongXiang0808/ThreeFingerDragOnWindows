using System;
using ThreeFingerDragEngine.utils;
using ThreeFingerDragOnWindows.utils;

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
/// 手势设置参数，移植自驱动中的配置
/// </summary>
public class GestureSettings
{
    /// <summary>手指间最小有效距离 (mm -> pixels)</summary>
    public float FingerMinDistance { get; set; } = 12f;

    /// <summary>手指合拢判断阈值距离</summary>
    public float FingerClosedThreshold { get; set; } = 16f;

    /// <summary>鼠标移动敏感度</summary>
    public float MouseSensitivity { get; set; } = 1.0f;

    /// <summary>手指尺寸缩放比例</summary>
    public float ThumbScale { get; set; } = 1.0f;

    /// <summary>抖动消除偏移量</summary>
    public float JitterOffset { get; set; } = 10f;

    /// <summary>按键指针间隔判断时间 (ms)</summary>
    public int ButtonPointerIntervalMs { get; set; } = 300;

    /// <summary>手指稳定间隔时间 - 分开状态 (ms)</summary>
    public int StableIntervalSeparated { get; set; } = 30;

    /// <summary>手指稳定间隔时间 - 合拢状态 (ms)</summary>
    public int StableIntervalClosed { get; set; } = 150;

    /// <summary>计算最大有效距离</summary>
    public float FingerMaxDistance => FingerMinDistance * 4f;
}