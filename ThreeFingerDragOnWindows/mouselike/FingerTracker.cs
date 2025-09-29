using System;
using System.Collections.Generic;
using System.Linq;
using ThreeFingerDragEngine.utils;
using ThreeFingerDragOnWindows.utils;

namespace ThreeFingerDragOnWindows.mouselike;

/// <summary>
/// Tracks Precision Touchpad contacts and maps them to the mouse-like gesture roles
/// (pointer, left button, right button).
/// </summary>
public sealed class FingerTracker
{
    private sealed class ContactState
    {
        public ContactState(TouchpadContact contact)
        {
            ContactId = contact.ContactId;
            Current = contact;
            Active = true;
            SeenThisFrame = true;
            FirstSeen = DateTime.Now;
            LastSeen = FirstSeen;
        }

        public int ContactId { get; }
        public TouchpadContact Current { get; private set; }
        public TouchpadContact? Previous { get; private set; }
        public bool SeenThisFrame { get; set; }
        public bool Active { get; set; }
        public DateTime FirstSeen { get; }
        public DateTime LastSeen { get; set; }

        public void Update(TouchpadContact contact)
        {
            if (!SeenThisFrame)
            {
                Previous = Current;
            }

            Current = contact;
            SeenThisFrame = true;
            Active = true;
            LastSeen = DateTime.Now;
        }

        public bool HasPrevious => Previous.HasValue;
        public Point CurrentPoint => new(Current.X, Current.Y);
        public Point? PreviousPoint => Previous.HasValue ? new Point(Previous.Value.X, Previous.Value.Y) : null;
    }

    private readonly GestureSettings _settings;
    private readonly Dictionary<int, ContactState> _contacts = new();

    private int? _pointerId;
    private int? _leftButtonId;
    private int? _rightButtonId;
    private int? _middleButtonId;
    private Point? _lastPointerPosition;
    private DateTime _lastFingerCountChangeTime;

    private const float SelectionHysteresis = 8f;    // 适合1600坐标系统
    private const float HoldDistanceFactor = 0.15f;
    private const float HoldDistanceMinimum = 30f;   // 降低到适合1600坐标系统
    private const float ScrollSeparationFactor = 0.45f;
    private const float ScrollSeparationMinimum = 20f; // 降低到适合1600坐标系统

    public FingerTracker(GestureSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        Reset();
    }

    public void Reset()
    {
        _contacts.Clear();
        _pointerId = null;
        _leftButtonId = null;
        _rightButtonId = null;
        _middleButtonId = null;
        _lastPointerPosition = null;
        _lastFingerCountChangeTime = DateTime.Now;
        Logger.Log("FingerTracker: Reset completed");
    }

    public GestureResult ProcessContacts(TouchpadContact[] contacts)
    {
        UpdateContactStates(contacts);

        AssignPointer();
        AssignButtons();

        var pointerDelta = CalculatePointerDelta();
        var scrollDelta = Point.Zero; // Scroll wheel behaviour may be added later.

        var buttons = new MouseButtonState
        {
            LeftButton = _leftButtonId.HasValue,
            RightButton = _rightButtonId.HasValue,
            MiddleButton = _middleButtonId.HasValue && _settings.MiddleButtonEnabled
        };

        var result = new GestureResult
        {
            PointerDelta = pointerDelta,
            ButtonStates = buttons,
            ScrollDelta = scrollDelta,
            IsGestureCompleted = AreAllContactsReleased()
        };

        // 移除频繁的日志输出以优化性能

        CleanupInactiveContacts();
        return result;
    }

    private void UpdateContactStates(TouchpadContact[] contacts)
    {
        foreach (var state in _contacts.Values)
        {
            state.SeenThisFrame = false;
        }

        if (contacts == null || contacts.Length == 0)
        {
            foreach (var state in _contacts.Values)
            {
                state.Active = false;
            }

            ReleaseButtons();
            _pointerId = null;
            _lastPointerPosition = null;
            return;
        }

        foreach (var contact in contacts)
        {
            if (!_contacts.TryGetValue(contact.ContactId, out var state))
            {
                state = new ContactState(contact);
                _contacts[contact.ContactId] = state;
                // 新接触点跟踪 - 移除频繁日志
            }
            else
            {
                state.Update(contact);
            }
        }

        var seenIds = new HashSet<int>(contacts.Select(c => c.ContactId));
        foreach (var (contactId, state) in _contacts.ToList())
        {
            if (!seenIds.Contains(contactId))
            {
                state.Active = false;
                state.SeenThisFrame = false;
                state.LastSeen = DateTime.Now;
            }
        }
    }

