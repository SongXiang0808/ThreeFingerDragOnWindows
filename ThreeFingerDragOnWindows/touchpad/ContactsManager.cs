using System;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ThreeFingerDragEngine.utils;
using ThreeFingerDragOnWindows.utils;
using ThreeFingerDragOnWindows.mouselike;
using WinRT.Interop;
using WinUICommunity;

namespace ThreeFingerDragOnWindows.touchpad;

public class ContactsManager{
    private readonly HandlerWindow _source;

    private readonly IntPtr _hwnd;
    private IntPtr _oldWndProc;

    // MouseLike手势引擎
    private readonly MouseLikeGestureEngine _mouseLikeGestureEngine;
    private bool _rawInputInitialized;
    private bool _exclusiveRawInputRegistered;

    /// <summary>
    /// MouseLike手势引擎访问接口
    /// </summary>
    public MouseLikeGestureEngine MouseLikeGestureEngine => _mouseLikeGestureEngine;

    public ContactsManager(HandlerWindow source){
        _source = source;

        _hwnd = WindowNative.GetWindowHandle(_source);
        _oldWndProc = Interop.SetWndProc(_hwnd, WindowProcess);

        // 初始化MouseLike手势引擎
        _mouseLikeGestureEngine = new MouseLikeGestureEngine();
        _mouseLikeGestureEngine.EnabledChanged += OnMouseLikeModeChanged;

        // 立即从SettingsData加载设置
        LoadMouseLikeSettings();

        // 强制启用MouseLike模式进行调试
    }

    public void InitializeSource(){
        var touchpadExists = TouchpadHelper.Exists();
        var inputReceiverInstalled = UpdateRawInputRegistration(_mouseLikeGestureEngine.IsEnabled);
        _rawInputInitialized = true;

        _source.OnTouchpadInitialized(touchpadExists, inputReceiverInstalled);
    }

    // WindowProc Listener
    private IntPtr WindowProcess(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam){
        switch(message){
            case TouchpadHelper.WM_INPUT:
                var (contacts, count) = TouchpadHelper.ParseInput(lParam);

                // 如果MouseLike模式启用，处理触摸事件但不传播给原生系统
                if (_mouseLikeGestureEngine.IsEnabled)
                {
                    Logger.Log($"ContactsManager: Intercepted {count} contacts for MouseLike mode (blocking system)");
                    ReceiveTouchpadContacts(contacts, count);

                    // 重要：不调用DefWindowProc，阻止消息传播到系统和原生触摸板处理
                    break;
                }
                else
                {
                    // MouseLike模式未启用，正常处理和传播
                    ReceiveTouchpadContacts(contacts, count);
                    break;
                }
            case TouchpadHelper.WM_INPUT_DEVICE_CHANGE:
                _source.OnTouchpadInitialized(TouchpadHelper.Exists(), true);
                break;
        }

        return Interop.CallWindowProc(_oldWndProc, hwnd, message, wParam, lParam);
    }


    // Contacts managements
    private List<TouchpadContact> _lastContacts = new();
    private uint _targetContactCount;

