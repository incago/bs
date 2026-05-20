using NUnit.Framework;
using UnityEngine;

namespace SpreadAsset.Tests.Editor
{
    public sealed class SpreadAssetObjectTests
    {
        [Test]
        public void SpreadAssetObject_IsScriptableObject()
        {
            Assert.That(typeof(ScriptableObject).IsAssignableFrom(typeof(TestAsset)), Is.True);
        }

        private sealed class TestAsset : SpreadAssetObject
        {
        }
    }
}