    private void AssignPointer()
    {
        if (_pointerId.HasValue &&
            _contacts.TryGetValue(_pointerId.Value, out var existing) &&
            existing.Active && existing.SeenThisFrame)
        {
            _lastPointerPosition = existing.CurrentPoint;
            return;
        }

        _pointerId = null;

        var candidates = _contacts.Values
            .Where(state => state.Active && state.SeenThisFrame)
            .OrderBy(state => state.FirstSeen)
            .ThenBy(state => state.ContactId)
            .ToList();

        if (candidates.Count == 0)
        {
            ReleaseButtons();
            _lastPointerPosition = null;
            return;
        }

        if (_lastPointerPosition.HasValue)
        {
            var nearest = candidates
                .OrderBy(state => state.CurrentPoint.DistTo(_lastPointerPosition.Value))
                .First();

            _pointerId = nearest.ContactId;
            _lastPointerPosition = nearest.CurrentPoint;
            // 指针重新分配 - 移除频繁日志
            return;
        }

        var chosen = candidates.First();
        _pointerId = chosen.ContactId;
        _lastPointerPosition = chosen.CurrentPoint;
        // 指针分配 - 移除频繁日志
    }

    private void AssignButtons()
    {
        var previousLeftId = _leftButtonId;
        var previousRightId = _rightButtonId;
        var previousMiddleId = _middleButtonId;

        _leftButtonId = null;
        _rightButtonId = null;
        _middleButtonId = null;

        if (!_pointerId.HasValue ||
            !_contacts.TryGetValue(_pointerId.Value, out var pointerState) ||
            !pointerState.Active)
        {
            return;
        }

        var activeStates = _contacts.Values
            .Where(state => state.Active && state.SeenThisFrame)
            .ToList();

        var activeContactCount = activeStates.Count;

        // 检测三指点击中键（防止与右键冲突）
        if (_settings.MiddleButtonEnabled && activeContactCount == 3)
        {
            var now = DateTime.Now;

            // 检查是否是新的三指触控（稳定时间检查）
            bool isStableThreeFingerTouch = activeStates.All(state =>
                (now - state.FirstSeen).TotalMilliseconds > 50 &&
                (now - state.FirstSeen).TotalMilliseconds < 300);

            if (isStableThreeFingerTouch)
            {
                // 检查三个手指是否相对静止（非滑动手势）
                bool fingersRelativelyStatic = activeStates.All(state =>
                {
                    if (!state.HasPrevious) return true;

                    var movement = state.CurrentPoint.DistTo(state.PreviousPoint.Value);
                    return movement < _settings.JitterOffset * 3f;
                });

                if (fingersRelativelyStatic)
                {
                    // 选择最靠近指针的手指作为中键代表
                    var closestToPointer = activeStates
                        .Where(s => s.ContactId != pointerState.ContactId)
                        .OrderBy(s => s.CurrentPoint.DistTo(pointerState.CurrentPoint))
                        .FirstOrDefault();

                    if (closestToPointer != null)
                    {
                        _middleButtonId = closestToPointer.ContactId;
                        Logger.Log($"FingerTracker: Three-finger middle button detected");
                        return; // 三指中键时不分配左右键
                    }
                }
            }
        }

        // 记录手指数量变化时间，用于防止快速切换时的误触
        if (activeContactCount != _contacts.Values.Count(s => s.Active))
        {
            _lastFingerCountChangeTime = DateTime.Now;
        }

        float bestLeftDx = float.NegativeInfinity;
        float bestLeftDistance = float.MaxValue;
        float bestRightDx = float.PositiveInfinity;
        float bestRightDistance = float.MaxValue;

        bool leftHeld = TryRestoreHeldButton(previousLeftId, pointerState, isLeft: true, ref bestLeftDx, ref bestLeftDistance);
        bool rightHeld = TryRestoreHeldButton(previousRightId, pointerState, isLeft: false, ref bestRightDx, ref bestRightDistance);

        // 防止在三指状态下意外触发左右键
        if (!leftHeld && !rightHeld && activeStates.Count > 0)
        {
            var now = DateTime.Now;

            // 如果是三指或更多，而且手指数量刚变化，暂时不分配左右键
            if (activeContactCount >= 3 && (now - _lastFingerCountChangeTime).TotalMilliseconds < 150)
            {
                return;
            }

            int newNonPointerCount = activeStates.Count(state =>
                state.ContactId != pointerState.ContactId &&
                (now - state.FirstSeen).TotalMilliseconds <= 120);

            if (newNonPointerCount >= 2)
            {
                return;
            }
        }

        foreach (var state in activeStates)
        {
            if (state.ContactId == pointerState.ContactId || IsLikelyScroll(state, pointerState))
            {
                continue;
            }

            var (dx, _, distance) = RelativeToPointer(state, pointerState);

            if (!leftHeld && QualifiesAsLeft(state, pointerState) &&
                ShouldReplaceLeft(dx, distance, ref bestLeftDx, ref bestLeftDistance))
            {
                bestLeftDx = dx;
                bestLeftDistance = distance;
                _leftButtonId = state.ContactId;
                leftHeld = true;
                continue;
            }

            if (!rightHeld && QualifiesAsRight(state, pointerState) &&
                ShouldReplaceRight(dx, distance, ref bestRightDx, ref bestRightDistance))
            {
                bestRightDx = dx;
                bestRightDistance = distance;
                _rightButtonId = state.ContactId;
                rightHeld = true;
            }
        }
    }

