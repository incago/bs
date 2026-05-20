using System;

namespace SpreadAsset
{
    /// <summary>
    /// Marks a user-defined enum as available in SpreadAsset Generator field type pickers.
    /// </summary>
    [AttributeUsage(AttributeTargets.Enum, AllowMultiple = false, Inherited = false)]
    public sealed class SpreadAssetEnumAttribute : Attribute
    {
    }
}
