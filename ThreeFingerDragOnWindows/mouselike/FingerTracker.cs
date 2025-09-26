using System;
using System.Linq;
using ThreeFingerDragEngine.utils;
using ThreeFingerDragOnWindows.utils;

namespace ThreeFingerDragOnWindows.mouselike;

/// <summary>
/// 手指跟踪器，负责手指角色分配和状态管理
/// 移植自MouseLikeTouchPad_I2C驱动的核心手指跟踪逻辑
/// </summary>
public class FingerTracker
{
    private const int MAX_CONTACT_POINTS = 5;
    private readonly GestureSettings _settings;

    // 当前和上一帧的手指状态
    private FingerState[] _currentFingers = new FingerState[MAX_CONTACT_POINTS];
    private FingerState[] _lastFingers = new FingerState[MAX_CONTACT_POINTS];

    // 手指角色索引 (移植自驱动中的nMouse_*_CurrentIndex变量)
    private int _pointerIndex = -1;
    private int _leftButtonIndex = -1;
    private int _rightButtonIndex = -1;
    private int _middleButtonIndex = -1;
    private int _wheelIndex = -1;

    private int _lastPointerIndex = -1;
    private int _lastLeftButtonIndex = -1;
    private int _lastRightButtonIndex = -1;
    private int _lastMiddleButtonIndex = -1;
    private int _lastWheelIndex = -1;

    // 手势状态
    private bool _isWheelMode = false;
    private bool _isWheelModeJudgeEnabled = true;
    private bool _isGestureCompleted = false;
    private DateTime _pointerDefineTime;
    private DateTime _jitterFixStartTime;

    public FingerTracker(GestureSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        Reset();
    }

    /// <summary>
    /// 重置跟踪器状态
    /// </summary>
    public void Reset()
    {
        // 重置所有索引
        _pointerIndex = _leftButtonIndex = _rightButtonIndex = _middleButtonIndex = _wheelIndex = -1;
        _lastPointerIndex = _lastLeftButtonIndex = _lastRightButtonIndex = _lastMiddleButtonIndex = _lastWheelIndex = -1;

        // 重置状态
        _isWheelMode = false;
        _isWheelModeJudgeEnabled = true;
        _isGestureCompleted = false;

        // 清空手指状态数组
        Array.Clear(_currentFingers, 0, _currentFingers.Length);
        Array.Clear(_lastFingers, 0, _lastFingers.Length);

        Logger.Log("FingerTracker: Reset completed");
    }

    /// <summary>
    /// 处理新的触摸接触点
    /// 移植自驱动中的MouseLikeTouchPad_parse函数主逻辑
    /// </summary>
    /// <param name="contacts">触摸接触点数组</param>
    /// <returns>手势识别结果</returns>
    public GestureResult ProcessContacts(TouchpadContact[] contacts)
    {
        if (contacts == null)
        {
            Logger.Log("FingerTracker: Null contacts received");
            return new GestureResult { IsGestureCompleted = true };
        }

        // 保存上一帧状态
        Array.Copy(_currentFingers, _lastFingers, _currentFingers.Length);
        SaveLastIndexes();

        // 更新当前帧状态
        UpdateCurrentFingers(contacts);

        // 执行手指跟踪和角色分配
        TrackFingers();

        // 生成手势结果
        var result = GenerateGestureResult();

        Logger.Log($"FingerTracker: Processed {contacts.Length} contacts, " +
                  $"Pointer: {_pointerIndex}, L: {_leftButtonIndex}, R: {_rightButtonIndex}, " +
                  $"M: {_middleButtonIndex}, Wheel: {_wheelIndex}");

        return result;
    }

    /// <summary>
    /// 保存上一帧的索引状态
    /// </summary>
    private void SaveLastIndexes()
    {
        _lastPointerIndex = _pointerIndex;
        _lastLeftButtonIndex = _leftButtonIndex;
        _lastRightButtonIndex = _rightButtonIndex;
        _lastMiddleButtonIndex = _middleButtonIndex;
        _lastWheelIndex = _wheelIndex;
    }

    /// <summary>
    /// 更新当前帧的手指状态
    /// </summary>
    private void UpdateCurrentFingers(TouchpadContact[] contacts)
    {
        // 重置当前帧索引
        _pointerIndex = _leftButtonIndex = _rightButtonIndex = _middleButtonIndex = _wheelIndex = -1;

        // 清空当前状态数组
        Array.Clear(_currentFingers, 0, _currentFingers.Length);

        // 填充当前接触点
        for (int i = 0; i < Math.Min(contacts.Length, MAX_CONTACT_POINTS); i++)
        {
            var contact = contacts[i];
            _currentFingers[i] = new FingerState(contact.ContactId, new Point(contact.X, contact.Y))
            {
                IsActive = true,
                LastUpdated = DateTime.Now
            };
        }
    }

    /// <summary>
    /// 执行手指跟踪和角色分配
    /// 移植自驱动中的手指追踪逻辑
    /// </summary>
    private void TrackFingers()
    {
        int activeFingerCount = _currentFingers.Count(f => f.IsActive);

        if (activeFingerCount == 0)
        {
            // 所有手指离开
            _isGestureCompleted = true;
            return;
        }

        // 首先尝试从上一帧继承手指角色
        InheritFingerRoles();

        // 如果没有指针手指，尝试分配新的指针
        if (_pointerIndex == -1 && activeFingerCount > 0)
        {
            AssignPointerFinger();
        }
        // 如果指针存在，处理其他手指的角色分配
        else if (_pointerIndex != -1 && !_isWheelMode)
        {
            AssignButtonFingers();
        }
    }