    private bool TryRestoreHeldButton(int? previousId, ContactState pointerState, bool isLeft, ref float bestDx, ref float bestDistance)
    {
        if (!previousId.HasValue)
        {
            return false;
        }

        if (!_contacts.TryGetValue(previousId.Value, out var state) || !state.Active)
        {
            return false;
        }

        if (IsLikelyScroll(state, pointerState) || !IsWithinButtonZone(state, pointerState, isLeft))
        {
            return false;
        }

        var (dx, _, distance) = RelativeToPointer(state, pointerState);
        bestDx = dx;
        bestDistance = distance;
        if (isLeft)
        {
            _leftButtonId = state.ContactId;
        }
        else
        {
            _rightButtonId = state.ContactId;
        }
        return true;
    }

    private bool IsWithinButtonZone(ContactState candidate, ContactState pointer, bool isLeft)
    {
        var (dx, dy, distance) = RelativeToPointer(candidate, pointer);

        float holdThreshold = Math.Max(_settings.FingerMinDistance * HoldDistanceFactor, HoldDistanceMinimum);
        float minimalSideOffset = Math.Max(_settings.FingerMinDistance * 0.05f, 1f);
        float verticalTolerance = Math.Max(_settings.FingerVerticalTolerance, 15f) * 1.3f;
        float maxDistance = _settings.FingerMaxDistance * 1.4f;

        bool sideOk = isLeft ? dx < -minimalSideOffset : dx > minimalSideOffset;
        if (!sideOk)
        {
            return false;
        }

        bool withinNarrowCorridor = isLeft ? dx > -holdThreshold : dx < holdThreshold;
        if (withinNarrowCorridor && candidate.HasPrevious)
        {
            var prev = candidate.PreviousPoint!.Value;
            var curr = candidate.CurrentPoint;

            float movementY = Math.Abs(curr.y - prev.y);
            float movementX = Math.Abs(curr.x - prev.x);
            float relaxedMotion = Math.Max(_settings.JitterOffset * 6f, 5f);

            if (movementY > relaxedMotion && movementY > movementX * 1.1f)
            {
                return false;
            }
        }

        return Math.Abs(dy) <= verticalTolerance && distance <= maxDistance;
    }

