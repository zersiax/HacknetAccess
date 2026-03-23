using System;
using System.Runtime.InteropServices;

namespace HacknetAccess
{
    /// <summary>
    /// Wrapper for Tolk screen reader bridge.
    /// Provides speech output and braille display support.
    /// </summary>
    public static class ScreenReader
    {
        [DllImport("Tolk.dll")]
        private static extern void Tolk_Load();

        [DllImport("Tolk.dll")]
        private static extern void Tolk_Unload();

        [DllImport("Tolk.dll")]
        private static extern bool Tolk_IsLoaded();

        [DllImport("Tolk.dll", CharSet = CharSet.Unicode)]
        private static extern bool Tolk_Output(string str, bool interrupt);

        [DllImport("Tolk.dll", CharSet = CharSet.Unicode)]
        private static extern bool Tolk_Speak(string str, bool interrupt);

        [DllImport("Tolk.dll", CharSet = CharSet.Unicode)]
        private static extern bool Tolk_Braille(string str);

        [DllImport("Tolk.dll")]
        private static extern void Tolk_Silence();

        [DllImport("Tolk.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr Tolk_DetectScreenReader();

        private static bool _isLoaded;

        /// <summary>
        /// Initializes the Tolk library.
        /// </summary>
        public static void Init()
        {
            try
            {
                Tolk_Load();
                _isLoaded = Tolk_IsLoaded();

                if (_isLoaded)
                {
                    IntPtr srPtr = Tolk_DetectScreenReader();
                    string sr = srPtr != IntPtr.Zero ? Marshal.PtrToStringUni(srPtr) : "None";
                    Plugin.Instance.Log.LogInfo($"Screen reader detected: {sr}");
                }
                else
                {
                    Plugin.Instance.Log.LogWarning("Tolk loaded but no screen reader detected.");
                }
            }
            catch (Exception ex)
            {
                Plugin.Instance.Log.LogError($"Failed to initialize Tolk: {ex.Message}");
                _isLoaded = false;
            }
        }

        /// <summary>
        /// Sends text to both speech and braille.
        /// </summary>
        public static void Output(string text, bool interrupt = true)
        {
            if (!_isLoaded || string.IsNullOrEmpty(text)) return;

            try
            {
                Tolk_Output(text, interrupt);
            }
            catch (Exception ex)
            {
                Plugin.Instance.Log.LogError($"Tolk_Output failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Sends text to speech only.
        /// </summary>
        public static void Speak(string text, bool interrupt = true)
        {
            if (!_isLoaded || string.IsNullOrEmpty(text)) return;

            try
            {
                Tolk_Speak(text, interrupt);
            }
            catch (Exception ex)
            {
                Plugin.Instance.Log.LogError($"Tolk_Speak failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Sends text to braille display only.
        /// </summary>
        public static void Braille(string text)
        {
            if (!_isLoaded || string.IsNullOrEmpty(text)) return;

            try
            {
                Tolk_Braille(text);
            }
            catch (Exception ex)
            {
                Plugin.Instance.Log.LogError($"Tolk_Braille failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Silences current speech output.
        /// </summary>
        public static void Silence()
        {
            if (!_isLoaded) return;

            try
            {
                Tolk_Silence();
            }
            catch (Exception ex)
            {
                Plugin.Instance.Log.LogError($"Tolk_Silence failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Cleans up Tolk resources.
        /// </summary>
        public static void Shutdown()
        {
            if (!_isLoaded) return;

            try
            {
                Tolk_Unload();
                _isLoaded = false;
            }
            catch (Exception ex)
            {
                Plugin.Instance.Log.LogError($"Tolk_Unload failed: {ex.Message}");
            }
        }
    }
}
