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

            // Bind clears restCached and captures the poses itself, in relic space.
            _shards = new List<SacredRelicFracture.Shard>
            {
                new SacredRelicFracture.Shard
                {
                    transform = _centreShard, detach = 0f, arrive = 0f,
                },
                new SacredRelicFracture.Shard
                {
                    transform = _rimShard, detach = 1f, arrive = 0.7f,
                },
            };
            _relic.Bind(_root.transform, null, _shards, null, Vector3.back);
        }

        /// <summary>Cached poses are relic-space; the assertions below are about world space.</summary>
        Vector3 RestWorld(int shard) =>
            _root.transform.TransformPoint(_shards[shard].restLocalPosition);

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
            Assert.That(Vector3.Distance(_centreShard.position, RestWorld(0)),
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

            float centreTravel = Vector3.Distance(_centreShard.position, RestWorld(0));
            float rimTravel = Vector3.Distance(_rimShard.position, RestWorld(1));

            Assert.That(centreTravel, Is.GreaterThan(rimTravel),
                        "burst order must follow the crack, centre first");
        }

        [Test]
        public void Every_Shard_Has_Flown_By_The_End()
        {
            _relic.Evaluate(_relic.TotalDuration);
            for (int i = 0; i < _shards.Count; i++)
            {
                float travel = Vector3.Distance(_shards[i].transform.position, RestWorld(i));
                Assert.That(travel, Is.GreaterThan(0.05f),
                            _shards[i].transform.name + " never left");
            }
        }

        [Test]
        public void ResetToSealed_Puts_Every_Shard_Back()
        {
            _relic.Evaluate(_relic.TotalDuration);
            _relic.ResetToSealed();
            Assert.AreEqual(SacredRelicFracture.Phase.Sealed, _relic.CurrentPhase);
            for (int i = 0; i < _shards.Count; i++)
            {
                Assert.That(Vector3.Distance(_shards[i].transform.position, RestWorld(i)),
                            Is.LessThan(0.0005f));
            }
        }

        // The regression these three guard: rest poses used to be cached in world space, so the
        // relic could be moved and the shell would stay behind at the spot it was authored at.
        // Nothing here re-caches — that is the point. Posing must read the parent matrix live.

        [Test]
        public void Sealed_Shards_Follow_A_Moved_Stele_Without_Re_Caching()
        {
            _root.transform.position += new Vector3(3f, -1f, 20f);
            _relic.ResetToSealed();

            for (int i = 0; i < _shards.Count; i++)
                Assert.That(Vector3.Distance(_shards[i].transform.position, RestWorld(i)),
                            Is.LessThan(0.0005f),
                            _shards[i].transform.name + " did not come along");
        }

        [Test]
        public void Sealed_Shards_Follow_A_Scaled_And_Turned_Stele()
        {
            _root.transform.position = new Vector3(-2f, 0.5f, 4f);
            _root.transform.rotation = Quaternion.Euler(0f, 143f, 12f);
            _root.transform.localScale = Vector3.one * 0.25f;
            _relic.ResetToSealed();

            for (int i = 0; i < _shards.Count; i++)
            {
                Assert.That(Vector3.Distance(_shards[i].transform.position, RestWorld(i)),
                            Is.LessThan(0.0005f), _shards[i].transform.name + " lost its place");
                Assert.That(Quaternion.Angle(_shards[i].transform.rotation,
                                             _root.transform.rotation * _shards[i].restLocalRotation),
                            Is.LessThan(0.05f), _shards[i].transform.name + " lost its facing");
            }
        }

        [Test]
        public void Flight_Distance_Scales_With_The_Stele()
        {
            _relic.Evaluate(_relic.TotalDuration);
            float fullSize = Vector3.Distance(_centreShard.position, RestWorld(0));

            _root.transform.localScale = Vector3.one * 0.25f;
            _relic.Evaluate(_relic.TotalDuration);
            float quarterSize = Vector3.Distance(_centreShard.position, RestWorld(0));

            // A quarter-size relic must not fling its flakes full-size distances across the room.
            Assert.That(quarterSize, Is.EqualTo(fullSize * 0.25f).Within(0.002f),
                        "burst travel has to be in relic units, not metres");
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