    private bool IsLikelyScroll(ContactState candidate, ContactState pointer)
    {
        var (dx, dy, distance) = RelativeToPointer(candidate, pointer);

        // 更严格的水平分离限制，防止与右键冲突
        float horizontalSeparation = Math.Abs(dx);
        float scrollHorizontalLimit = Math.Max(_settings.FingerMinDistance * ScrollSeparationFactor * _settings.ScrollStrictness, ScrollSeparationMinimum);
        if (horizontalSeparation > scrollHorizontalLimit)
        {
            return false;
        }

        float pairDistanceLimit = Math.Max(_settings.FingerMaxDistance * 0.4f, scrollHorizontalLimit * 1.5f); // 降低距离限制
        if (distance > pairDistanceLimit)
        {
            return false;
        }

        if (!candidate.HasPrevious || !pointer.HasPrevious)
        {
            return false;
        }

        var prevCandidate = candidate.PreviousPoint!.Value;
        var prevPointer = pointer.PreviousPoint!.Value;

        float candidateMoveX = candidate.CurrentPoint.x - prevCandidate.x;
        float candidateMoveY = candidate.CurrentPoint.y - prevCandidate.y;
        float pointerMoveX = pointer.CurrentPoint.x - prevPointer.x;
        float pointerMoveY = pointer.CurrentPoint.y - prevPointer.y;

        // 提高垂直移动的最小要求
        float minCandidateVertical = Math.Max(5f, _settings.JitterOffset * 6f);
        if (Math.Abs(candidateMoveY) < minCandidateVertical)
        {
            return false;
        }

        // 更严格要求垂直移动比水平移动大
        if (Math.Abs(candidateMoveY) < Math.Abs(candidateMoveX) * _settings.ScrollStrictness * 1.5f)
        {
            return false;
        }

        float minPointerVertical = Math.Max(3f, _settings.JitterOffset * 4f);
        bool pointerMovingVertically = Math.Abs(pointerMoveY) > minPointerVertical &&
                                       Math.Abs(pointerMoveY) >= Math.Abs(pointerMoveX) * 0.9f;

        if (pointerMovingVertically)
        {
            // 检查移动方向是否一致
            if (Math.Sign(candidateMoveY) != Math.Sign(pointerMoveY))
            {
                return false;
            }

            // 更严格的同步性检查
            float diff = Math.Abs(candidateMoveY - pointerMoveY);
            float allowedDiff = Math.Max(10f, Math.Abs(candidateMoveY) * 0.4f);
            if (diff > allowedDiff)
            {
                return false;
            }
        }
        else
        {
            // 指针没有垂直移动时，更严格的水平限制
            float pointerDriftTolerance = Math.Max(1f, _settings.JitterOffset * 1.5f);
            if (Math.Abs(pointerMoveY) > pointerDriftTolerance)
            {
                return false;
            }

            float pointerHorizontalLimit = Math.Max(3f, horizontalSeparation * 0.5f);
            if (Math.Abs(pointerMoveX) > pointerHorizontalLimit)
            {
                return false;
            }
        }

        // 额外检查：如果水平分离过大，不认为是滚动
        if (horizontalSeparation > _settings.FingerMinDistance * 0.7f)
        {
            return false;
        }

        Logger.Log($"FingerTracker: Identified scroll gesture - HorizontalSep: {horizontalSeparation:F1}, " +
                  $"CandidateMove: ({candidateMoveX:F1}, {candidateMoveY:F1}), " +
                  $"PointerMove: ({pointerMoveX:F1}, {pointerMoveY:F1})");

        return true;
    }

    private static bool ShouldReplaceLeft(float candidateDx, float candidateDistance, ref float currentDx, ref float currentDistance)
    {
        if (float.IsNegativeInfinity(currentDx))
        {
            return true;
        }

        bool closerHorizontally = candidateDx > currentDx + SelectionHysteresis;
        bool similarHorizontal = Math.Abs(candidateDx - currentDx) <= SelectionHysteresis && candidateDistance < currentDistance;
        return closerHorizontally || similarHorizontal;
    }

    private static bool ShouldReplaceRight(float candidateDx, float candidateDistance, ref float currentDx, ref float currentDistance)
    {
        if (float.IsPositiveInfinity(currentDx))
        {
            return true;
        }

        bool closerHorizontally = candidateDx < currentDx - SelectionHysteresis;
        bool similarHorizontal = Math.Abs(candidateDx - currentDx) <= SelectionHysteresis && candidateDistance < currentDistance;
        return closerHorizontally || similarHorizontal;
    }

    private bool QualifiesAsLeft(ContactState candidate, ContactState pointer, bool relaxed = false)
    {
        var (dx, dy, _) = RelativeToPointer(candidate, pointer);
        if (dx >= 0)
        {
            return false;
        }

        float horizontalDistance = Math.Abs(dx);
        bool isDragging = _leftButtonId.HasValue; // 检测是否正在拖拽
        float minHorizontal = GetMinimumHorizontalThreshold(relaxed, isLeftButton: true, isDragging: isDragging);
        float maxHorizontal = Math.Max(_settings.FingerMaxDistance, minHorizontal + 10f);
        float verticalTolerance = Math.Max(_settings.FingerVerticalTolerance, 15f);

        return horizontalDistance >= minHorizontal &&
               horizontalDistance <= maxHorizontal &&
               Math.Abs(dy) <= verticalTolerance;
    }

