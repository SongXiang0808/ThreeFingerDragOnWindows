using System;
using ThreeFingerDragEngine.utils;
using ThreeFingerDragOnWindows.mouselike;
using ThreeFingerDragOnWindows.utils;

namespace ThreeFingerDragOnWindows.mouselike
{
    /// <summary>
    /// 手指跟踪器 - 完全基于C++驱动MouseLikeTouchPad_parse函数重写
    /// </summary>
    public class FingerTracker
    {
        private const int MAX_CONTACT_POINTS = 5;
        private readonly GestureSettings _settings;

        // 手指角色索引 (完全对应C++驱动的变量)
        private int nMouse_Pointer_CurrentIndex = -1;
        private int nMouse_LButton_CurrentIndex = -1;
        private int nMouse_RButton_CurrentIndex = -1;
        private int nMouse_MButton_CurrentIndex = -1;

        private int nMouse_Pointer_LastIndex = -1;
        private int nMouse_LButton_LastIndex = -1;
        private int nMouse_RButton_LastIndex = -1;
        private int nMouse_MButton_LastIndex = -1;

        // 当前和上一帧的手指状态
        private TouchpadContact[] currentFingers = new TouchpadContact[MAX_CONTACT_POINTS];
        private TouchpadContact[] lastFingers = new TouchpadContact[MAX_CONTACT_POINTS];
        private int currentFingerCount = 0;
        private int lastFingerCount = 0;

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
            nMouse_Pointer_CurrentIndex = -1;
            nMouse_LButton_CurrentIndex = -1;
            nMouse_RButton_CurrentIndex = -1;
            nMouse_MButton_CurrentIndex = -1;

            nMouse_Pointer_LastIndex = -1;
            nMouse_LButton_LastIndex = -1;
            nMouse_RButton_LastIndex = -1;
            nMouse_MButton_LastIndex = -1;

            Array.Clear(currentFingers, 0, currentFingers.Length);
            Array.Clear(lastFingers, 0, lastFingers.Length);
            currentFingerCount = 0;
            lastFingerCount = 0;

            Logger.Log("FingerTracker: Reset completed");
        }

        /// <summary>
        /// 处理新的触摸接触点 - 完全基于C++驱动逻辑
        /// </summary>
        public GestureResult ProcessContacts(TouchpadContact[] contacts)
        {
            Logger.Log($"FingerTracker: Processing {contacts?.Length ?? 0} contacts");

            // 保存上一帧状态
            SaveLastState();

            // 更新当前状态
            UpdateCurrentState(contacts);

            // 核心手指分配逻辑 - 对应C++的MouseLikeTouchPad_parse
            ProcessFingerAssignment();

            // 计算鼠标移动
            var movement = CalculateMovement();

            // 生成手势结果
            var result = new GestureResult
            {
                PointerDelta = new Point { x = movement.dx, y = movement.dy },
                ButtonStates = new MouseButtonState
                {
                    LeftButton = nMouse_LButton_CurrentIndex != -1,
                    RightButton = nMouse_RButton_CurrentIndex != -1,
                    MiddleButton = nMouse_MButton_CurrentIndex != -1
                },
                ScrollDelta = new Point { x = 0, y = 0 },
                IsGestureCompleted = false
            };

            Logger.Log($"FingerTracker: Result - Pointer:{nMouse_Pointer_CurrentIndex}, L:{nMouse_LButton_CurrentIndex}, R:{nMouse_RButton_CurrentIndex}, M:{nMouse_MButton_CurrentIndex}");
            Logger.Log($"FingerTracker: Movement - dx:{movement.dx:F1}, dy:{movement.dy:F1}");
            Logger.Log($"FingerTracker: Buttons - L:{result.ButtonStates.LeftButton}, R:{result.ButtonStates.RightButton}, M:{result.ButtonStates.MiddleButton}");

            return result;
        }

        /// <summary>
        /// 保存上一帧状态
        /// </summary>
        private void SaveLastState()
        {
            Array.Copy(currentFingers, lastFingers, currentFingers.Length);
            lastFingerCount = currentFingerCount;

            nMouse_Pointer_LastIndex = nMouse_Pointer_CurrentIndex;
            nMouse_LButton_LastIndex = nMouse_LButton_CurrentIndex;
            nMouse_RButton_LastIndex = nMouse_RButton_CurrentIndex;
            nMouse_MButton_LastIndex = nMouse_MButton_CurrentIndex;
        }

        /// <summary>
        /// 更新当前状态
        /// </summary>
        private void UpdateCurrentState(TouchpadContact[] contacts)
        {
            Array.Clear(currentFingers, 0, currentFingers.Length);
            currentFingerCount = 0;

            if (contacts != null)
            {
                for (int i = 0; i < Math.Min(contacts.Length, MAX_CONTACT_POINTS); i++)
                {
                    currentFingers[i] = contacts[i];
                }
                currentFingerCount = contacts.Length;
            }

            Logger.Log($"FingerTracker: Updated state - current:{currentFingerCount}, last:{lastFingerCount}");
        }

