using System;

namespace SpreadAsset
{
    /// <summary>
    /// Marks a Unity-serializable user-defined class or struct as available in SpreadAsset Generator field type pickers.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
    public sealed class SpreadAssetClassAttribute : Attribute
    {
    }
}
