using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ModernUI.WinForms;

internal static class ModernCompatibility
{
    public const float PI = (float)Math.PI;

    public static T Clamp<T>(T value, T minimum, T maximum) where T : IComparable<T>
    {
        if (minimum.CompareTo(maximum) > 0) throw new ArgumentException("minimum cannot be greater than maximum.");
        if (value.CompareTo(minimum) < 0) return minimum;
        return value.CompareTo(maximum) > 0 ? maximum : value;
    }

    public static float Clamp(float value, float minimum, float maximum)
    {
        if (minimum > maximum) throw new ArgumentException("minimum cannot be greater than maximum.");
        if (float.IsNaN(value)) return value;
        return value < minimum ? minimum : value > maximum ? maximum : value;
    }

    public static double Clamp(double value, double minimum, double maximum)
    {
        if (minimum > maximum) throw new ArgumentException("minimum cannot be greater than maximum.");
        if (double.IsNaN(value)) return value;
        return value < minimum ? minimum : value > maximum ? maximum : value;
    }

    public static float Sqrt(float value) => (float)Math.Sqrt(value);
    public static float Pow(float value, float power) => (float)Math.Pow(value, power);
    public static float Sin(float value) => (float)Math.Sin(value);
    public static float Cos(float value) => (float)Math.Cos(value);

    public static void ThrowIfNull(object? value, string? parameterName = null)
    {
        if (value is null) throw new ArgumentNullException(parameterName);
    }

    public static void ThrowIfNullOrWhiteSpace(string? value, string? parameterName = null)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Value cannot be null or whitespace.", parameterName);
    }

    public static void ThrowIfDisposed(bool condition, object instance)
    {
        if (condition) throw new ObjectDisposedException(instance.GetType().FullName);
    }

    public static bool IsWindows() => Environment.OSVersion.Platform == PlatformID.Win32NT;

    public static bool IsWindowsVersionAtLeast(int major, int minor = 0, int build = 0) =>
        IsWindows() && Environment.OSVersion.Version >= new Version(major, minor, build);

    public static bool IsClientAreaAnimationEnabled
    {
        get
        {
            if (!IsWindows()) return true;
            var enabled = true;
            return SystemParametersInfo(0x1042, 0, ref enabled, 0) && enabled;
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint parameter, ref bool value, uint update);

    public static long TickCount64
    {
        get
        {
#if NET8_0_OR_GREATER
            return Environment.TickCount64;
#else
            return unchecked((uint)Environment.TickCount);
#endif
        }
    }
}

internal sealed class ModernReferenceEqualityComparer : IEqualityComparer<object>
{
    public static ModernReferenceEqualityComparer Instance { get; } = new();
    private ModernReferenceEqualityComparer() { }
    public new bool Equals(object? left, object? right) => ReferenceEquals(left, right);
    public int GetHashCode(object value) => RuntimeHelpers.GetHashCode(value);
}

internal static class CollectionCompatibilityExtensions
{
    public static TValue? GetValueOrDefault<TKey, TValue>(this IDictionary<TKey, TValue> source, TKey key) =>
        source.TryGetValue(key, out var value) ? value : default;

    public static TValue GetValueOrDefault<TKey, TValue>(this IDictionary<TKey, TValue> source, TKey key, TValue defaultValue) =>
        source.TryGetValue(key, out var value) ? value : defaultValue;

    public static bool Remove<TKey, TValue>(this IDictionary<TKey, TValue> source, TKey key, out TValue value)
    {
        if (source.TryGetValue(key, out value!))
        {
            source.Remove(key);
            return true;
        }
        value = default!;
        return false;
    }

    public static bool Contains(this string source, string value, StringComparison comparison) =>
        source.IndexOf(value, comparison) >= 0;
}
