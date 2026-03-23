using System;
using System.Reflection;

namespace HacknetAccess
{
    /// <summary>
    /// Helper class for accessing private fields via Reflection.
    /// Use when Harmony ___fieldName syntax is not available (e.g., dynamic access).
    /// </summary>
    public static class ReflectionHelper
    {
        private const BindingFlags PrivateFlags =
            BindingFlags.NonPublic | BindingFlags.Instance;

        private const BindingFlags AllFlags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        /// <summary>
        /// Gets a private field value as a reference type.
        /// Returns null if field not found or value is null.
        /// </summary>
        public static T GetPrivateField<T>(object obj, string fieldName) where T : class
        {
            if (obj == null) return null;

            try
            {
                var field = obj.GetType().GetField(fieldName, PrivateFlags);
                return field?.GetValue(obj) as T;
            }
            catch (Exception ex)
            {
                DebugLogger.Log(LogCategory.Game, "Reflection",
                    $"Failed to get field '{fieldName}': {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Gets a private field value as a value type (int, bool, enum, etc.).
        /// Returns defaultValue if field not found.
        /// </summary>
        public static T GetPrivateFieldValue<T>(object obj, string fieldName, T defaultValue = default) where T : struct
        {
            if (obj == null) return defaultValue;

            try
            {
                var field = obj.GetType().GetField(fieldName, PrivateFlags);
                if (field == null) return defaultValue;

                var value = field.GetValue(obj);
                if (value == null) return defaultValue;

                return (T)value;
            }
            catch (Exception ex)
            {
                DebugLogger.Log(LogCategory.Game, "Reflection",
                    $"Failed to get field '{fieldName}': {ex.Message}");
                return defaultValue;
            }
        }

        /// <summary>
        /// Sets a private field value.
        /// </summary>
        public static bool SetPrivateField(object obj, string fieldName, object value)
        {
            if (obj == null) return false;

            try
            {
                var field = obj.GetType().GetField(fieldName, PrivateFlags);
                if (field == null) return false;

                field.SetValue(obj, value);
                return true;
            }
            catch (Exception ex)
            {
                DebugLogger.Log(LogCategory.Game, "Reflection",
                    $"Failed to set field '{fieldName}': {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Gets FieldInfo for caching. Use for frequently accessed fields.
        /// </summary>
        public static FieldInfo GetFieldInfo(Type type, string fieldName)
        {
            return type.GetField(fieldName, AllFlags);
        }

        /// <summary>
        /// Gets FieldInfo, trying multiple possible names.
        /// </summary>
        public static FieldInfo GetFieldInfo(Type type, params string[] possibleNames)
        {
            foreach (var name in possibleNames)
            {
                var field = type.GetField(name, AllFlags);
                if (field != null) return field;
            }
            return null;
        }
    }
}