    /// <summary>
    /// 从上一帧继承手指角色
    /// </summary>
    private void InheritFingerRoles()
    {
        for (int i = 0; i < _currentFingers.Length; i++)
        {
            if (!_currentFingers[i].IsActive) continue;

            int contactId = _currentFingers[i].ContactId;

            // 尝试继承指针角色
            if (_lastPointerIndex != -1 &&
                _lastFingers[_lastPointerIndex].ContactId == contactId)
            {
                _pointerIndex = i;
                continue;
            }

            // 尝试继承其他角色
            if (_lastLeftButtonIndex != -1 &&
                _lastFingers[_lastLeftButtonIndex].ContactId == contactId)
            {
                _leftButtonIndex = i;
                continue;
            }

            if (_lastRightButtonIndex != -1 &&
                _lastFingers[_lastRightButtonIndex].ContactId == contactId)
            {
                _rightButtonIndex = i;
                continue;
            }

            if (_lastMiddleButtonIndex != -1 &&
                _lastFingers[_lastMiddleButtonIndex].ContactId == contactId)
            {
                _middleButtonIndex = i;
                continue;
            }

            if (_lastWheelIndex != -1 &&
                _lastFingers[_lastWheelIndex].ContactId == contactId)
            {
                _wheelIndex = i;
                continue;
            }
        }
    }

    /// <summary>
    /// 分配指针手指
    /// 移植自驱动中的指针分配逻辑
    /// </summary>
    private void AssignPointerFinger()
    {
        // 选择第一个有效的接触点作为指针
        for (int i = 0; i < _currentFingers.Length; i++)
        {
            if (_currentFingers[i].IsActive)
            {
                _pointerIndex = i;
                _pointerDefineTime = DateTime.Now;
                Logger.Log($"FingerTracker: Assigned pointer to finger {i}");
                break;
            }
        }
    }

    /// <summary>
    /// 分配按键手指角色
    /// 移植自驱动中的按键分配逻辑
    /// </summary>
    private void AssignButtonFingers()
    {
        if (_pointerIndex == -1) return;

        var pointerPos = _currentFingers[_pointerIndex].Position;

        for (int i = 0; i < _currentFingers.Length; i++)
        {
            if (!_currentFingers[i].IsActive || i == _pointerIndex) continue;

            var fingerPos = _currentFingers[i].Position;
            var (dx, dy, distance) = DistanceCalculator.CalculateDistanceAndDirection(pointerPos, fingerPos);

            // 判断是否在有效距离范围内
            if (!DistanceCalculator.IsWithinValidRange(pointerPos, fingerPos, _settings))
                continue;

            // 根据位置和距离分配角色
            if (_middleButtonIndex == -1 &&
                DistanceCalculator.IsClosed(pointerPos, fingerPos, _settings) &&
                dx < 0)
            {
                // 左侧合拢 - 中键
                _middleButtonIndex = i;
                Logger.Log($"FingerTracker: Assigned middle button to finger {i}");
            }
            else if (_leftButtonIndex == -1 &&
                     DistanceCalculator.IsSeparated(pointerPos, fingerPos, _settings) &&
                     dx < 0)
            {
                // 左侧分开 - 左键
                _leftButtonIndex = i;
                Logger.Log($"FingerTracker: Assigned left button to finger {i}");
            }
            else if (_rightButtonIndex == -1 &&
                     DistanceCalculator.IsWithinValidRange(pointerPos, fingerPos, _settings) &&
                     dx > 0)
            {
                // 右侧 - 右键
                _rightButtonIndex = i;
                Logger.Log($"FingerTracker: Assigned right button to finger {i}");
            }
        }
    }

    /// <summary>
    /// 生成手势识别结果
    /// </summary>
    private GestureResult GenerateGestureResult()
    {
        var result = new GestureResult
        {
            IsGestureCompleted = _isGestureCompleted
        };

        // 计算指针移动
        if (_pointerIndex != -1 && _lastPointerIndex != -1)
        {
            var currentPos = _currentFingers[_pointerIndex].Position;
            var lastPos = _lastFingers[_lastPointerIndex].Position;
            var rawDelta = new Point(currentPos.x - lastPos.x, currentPos.y - lastPos.y);

            // 应用抖动消除和敏感度
            var smoothedDelta = new Point(
                DistanceCalculator.RemoveJitter(rawDelta.x, _settings.JitterOffset),
                DistanceCalculator.RemoveJitter(rawDelta.y, _settings.JitterOffset)
            );

            result.PointerDelta = DistanceCalculator.ApplySensitivity(
                smoothedDelta, _settings.MouseSensitivity, _settings.ThumbScale);
        }

        // 设置按键状态
        result.ButtonStates = new MouseButtonState
        {
            LeftButton = _leftButtonIndex != -1,
            RightButton = _rightButtonIndex != -1,
            MiddleButton = _middleButtonIndex != -1
        };

        return result;
    }
}