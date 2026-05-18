using NUnit.Framework;
using UnityEngine;

namespace BetterScriptable.Tests.Editor
{
    public sealed class BetterScriptableAssetTests
    {
        [Test]
        public void BetterScriptableAsset_IsScriptableObject()
        {
            Assert.That(typeof(ScriptableObject).IsAssignableFrom(typeof(TestAsset)), Is.True);
        }

        private sealed class TestAsset : BetterScriptableAsset
        {
        }
    }
}

