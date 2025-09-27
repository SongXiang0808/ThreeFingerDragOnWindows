using System;
using ThreeFingerDragEngine.utils;
using ThreeFingerDragOnWindows.utils;

namespace ThreeFingerDragOnWindows.mouselike;

/// <summary>
/// MouseLike手势引擎主类
/// 整合手指跟踪和手势识别功能
/// </summary>
public class MouseLikeGestureEngine
{
    private readonly FingerTracker _fingerTracker;
    private readonly ScrollProcessor _scrollProcessor;
    private readonly GestureSettings _settings;
    private bool _isEnabled;

    // 按键状态跟踪
    private MouseButtonState _lastButtonStates;

    public MouseLikeGestureEngine()
    {
        _settings = new GestureSettings();
        _fingerTracker = new FingerTracker(_settings);
        _scrollProcessor = new ScrollProcessor(_settings);
        _isEnabled = false;

        Logger.Log("MouseLikeGestureEngine: Initialized");
    }

    /// <summary>
    /// 是否启用MouseLike模式
    /// </summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            _isEnabled = value;
            if (value)
            {
                _fingerTracker.Reset();
                Logger.Log("MouseLikeGestureEngine: Enabled");
            }
            else
            {
                Logger.Log("MouseLikeGestureEngine: Disabled");
            }
        }
    }

    /// <summary>
    /// 手势设置
    /// </summary>
    public GestureSettings Settings => _settings;

    /// <summary>
    /// 处理触摸板接触点数据
    /// </summary>
    /// <param name="contacts">接触点数组</param>
    public void ProcessContacts(TouchpadContact[] contacts)
    {
        if (!_isEnabled)
        {
            Logger.Log("MouseLikeGestureEngine: Disabled, skipping contact processing");
            return;
        }

        Logger.Log($"MouseLikeGestureEngine: *** PROCESSING {contacts?.Length ?? 0} contacts ***");

        if (contacts != null && contacts.Length > 0)
        {
            for (int i = 0; i < contacts.Length; i++)
            {
                Logger.Log($"  >> Contact {i}: ID={contacts[i].ContactId}, Pos=({contacts[i].X:F1}, {contacts[i].Y:F1})");
            }
        }
        else
        {
            Logger.Log("  >> No contacts (fingers up)");
        }

        try
        {
            var gestureResult = _fingerTracker.ProcessContacts(contacts);

            // 处理手势结果
            ProcessGestureResult(gestureResult);
        }
        catch (Exception ex)
        {
            Logger.Log($"MouseLikeGestureEngine: Error processing contacts - {ex.Message}");
        }
    }

    /// <summary>
    /// 处理手势识别结果
    /// </summary>
    /// <param name="result">手势结果</param>
    private void ProcessGestureResult(GestureResult result)
    {
        // 处理指针移动
        if (result.PointerDelta.x != 0 || result.PointerDelta.y != 0)
        {
            Logger.Log($"MouseLikeGestureEngine: Moving cursor by ({result.PointerDelta.x}, {result.PointerDelta.y})");
            MouseOperations.ShiftCursorPosition(result.PointerDelta.x, result.PointerDelta.y);
        }

        // 处理按键状态
        ProcessButtonStates(result.ButtonStates);

        // 处理滚轮
        if (result.ScrollDelta.x != 0 || result.ScrollDelta.y != 0)
        {
            ScrollProcessor.SendScrollEvent(result.ScrollDelta);
            Logger.Log($"MouseLikeGestureEngine: Scroll delta X:{result.ScrollDelta.x}, Y:{result.ScrollDelta.y}");
        }

        // 处理手势完成状态
        if (result.IsGestureCompleted)
        {
            Logger.Log("MouseLikeGestureEngine: Gesture completed");
        }
    }

    /// <summary>
    /// 处理按键状态变化
    /// </summary>
    /// <param name="buttonStates">当前按键状态</param>
    private void ProcessButtonStates(MouseButtonState buttonStates)
    {
        // 检查左键状态变化
        if (buttonStates.LeftButton != _lastButtonStates.LeftButton)
        {
            if (buttonStates.LeftButton)
            {
                MouseOperations.MouseDown(MouseOperations.MOUSEEVENTF_LEFTDOWN);
                Logger.Log("MouseLikeGestureEngine: Left button DOWN");
            }
            else
            {
                MouseOperations.MouseUp(MouseOperations.MOUSEEVENTF_LEFTUP);
                Logger.Log("MouseLikeGestureEngine: Left button UP");
            }
        }

        // 检查右键状态变化
        if (buttonStates.RightButton != _lastButtonStates.RightButton)
        {
            if (buttonStates.RightButton)
            {
                MouseOperations.MouseDown(MouseOperations.MOUSEEVENTF_RIGHTDOWN);
                Logger.Log("MouseLikeGestureEngine: Right button DOWN");
            }
            else
            {
                MouseOperations.MouseUp(MouseOperations.MOUSEEVENTF_RIGHTUP);
                Logger.Log("MouseLikeGestureEngine: Right button UP");
            }
        }

        // 检查中键状态变化
        if (buttonStates.MiddleButton != _lastButtonStates.MiddleButton)
        {
            if (buttonStates.MiddleButton)
            {
                MouseOperations.MouseDown(MouseOperations.MOUSEEVENTF_MIDDLEDOWN);
                Logger.Log("MouseLikeGestureEngine: Middle button DOWN");
            }
            else
            {
                MouseOperations.MouseUp(MouseOperations.MOUSEEVENTF_MIDDLEUP);
                Logger.Log("MouseLikeGestureEngine: Middle button UP");
            }
        }

        // 更新上次按键状态
        _lastButtonStates = buttonStates;
    }

    /// <summary>
    /// 重置手势引擎状态
    /// </summary>
    public void Reset()
    {
        _fingerTracker.Reset();
        _scrollProcessor.Reset();
        _lastButtonStates = new MouseButtonState(); // 重置按键状态
        Logger.Log("MouseLikeGestureEngine: Reset completed");
    }

    /// <summary>
    /// 更新设置参数
    /// </summary>
    /// <param name="sensitivity">鼠标敏感度</param>
    /// <param name="thumbScale">手指缩放</param>
    public void UpdateSettings(float? sensitivity = null, float? thumbScale = null)
    {
        if (sensitivity.HasValue)
        {
            _settings.MouseSensitivity = Math.Max(0.1f, Math.Min(3.0f, sensitivity.Value));
            Logger.Log($"MouseLikeGestureEngine: Mouse sensitivity updated to {_settings.MouseSensitivity}");
        }

        if (thumbScale.HasValue)
        {
            _settings.ThumbScale = Math.Max(0.5f, Math.Min(2.0f, thumbScale.Value));
            Logger.Log($"MouseLikeGestureEngine: Thumb scale updated to {_settings.ThumbScale}");
        }
    }
}