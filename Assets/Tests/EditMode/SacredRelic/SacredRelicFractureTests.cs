using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace MRBase.SacredRelic.Tests
{
    public class SacredRelicFractureTests
    {
        GameObject _root;
        SacredRelicFracture _relic;
        List<SacredRelicFracture.Shard> _shards;
        Transform _centreShard;
        Transform _rimShard;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("Relic");
            _relic = _root.AddComponent<SacredRelicFracture>();

            _centreShard = new GameObject("Shell_Piece_000").transform;
            _centreShard.SetParent(_root.transform);
            _centreShard.position = new Vector3(0.02f, 0.01f, 0f);

            _rimShard = new GameObject("Shell_Piece_024").transform;
            _rimShard.SetParent(_root.transform);
            _rimShard.position = new Vector3(0.28f, 0.42f, 0f);

            _shards = new List<SacredRelicFracture.Shard>
            {
                new SacredRelicFracture.Shard
                {
                    transform = _centreShard, detach = 0f, arrive = 0f,
                    restCached = true, restPosition = _centreShard.position,
                    restRotation = _centreShard.rotation,
                },
                new SacredRelicFracture.Shard
                {
                    transform = _rimShard, detach = 1f, arrive = 0.7f,
                    restCached = true, restPosition = _rimShard.position,
                    restRotation = _rimShard.rotation,
                },
            };
            _relic.Bind(_root.transform, null, _shards, null, Vector3.back);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_root);

        [Test]
        public void Starts_Sealed()
        {
            Assert.AreEqual(SacredRelicFracture.Phase.Sealed, _relic.CurrentPhase);
        }

        [Test]
        public void Shards_Hold_Position_While_The_Crack_Is_Still_Spreading()
        {
            _relic.Evaluate(0.2f);
            Assert.AreEqual(SacredRelicFracture.Phase.Cracking, _relic.CurrentPhase);
            Assert.That(Vector3.Distance(_centreShard.position, _shards[0].restPosition),
                        Is.LessThan(0.0005f), "nothing should move before the gold seeps");
        }

        [Test]
        public void Centre_Shard_Releases_Before_The_Rim_Shard()
        {
            // Directional spread recomputes arrive/detach from where each shard sits, so the
            // authored detach values in SetUp do not decide the order — the wave does. Aim it
            // at the centre shard's corner, otherwise this asserts against whatever the
            // component's default from→to happens to be.
            _relic.SetSpreadDirection(Vector2.zero, Vector2.one);

            // Just into the burst beat: the piece the crack freed first is already flying,
            // the last piece the crack reaches has not moved yet.
            _relic.Evaluate(_relic.TotalDuration * 0.5f);

            float centreTravel = Vector3.Distance(_centreShard.position, _shards[0].restPosition);
            float rimTravel = Vector3.Distance(_rimShard.position, _shards[1].restPosition);

            Assert.That(centreTravel, Is.GreaterThan(rimTravel),
                        "burst order must follow the crack, centre first");
        }

        [Test]
        public void Every_Shard_Has_Flown_By_The_End()
        {
            _relic.Evaluate(_relic.TotalDuration);
            foreach (SacredRelicFracture.Shard shard in _shards)
            {
                float travel = Vector3.Distance(shard.transform.position, shard.restPosition);
                Assert.That(travel, Is.GreaterThan(0.05f), shard.transform.name + " never left");
            }
        }

        [Test]
        public void ResetToSealed_Puts_Every_Shard_Back()
        {
            _relic.Evaluate(_relic.TotalDuration);
            _relic.ResetToSealed();
            Assert.AreEqual(SacredRelicFracture.Phase.Sealed, _relic.CurrentPhase);
            foreach (SacredRelicFracture.Shard shard in _shards)
            {
                Assert.That(Vector3.Distance(shard.transform.position, shard.restPosition),
                            Is.LessThan(0.0005f));
            }
        }

        [Test]
        public void Evaluate_Is_Deterministic_When_Scrubbed_Backwards()
        {
            _relic.Evaluate(_relic.TotalDuration * 0.6f);
            Vector3 forward = _centreShard.position;
            _relic.Evaluate(_relic.TotalDuration);
            _relic.Evaluate(_relic.TotalDuration * 0.6f);
            Assert.That(Vector3.Distance(_centreShard.position, forward), Is.LessThan(0.0005f),
                        "scrubbing must be stateless so the sequence can be driven by a slider");
        }
    }
}
