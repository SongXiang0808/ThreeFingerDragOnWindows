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
    private Point? _lastPointerPosition;

    private const float SelectionHysteresis = 10f;
    private const float RelaxedDistanceFactor = 0.5f;

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
        _lastPointerPosition = null;
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
            MiddleButton = false
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

        _leftButtonId = null;
        _rightButtonId = null;

        if (!_pointerId.HasValue ||
            !_contacts.TryGetValue(_pointerId.Value, out var pointerState) ||
            !pointerState.Active)
        {
            return;
        }

        float bestLeftDx = float.NegativeInfinity;
        float bestLeftDistance = float.MaxValue;
        float bestRightDx = float.PositiveInfinity;
        float bestRightDistance = float.MaxValue;

        bool leftHeld = TryRestoreHeldButton(previousLeftId, pointerState, isLeft: true, ref bestLeftDx, ref bestLeftDistance);
        bool rightHeld = TryRestoreHeldButton(previousRightId, pointerState, isLeft: false, ref bestRightDx, ref bestRightDistance);

        foreach (var state in _contacts.Values)
        {
            if (!state.Active || state.ContactId == pointerState.ContactId || IsLikelyScroll(state, pointerState))
            {
                continue;
            }

            var (dx, dy, distance) = RelativeToPointer(state, pointerState);

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
        float minDistance = Math.Max(_settings.FingerMinDistance * 0.4f, 12f);
        float verticalTolerance = Math.Max(_settings.FingerVerticalTolerance, 20f) * 1.8f;
        float maxDistance = _settings.FingerMaxDistance * 2.0f;
        bool sideOk = isLeft ? dx < -minDistance : dx > minDistance;
        return sideOk && Math.Abs(dy) <= verticalTolerance && distance <= maxDistance;
    }

    private bool IsLikelyScroll(ContactState candidate, ContactState pointer)
    {
        var (dx, dy, distance) = RelativeToPointer(candidate, pointer);
        float verticalTolerance = Math.Max(_settings.FingerVerticalTolerance, 20f);

        if (Math.Abs(dy) <= verticalTolerance)
        {
            return false;
        }

        if (distance < _settings.FingerMinDistance * 0.6f)
        {
            return false;
        }

        return Math.Abs(dy) > Math.Abs(dx) * 1.2f;
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
        float minHorizontal = GetMinimumHorizontalThreshold(relaxed);
        float maxHorizontal = Math.Max(_settings.FingerMaxDistance, minHorizontal + 10f);
        float verticalTolerance = Math.Max(_settings.FingerVerticalTolerance, 20f);

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
        float minHorizontal = GetMinimumHorizontalThreshold(relaxed);
        float maxHorizontal = Math.Max(_settings.FingerMaxDistance, minHorizontal + 10f);
        float verticalTolerance = Math.Max(_settings.FingerVerticalTolerance, 20f);

        return horizontalDistance >= minHorizontal &&
               horizontalDistance <= maxHorizontal &&
               Math.Abs(dy) <= verticalTolerance;
    }

    private float GetMinimumHorizontalThreshold(bool relaxed)
    {
        var baseValue = Math.Max(_settings.FingerMinDistance, 20f);
        return relaxed ? Math.Max(baseValue * RelaxedDistanceFactor, 15f) : baseValue;
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
    }

    private void ReleaseButtons()
    {
        _leftButtonId = null;
        _rightButtonId = null;
    }
}
