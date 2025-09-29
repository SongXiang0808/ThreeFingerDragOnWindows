using System;

using System.Diagnostics;

using System.IO;

using System.Xml.Serialization;

using Windows.Storage;

using Microsoft.UI.Xaml;

using Microsoft.UI.Xaml.Controls;

using ThreeFingerDragOnWindows.utils;

using WinUICommunity;



namespace ThreeFingerDragOnWindows.settings;



public class SettingsData

{

    private static int CURRENT_SETTINGS_VERSION = 5;



    // Other

    public static bool DidVersionChanged { get; set; } = false;

    public int SettingsVersion { get; set; } = 0;



    // Three finger drag Settings

    public bool ThreeFingerDrag { get; set; } = true;



    public enum ThreeFingerDragButtonType

    {

        NONE,

        LEFT,

        RIGHT,

        MIDDLE,

    }



    public ThreeFingerDragButtonType ThreeFingerDragButton { get; set; } = ThreeFingerDragButtonType.LEFT;



    public bool ThreeFingerDragAllowReleaseAndRestart { get; set; } = true;

    public int ThreeFingerDragReleaseDelay { get; set; } = 500;



    public bool ThreeFingerDragCursorMove { get; set; } = true;

    public float ThreeFingerDragCursorSpeed { get; set; } = 30;

    public float ThreeFingerDragCursorAcceleration { get; set; } = 10;

    public int ThreeFingerDragCursorAveraging { get; set; } = 1;

    public int ThreeFingerDragMaxFingerMoveDistance { get; set; } = 0;



    public int ThreeFingerDragStartThreshold { get; set; } = 100;

    public int ThreeFingerDragStopThreshold { get; set; } = 10;



    // MouseLike Mode Settings

    public bool MouseLikeModeEnabled { get; set; } = true;

    public float MouseLikeSensitivity { get; set; } = 1.0f;

    public float MouseLikeThumbScale { get; set; } = 1.0f;

    public float MouseLikeJitterOffset { get; set; } = 0.4f;

    public float MouseLikeLeftRightMinDistance { get; set; } = 80f;

    public float MouseLikeLeftRightMaxDistance { get; set; } = 520f;

    public float MouseLikeLeftRightVerticalTolerance { get; set; } = 200f;

    public bool MouseLikeMiddleButtonEnabled { get; set; } = false;



    // Other settings

    public enum StartupActionType

    {

        NONE,

        ENABLE_ELEVATED_RUN_WITH_STARTUP,

        DISABLE_ELEVATED_RUN_WITH_STARTUP,

        ENABLE_ELEVATED_STARTUP,

        DISABLE_ELEVATED_STARTUP,

    }



    public StartupActionType StartupAction { get; set; } = StartupActionType.NONE;



    public bool RunElevated { get; set; } = false;



    public bool RecordLogs { get; set; } = false;



    public static SettingsData load()

    {

        Logger.Log("Loading settings...");



        var serializer = new XmlSerializer(typeof(SettingsData));

        SettingsData data;



        try

        {

            using var stream = new FileStream(getPath(true), FileMode.Open, FileAccess.Read, FileShare.Read);

            data = (SettingsData)serializer.Deserialize(stream);

            Logger.Log($"Settings loaded, version = {data.SettingsVersion}");

        }

        catch (Exception e)

        {

            Debug.WriteLine(e);

            data = new SettingsData();

            data.save();

        }



        if (data.SettingsVersion < 1)

        {

            Logger.Log("Updating settings to version 1");

            data.ThreeFingerDragCursorAcceleration *= 10;

            data.save();

        }



        if (data.SettingsVersion < 2)

        {

            Logger.Log("Updating settings to version 2");

            if (data.RunElevated && StartupManager.IsElevatedStartupOn())

            {

                if (Utils.IsAppRunningAsAdministrator())

                {

                    StartupManager.DisableElevatedStartup();

                    StartupManager.EnableElevatedStartup();

                }

                else

                {

                    Utils.runOnMainThreadAfter(2000, () =>
                    {
                        if (App.SettingsWindow?.Content?.XamlRoot == null)
                        {
                            Logger.Log("SettingsWindow not ready, skipping v2.0.3 upgrade dialog");
                            return;
                        }

                        ContentDialog dialog = new()
                        {
                            XamlRoot = App.SettingsWindow.Content.XamlRoot,
                            Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                            Title = "Fixing startup task issue",
                            Content = "The v2.0.3 fixed a bug in the app startup task with elevated privileges. Please disable and re-enable the \"Run at startup\" option in the Other Settings tab to fix this bug.",
                            CloseButtonText = "Ok"
                        };
                        dialog.ShowAsyncDraggable();
                    });

                }

            }

        }



        if (data.SettingsVersion < 5)

        {

            Logger.Log("Updating settings to version 5");

            if (data.MouseLikeLeftRightMinDistance <= 0)

            {

                data.MouseLikeLeftRightMinDistance = 80f;

            }

            if (data.MouseLikeLeftRightMaxDistance <= 0)

            {

                data.MouseLikeLeftRightMaxDistance = 520f;

            }

            if (data.MouseLikeLeftRightVerticalTolerance <= 0)

            {

                data.MouseLikeLeftRightVerticalTolerance = 200f;

            }

            if (data.MouseLikeJitterOffset <= 0)

            {

                data.MouseLikeJitterOffset = 0.4f;

            }

            data.RecordLogs = false;

            data.save();

        }



        if (data.SettingsVersion != CURRENT_SETTINGS_VERSION)

        {

            DidVersionChanged = true;

            data.RecordLogs = false;

            data.save();

        }



        return data;

    }



    public void save()

    {

        SettingsVersion = CURRENT_SETTINGS_VERSION;

        var serializer = new XmlSerializer(typeof(SettingsData));

        using var writer = new StreamWriter(getPath(false));

        serializer.Serialize(writer, this);

    }



    private static string getPath(bool createIfEmpty)

    {

        var dirPath = ApplicationData.Current.LocalFolder.Path;

        var filePath = Path.Combine(dirPath, "preferences.xml");



        if (!Directory.Exists(dirPath) || !File.Exists(filePath))

        {

            Logger.Log("First run: creating settings file");

            Directory.CreateDirectory(dirPath);

            DidVersionChanged = true;

            if (createIfEmpty)

            {

                new SettingsData().save();

            }

        }



        return filePath;

    }

}









