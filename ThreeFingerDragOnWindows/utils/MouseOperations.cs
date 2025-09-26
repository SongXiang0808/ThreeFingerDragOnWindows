using System;
using System.Runtime.InteropServices;
using ThreeFingerDragOnWindows.settings;

namespace ThreeFingerDragOnWindows.utils;

public class MouseOperations {
    private const int INPUT_MOUSE = 0;
    private const int MOUSEEVENTF_MOVE = 0x0001;
    public const int MOUSEEVENTF_LEFTDOWN = 0x0002;
    public const int MOUSEEVENTF_LEFTUP = 0x0004;
    public const int MOUSEEVENTF_RIGHTDOWN = 0x0008;
    public const int MOUSEEVENTF_RIGHTUP = 0x0010;
    public const int MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    public const int MOUSEEVENTF_MIDDLEUP = 0x0040;

    // moving the cursor does not work with floating point values
    // decimal parts are kept and then added to be taken in account
    private static float _decimalX;
    private static float _decimalY;


    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out IntMousePoint lpFIntMousePoint);


    public static IntMousePoint GetCursorPosition(){
        IntMousePoint currentIntMousePoint;
        var gotPoint = GetCursorPos(out currentIntMousePoint);
        if(!gotPoint) currentIntMousePoint = new IntMousePoint(0, 0);
        return currentIntMousePoint;
    }

    public static void ShiftCursorPosition(float x, float y){
        var intX = (int) (x + _decimalX);
        var intY = (int) (y + _decimalY);
        _decimalX = x + _decimalX - intX;
        _decimalY = y + _decimalY - intY;

        MoveMouse(intX, intY);
    }

    public static void SetCursorPosition(float x, float y){
        var intX = (int) (x + _decimalX);
        var intY = (int) (y + _decimalY);
        _decimalX = x + _decimalX - intX;
        _decimalY = y + _decimalY - intY;
        var point = GetCursorPosition();

        MoveMouse(intX - point.x, intY - point.y);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, Input[] pInputs, int cbSize);

    private static void MoveMouse(int dx, int dy){
        var input = new Input[1];
        input[0].type = INPUT_MOUSE;
        input[0].mi.dx = dx;
        input[0].mi.dy = dy;
        input[0].mi.dwFlags = MOUSEEVENTF_MOVE;

        var result = SendInput(1, input, Marshal.SizeOf(typeof(Input)));
        if(result == 0) Console.WriteLine("Failed to move the mouse. Error code: " + Marshal.GetLastWin32Error());
    }

    public static void MouseClick(int mouseEventFlag){
        var input = new Input[1];
        input[0].type = INPUT_MOUSE;
        input[0].mi.dwFlags = mouseEventFlag;

        var result = SendInput(1, input, Marshal.SizeOf(typeof(Input)));

        if(result == 0) Console.WriteLine("Failed to send mouse click. Error code: " + Marshal.GetLastWin32Error());
    }

    public static void ThreeFingersDragMouseDown(){
        switch (App.SettingsData.ThreeFingerDragButton){
            case SettingsData.ThreeFingerDragButtonType.LEFT:
                MouseClick(MOUSEEVENTF_LEFTDOWN);
                break;
            case SettingsData.ThreeFingerDragButtonType.RIGHT:
                MouseClick(MOUSEEVENTF_RIGHTDOWN);
                break;
            case SettingsData.ThreeFingerDragButtonType.MIDDLE:
                MouseClick(MOUSEEVENTF_MIDDLEDOWN);
                break;
        }
    }

    public static void ThreeFingersDragMouseUp(){
        switch (App.SettingsData.ThreeFingerDragButton){
            case SettingsData.ThreeFingerDragButtonType.LEFT:
                MouseClick(MOUSEEVENTF_LEFTUP);
                break;
            case SettingsData.ThreeFingerDragButtonType.RIGHT:
                MouseClick(MOUSEEVENTF_RIGHTUP);
                break;
            case SettingsData.ThreeFingerDragButtonType.MIDDLE:
                MouseClick(MOUSEEVENTF_MIDDLEUP);
                break;
        }
    }

    // MouseLike gesture engine support methods
    public static void MouseDown(int mouseEventFlag){
        MouseClick(mouseEventFlag);
    }

    public static void MouseUp(int mouseEventFlag){
        MouseClick(mouseEventFlag);
    }

    /// <summary>
    /// 发送滚轮事件
    /// </summary>
    /// <param name="horizontalDelta">水平滚动增量</param>
    /// <param name="verticalDelta">垂直滚动增量</param>
    public static void SendWheelEvent(int horizontalDelta, int verticalDelta)
    {
        if (verticalDelta != 0)
        {
            var input = new Input[1];
            input[0].type = INPUT_MOUSE;
            input[0].mi.dwFlags = 0x0800; // MOUSEEVENTF_WHEEL
            input[0].mi.mouseData = verticalDelta;

            SendInput(1, input, Marshal.SizeOf(typeof(Input)));
        }

        if (horizontalDelta != 0)
        {
            var input = new Input[1];
            input[0].type = INPUT_MOUSE;
            input[0].mi.dwFlags = 0x1000; // MOUSEEVENTF_HWHEEL
            input[0].mi.mouseData = horizontalDelta;

            SendInput(1, input, Marshal.SizeOf(typeof(Input)));
        }
    }


    [StructLayout(LayoutKind.Sequential)]
    private struct Input {
        public int type;
        public MouseInput mi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput {
        public int dx;
        public int dy;
        public int mouseData;
        public int dwFlags;
        public int time;
        public IntPtr dwExtraInfo;
    }
}

public struct IntMousePoint {
    public int x;
    public int y;

    public IntMousePoint(int x, int y){
        this.x = x;
        this.y = y;
    }
}