    private void ReceiveTouchpadContacts(List<TouchpadContact> contacts, uint count){
        if(contacts == null || contacts.Count == 0){
            Logger.Log("Receiving empty contacts with cC=" + count);
            if (_mouseLikeGestureEngine.IsEnabled)
            {
                _mouseLikeGestureEngine.ProcessContacts(Array.Empty<TouchpadContact>());
                MouseBlocker.SetSuppressionActive(false);
            }
            else
            {
                _source.OnTouchpadContact(new List<TouchpadContact>());
            }
            return;
        }

        // Regular contact list
        if(count == contacts.Count){
            Logger.Log("+ Receiving regular contact list: " +  string.Join(", ", contacts.Select(c => c.ToString())));

            // 处理触摸板接触点
            ProcessTouchpadContacts(contacts);

            _lastContacts.Clear();
            return;
        }

        // Partial contact list (always sent after an incomplete contact list)
        if(count == 0){
            Logger.Log("Receiving partial contact list: " + string.Join(", ", contacts.Select(c => c.ToString())));
            _lastContacts.AddRange(contacts);
            _lastContacts = RemoveDuplicates(_lastContacts);

            if(_targetContactCount == 0){
                Logger.Log("[WARNING] Target contact count not received yet through an invalid contact list.");
                return;
            }

            if(_lastContacts.Count > _targetContactCount){
                Logger.Log("[WARNING] LastContact list has more contacts than expected: " + string.Join(", ", _lastContacts.Select(c => c.ToString())));
                _lastContacts = _lastContacts.Take((int) _targetContactCount).ToList();
                ProcessTouchpadContacts(_lastContacts);
                _lastContacts.Clear();

            }
            if(_lastContacts.Count == _targetContactCount){
                Logger.Log("+ LastContact list has correct length: " + string.Join(", ", _lastContacts.Select(c => c.ToString())));
                ProcessTouchpadContacts(_lastContacts);
                _lastContacts.Clear();
            }
            return;
        }

        // Old partial contact list has not been submitted yet : duplicating
        if(_lastContacts.Count != 0){
            Logger.Log("[WARNING] New incomplete contact list received while old lastContacts not empty: " + contacts.Count);

            if(_lastContacts.Count < _targetContactCount){
                var lastContact = _lastContacts.Last();
                var maxId = _lastContacts.Max(c => c.ContactId);
                for(int i = 1; i <= _targetContactCount - _lastContacts.Count; i++){
                    _lastContacts.Add(new TouchpadContact(maxId + i, lastContact.X, lastContact.Y));
                }
                Logger.Log("+ Duplicated last contact to fulfil list: " + string.Join(", ", _lastContacts.Select(c => c.ToString())));
            }else if(_lastContacts.Count > _targetContactCount){
                Logger.Log("[WARNING] LastContact list has more contacts than expected: " + string.Join(", ", _lastContacts.Select(c => c.ToString())));
                _lastContacts = _lastContacts.Take((int) _targetContactCount).ToList();
            }

            Logger.Log("+ LastContact list has correct length: " + string.Join(", ", _lastContacts.Select(c => c.ToString())));

            ProcessTouchpadContacts(_lastContacts);
            _lastContacts.Clear();
        }

        // Regular contact list with more contacts than expected (unlikely to happen)
        if(count <= contacts.Count){
            Logger.Log("[WARNING] Received contact list with more contacts than expected: " + string.Join(", ", contacts.Select(c => c.ToString())));
            contacts = contacts.Take((int) count).ToList();
            Logger.Log("+ Contact list has been clamped: " + string.Join(", ", contacts.Select(c => c.ToString())));
            ProcessTouchpadContacts(contacts);
            _lastContacts.Clear();
            return;
        }

        // Here, 0 < contacts.Length < count and lastContacts is empty: incomplete contact list
        _targetContactCount = count;
        _lastContacts = contacts;
        Logger.Log("Receiving incomplete contact count, waiting for partial contacts: " + string.Join(", ", contacts.Select(c => c.ToString())));
    }

    private List<TouchpadContact> RemoveDuplicates(List<TouchpadContact> contacts){
        var uniqueContacts = new List<TouchpadContact>();
        foreach(var contact in contacts){
            if(!uniqueContacts.Any(c => c.ContactId == contact.ContactId)){
                uniqueContacts.Add(contact);
            }
        }
        if(uniqueContacts.Count != contacts.Count){
            Logger.Log("[WARNING] Duplicate contacts ID in list. Removing duplicates: " + string.Join(", ", uniqueContacts.Select(c => c.ToString())));
        }
        return uniqueContacts;
    }

    /// <summary>
    /// 处理触摸板接触点 - 完全支持MouseLike模式，屏蔽原生行为
    /// </summary>
    /// <param name="contacts">接触点列表</param>
    private void ProcessTouchpadContacts(List<TouchpadContact> contacts)
    {
        // 如果MouseLike模式启用，完全使用MouseLike手势引擎，屏蔽所有原生触摸板行为
        if (_mouseLikeGestureEngine.IsEnabled)
        {
            Logger.Log($"ContactsManager: Routing {contacts?.Count ?? 0} contacts to MouseLike engine (blocking native behavior)");

            // 确保MouseLike引擎能够处理接触点数据
            if (contacts != null && contacts.Count > 0)
            {
                // 启用MouseLike模式标志，确保其他组件知道当前处于MouseLike模式
                _mouseLikeGestureEngine.ProcessContacts(contacts.ToArray());
                Logger.Log($"ContactsManager: Successfully processed {contacts.Count} contacts in MouseLike mode");
            }
            else
            {
                // 处理空的接触点数据（手指离开）
                _mouseLikeGestureEngine.ProcessContacts(new TouchpadContact[0]);
                Logger.Log("ContactsManager: Processed empty contacts (fingers up) in MouseLike mode");
            }

            // 重要：不调用原生触摸板处理，完全屏蔽Windows原生触摸板行为
            // 这样就不会有单击、双击等原生触摸板事件
            return;
        }

        // 否则使用原来的三指拖拽功能
        Logger.Log($"ContactsManager: Routing {contacts.Count} contacts to ThreeFingerDrag engine");
        _source.OnTouchpadContact(contacts);
    }

