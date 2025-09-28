using System;
using System.Runtime.InteropServices;

namespace ThreeFingerDragOnWindows.utils;

internal static class MouseBlocker
{
    private const int WH_MOUSE_LL = 14;
    private const uint LLMHF_INJECTED = 0x00000001;
    private const uint LLMHF_LOWER_IL_INJECTED = 0x00000002;

    private static IntPtr _hookHandle = IntPtr.Zero;
    private static LowLevelMouseProc? _hookProc;
    private static bool _hookRequested;
    private static bool _shouldSuppress;

    public static void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            if (_hookHandle == IntPtr.Zero)
            {
                _hookProc = HookCallback;
                var moduleHandle = GetModuleHandle(null);
                _hookHandle = SetWindowsHookEx(WH_MOUSE_LL, _hookProc, moduleHandle, 0);
                if (_hookHandle == IntPtr.Zero)
                {
                    Logger.Log($"MouseBlocker: Failed to install hook - error {Marshal.GetLastWin32Error()}");
                }
                else
                {
                    Logger.Log("MouseBlocker: Hook installed");
                }
            }

            if (_hookHandle != IntPtr.Zero)
            {
                _hookRequested = true;
                Logger.Log("MouseBlocker: Enabled");
            }
            else
            {
                _hookRequested = false;
                Logger.Log("MouseBlocker: Enable requested but hook is not active");
            }
        }
        else
        {
            _hookRequested = false;
            _shouldSuppress = false;

            if (_hookHandle != IntPtr.Zero)
            {
                if (!UnhookWindowsHookEx(_hookHandle))
                {
                    Logger.Log($"MouseBlocker: Failed to remove hook - error {Marshal.GetLastWin32Error()}");
                }
                else
                {
                    Logger.Log("MouseBlocker: Hook removed");
                }

                _hookHandle = IntPtr.Zero;
                _hookProc = null;
            }

            Logger.Log("MouseBlocker: Disabled");
        }
    }

    public static void SetSuppressionActive(bool active)
    {
        if (!_hookRequested || _hookHandle == IntPtr.Zero)
        {
            _shouldSuppress = false;
            return;
        }

        if (_shouldSuppress != active)
        {
            _shouldSuppress = active;
            Logger.Log(active ? "MouseBlocker: Suppression active" : "MouseBlocker: Suppression cleared");
        }
    }

    private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0 || !_hookRequested || !_shouldSuppress || _hookHandle == IntPtr.Zero)
        {
            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
        var flags = data.flags;
        var injected = (flags & (LLMHF_INJECTED | LLMHF_LOWER_IL_INJECTED)) != 0;
        if (injected)
        {
            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        return new IntPtr(1);
    }

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