        /// <summary>
        /// 核心手指分配逻辑 - 完全对应C++驱动的逻辑
        /// </summary>
        private void ProcessFingerAssignment()
        {
            // 1. 如果没有指针且有手指，分配第一个手指为指针
            if (nMouse_Pointer_LastIndex == -1 && currentFingerCount > 0)
            {
                // 找到第一个有效的手指作为指针
                for (int i = 0; i < currentFingerCount; i++)
                {
                    if (IsValidContact(currentFingers[i]))
                    {
                        nMouse_Pointer_CurrentIndex = i;
                        Logger.Log($"FingerTracker: Assigned pointer to finger {i}");
                        break;
                    }
                }
            }
            // 2. 如果指针丢失，重置所有角色
            else if (nMouse_Pointer_CurrentIndex == -1 && nMouse_Pointer_LastIndex != -1)
            {
                Logger.Log("FingerTracker: Pointer lost, resetting all roles");
                nMouse_Pointer_CurrentIndex = -1;
                nMouse_LButton_CurrentIndex = -1;
                nMouse_RButton_CurrentIndex = -1;
                nMouse_MButton_CurrentIndex = -1;
            }
            // 3. 如果有指针，分配其他手指的角色
            else if (nMouse_Pointer_CurrentIndex != -1)
            {
                // 重置按钮角色
                nMouse_LButton_CurrentIndex = -1;
                nMouse_RButton_CurrentIndex = -1;
                nMouse_MButton_CurrentIndex = -1;

                // 分配按钮角色
                AssignButtonRoles();
            }
        }

        /// <summary>
        /// 分配按钮角色 - 完全对应C++驱动的逻辑
        /// </summary>
        private void AssignButtonRoles()
        {
            if (currentFingerCount <= 1) return;

            var pointerContact = currentFingers[nMouse_Pointer_CurrentIndex];
            Logger.Log($"FingerTracker: Pointer at ({pointerContact.X}, {pointerContact.Y})");

            for (int i = 0; i < currentFingerCount; i++)
            {
                // 跳过指针手指和无效手指
                if (i == nMouse_Pointer_CurrentIndex || !IsValidContact(currentFingers[i]))
                    continue;

                // 跳过已分配的手指
                if (i == nMouse_LButton_CurrentIndex || i == nMouse_RButton_CurrentIndex || i == nMouse_MButton_CurrentIndex)
                    continue;

                var fingerContact = currentFingers[i];
                float dx = fingerContact.X - pointerContact.X;
                float dy = fingerContact.Y - pointerContact.Y;
                float distance = (float)Math.Sqrt(dx * dx + dy * dy);

                Logger.Log($"FingerTracker: Finger {i} at ({fingerContact.X}, {fingerContact.Y}), dx:{dx:F1}, dy:{dy:F1}, distance:{distance:F1}");

                // 按C++驱动的逻辑分配角色
                if (nMouse_MButton_CurrentIndex == -1 &&
                    distance > _settings.FingerMinDistance &&
                    distance < _settings.FingerClosedThreshold &&
                    dx < 0)
                {
                    // 左侧合拢 - 中键
                    nMouse_MButton_CurrentIndex = i;
                    Logger.Log($"FingerTracker: Assigned MIDDLE button to finger {i} (closed left, distance:{distance:F1})");
                }
                else if (nMouse_LButton_CurrentIndex == -1 &&
                         distance > _settings.FingerClosedThreshold &&
                         distance < _settings.FingerMaxDistance &&
                         dx < 0)
                {
                    // 左侧分开 - 左键
                    nMouse_LButton_CurrentIndex = i;
                    Logger.Log($"FingerTracker: Assigned LEFT button to finger {i} (separated left, distance:{distance:F1})");
                }
                else if (nMouse_RButton_CurrentIndex == -1 &&
                         distance > _settings.FingerMinDistance &&
                         distance < _settings.FingerMaxDistance &&
                         dx > 0)
                {
                    // 右侧 - 右键
                    nMouse_RButton_CurrentIndex = i;
                    Logger.Log($"FingerTracker: Assigned RIGHT button to finger {i} (right side, distance:{distance:F1})");
                }
            }
        }

        /// <summary>
        /// 检查接触点是否有效
        /// </summary>
        private bool IsValidContact(TouchpadContact contact)
        {
            // 对应C++的 Confidence && TipSwitch 检查
            return contact.ContactId >= 0; // 简化的有效性检查
        }

        /// <summary>
        /// 计算鼠标移动 - 对应C++驱动的移动计算逻辑
        /// </summary>
        private (float dx, float dy) CalculateMovement()
        {
            if (nMouse_Pointer_CurrentIndex == -1 || nMouse_Pointer_LastIndex == -1)
                return (0, 0);

            var currentPointer = currentFingers[nMouse_Pointer_CurrentIndex];
            var lastPointer = lastFingers[nMouse_Pointer_LastIndex];

            float diffX = currentPointer.X - lastPointer.X;
            float diffY = currentPointer.Y - lastPointer.Y;

            // 应用缩放和敏感度
            float px = diffX / _settings.ThumbScale;
            float py = diffY / _settings.ThumbScale;

            // 抖动消除
            if (Math.Abs(px) <= _settings.JitterOffset)
                px = 0;
            if (Math.Abs(py) <= _settings.JitterOffset)
                py = 0;

            // 应用敏感度
            double dx = _settings.MouseSensitivity * px;
            double dy = _settings.MouseSensitivity * py;

            // 精细移动处理
            if (Math.Abs(dx) > 0.5 && Math.Abs(dx) < 1)
                dx = dx > 0 ? 1 : -1;
            if (Math.Abs(dy) > 0.5 && Math.Abs(dy) < 1)
                dy = dy > 0 ? 1 : -1;

            return ((float)dx, (float)dy);
        }
    }
}