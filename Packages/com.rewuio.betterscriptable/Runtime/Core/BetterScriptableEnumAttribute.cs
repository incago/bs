using System;

namespace BetterScriptable
{
    /// <summary>
    /// Marks a user-defined enum as available in BetterScriptable Generator field type pickers.
    /// </summary>
    [AttributeUsage(AttributeTargets.Enum, AllowMultiple = false, Inherited = false)]
    public sealed class BetterScriptableEnumAttribute : Attribute
    {
    }
}