    private bool QualifiesAsRight(ContactState candidate, ContactState pointer, bool relaxed = false)
    {
        var (dx, dy, _) = RelativeToPointer(candidate, pointer);
        if (dx <= 0)
        {
            return false;
        }

        float horizontalDistance = Math.Abs(dx);
        bool isDragging = _rightButtonId.HasValue; // 检测是否正在拖拽
        float minHorizontal = GetMinimumHorizontalThreshold(relaxed, isLeftButton: false, isDragging: isDragging);
        float maxHorizontal = Math.Max(_settings.FingerMaxDistance, minHorizontal + 10f);
        float verticalTolerance = Math.Max(_settings.FingerVerticalTolerance, 15f);

        return horizontalDistance >= minHorizontal &&
               horizontalDistance <= maxHorizontal &&
               Math.Abs(dy) <= verticalTolerance;
    }

    private float GetMinimumHorizontalThreshold(bool relaxed, bool isLeftButton = false, bool isDragging = false)
    {
        // 使用新的自适应距离计算方法
        var adaptiveDistance = _settings.GetAdaptiveMinDistance(isLeftButton, isDragging);
        var baseValue = Math.Max(adaptiveDistance * (relaxed ? 0.35f : 0.5f), 5f);
        return baseValue;
    }

    private (float dx, float dy, float distance) RelativeToPointer(ContactState candidate, ContactState pointer)
    {
        float dx = candidate.Current.X - pointer.Current.X;
        float dy = candidate.Current.Y - pointer.Current.Y;
        float distance = MathF.Sqrt(dx * dx + dy * dy);
        return (dx, dy, distance);
    }

    private Point CalculatePointerDelta()
    {
        if (!_pointerId.HasValue ||
            !_contacts.TryGetValue(_pointerId.Value, out var pointerState) ||
            !pointerState.HasPrevious)
        {
            return Point.Zero;
        }

        var prev = pointerState.PreviousPoint!.Value;
        var curr = pointerState.CurrentPoint;

        float diffX = curr.x - prev.x;
        float diffY = curr.y - prev.y;

        float px = diffX / _settings.ThumbScale;
        float py = diffY / _settings.ThumbScale;

        if (Math.Abs(px) <= _settings.JitterOffset)
        {
            px = 0;
        }
        if (Math.Abs(py) <= _settings.JitterOffset)
        {
            py = 0;
        }

        double dx = _settings.MouseSensitivity * px;
        double dy = _settings.MouseSensitivity * py;

        if (Math.Abs(dx) > 0 && Math.Abs(dx) < 1)
        {
            dx = Math.Sign(dx);
        }
        if (Math.Abs(dy) > 0 && Math.Abs(dy) < 1)
        {
            dy = Math.Sign(dy);
        }

        _lastPointerPosition = curr;
        return new Point((float)dx, (float)dy);
    }

    private bool AreAllContactsReleased()
    {
        return _contacts.Values.All(state => !state.Active);
    }

    private void CleanupInactiveContacts()
    {
        var now = DateTime.Now;
        var staleIds = _contacts
            .Where(kvp => !kvp.Value.Active && (now - kvp.Value.LastSeen).TotalMilliseconds > 250)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var id in staleIds)
        {
            _contacts.Remove(id);
            // 接触点释放 - 移除频繁日志
        }

        if (_pointerId.HasValue && (!_contacts.TryGetValue(_pointerId.Value, out var pointer) || !pointer.Active))
        {
            _pointerId = null;
        }
        if (_leftButtonId.HasValue && (!_contacts.TryGetValue(_leftButtonId.Value, out var left) || !left.Active))
        {
            _leftButtonId = null;
        }
        if (_rightButtonId.HasValue && (!_contacts.TryGetValue(_rightButtonId.Value, out var right) || !right.Active))
        {
            _rightButtonId = null;
        }
        if (_middleButtonId.HasValue && (!_contacts.TryGetValue(_middleButtonId.Value, out var middle) || !middle.Active))
        {
            _middleButtonId = null;
        }
    }

    private void ReleaseButtons()
    {
        _leftButtonId = null;
        _rightButtonId = null;
        _middleButtonId = null;
    }
}