    /// <summary>
    /// 从SettingsData加载MouseLike设置
    /// </summary>
    private void OnMouseLikeModeChanged(bool isEnabled)
    {
        Logger.Log($"ContactsManager: MouseLike mode changed -> {isEnabled}");
        if (!_rawInputInitialized)
        {
            return;
        }

        var registrationOk = UpdateRawInputRegistration(isEnabled);
        if (!registrationOk)
        {
            Logger.Log("ContactsManager: Failed to update raw input registration during mode toggle");
        }
    }

    private bool UpdateRawInputRegistration(bool useExclusive)
    {
        bool registrationOk;

        if (useExclusive)
        {
            registrationOk = TouchpadHelper.RegisterExclusiveInput(_hwnd);
            if (!registrationOk)
            {
                var exclusiveError = Marshal.GetLastWin32Error();
                Logger.Log($"ContactsManager: Exclusive raw input registration failed (error={exclusiveError}), falling back to shared mode");

                registrationOk = TouchpadHelper.RegisterInput(_hwnd);
                if (!registrationOk)
                {
                    var sharedError = Marshal.GetLastWin32Error();
                    Logger.Log($"ContactsManager: Shared raw input registration failed (error={sharedError})");
                }

                _exclusiveRawInputRegistered = false;
            }
            else
            {
                _exclusiveRawInputRegistered = true;
            }
        }
        else
        {
            registrationOk = TouchpadHelper.RegisterInput(_hwnd);
            if (!registrationOk)
            {
                var sharedError = Marshal.GetLastWin32Error();
                Logger.Log($"ContactsManager: Raw input registration update failed (shared mode, error={sharedError})");
            }
            _exclusiveRawInputRegistered = false;
        }

        if (registrationOk)
        {
            Logger.Log($"ContactsManager: Raw input registration updated (exclusive={_exclusiveRawInputRegistered})");
        }

        return registrationOk;
    }    

    private void LoadMouseLikeSettings()

    {

        try

        {

            var settingsData = App.SettingsData;

            if (settingsData != null && _mouseLikeGestureEngine != null)

            {

                _mouseLikeGestureEngine.IsEnabled = settingsData.MouseLikeModeEnabled;



                var clampedSensitivity = Math.Clamp(settingsData.MouseLikeSensitivity, 0.5f, 5f);

                var clampedJitter = Math.Clamp(settingsData.MouseLikeJitterOffset, 0.1f, 2f);



                bool normalizationNeeded = Math.Abs(clampedSensitivity - settingsData.MouseLikeSensitivity) > 0.001f ||

                                            Math.Abs(clampedJitter - settingsData.MouseLikeJitterOffset) > 0.001f;



                settingsData.MouseLikeSensitivity = clampedSensitivity;

                settingsData.MouseLikeJitterOffset = clampedJitter;



                _mouseLikeGestureEngine.UpdateSettings(clampedSensitivity, settingsData.MouseLikeThumbScale, false);

                _mouseLikeGestureEngine.Settings.JitterOffset = clampedJitter;



                if (normalizationNeeded)

                {

                    try

                    {

                        settingsData.save();

                        Logger.Log("ContactsManager: MouseLike settings normalized and saved");

                    }

                    catch (Exception saveEx)

                    {

                        Logger.Log($"ContactsManager: Failed to persist MouseLike normalization - {saveEx.Message}");

                    }

                }



                Logger.Log($"ContactsManager: MouseLike settings loaded - Enabled: {_mouseLikeGestureEngine.IsEnabled}, " +

                          $"Sensitivity: {_mouseLikeGestureEngine.Settings.MouseSensitivity}, " +

                          $"ThumbScale: {_mouseLikeGestureEngine.Settings.ThumbScale}, " +

                          $"JitterOffset: {_mouseLikeGestureEngine.Settings.JitterOffset}");

            }

            else

            {

                Logger.Log("ContactsManager: Unable to load MouseLike settings - SettingsData or engine is null");

            }

        }

        catch (Exception ex)

        {

            Logger.Log($"ContactsManager: Error loading MouseLike settings - {ex.Message}");

        }

    }








}

















