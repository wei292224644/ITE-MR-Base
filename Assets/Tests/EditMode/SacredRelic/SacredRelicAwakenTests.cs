using NUnit.Framework;
using UnityEngine;
using MRBase.SacredRelic;

namespace MRBase.SacredRelic.Tests
{
    public class SacredRelicAwakenTests
    {
        SacredRelicAwaken _awaken;
        GameObject _go;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("SacredRelicTest");
            _awaken = _go.AddComponent<SacredRelicAwaken>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_go);
        }

        [Test]
        public void Starts_Sealed()
        {
            Assert.AreEqual(SacredRelicAwaken.State.Sealed, _awaken.CurrentState);
        }

        [Test]
        public void TryAwaken_FromSealed_ReturnsTrue_AndEntersAwakening()
        {
            Assert.IsTrue(_awaken.TryAwaken());
            Assert.AreEqual(SacredRelicAwaken.State.Awakening, _awaken.CurrentState);
        }

        [Test]
        public void TryAwaken_WhileAwakening_ReturnsFalse()
        {
            _awaken.TryAwaken();
            Assert.IsFalse(_awaken.TryAwaken());
        }

        [Test]
        public void ResetToSealed_ReturnsToSealed_AndAllowsAwakenAgain()
        {
            _awaken.TryAwaken();
            _awaken.ResetToSealed();
            Assert.AreEqual(SacredRelicAwaken.State.Sealed, _awaken.CurrentState);
            Assert.IsTrue(_awaken.TryAwaken());
        }
    }
}
