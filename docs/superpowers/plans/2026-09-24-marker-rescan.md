# 标记重扫 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 一直盯着码只扫一次；移开视线满 3 秒（全局可调）再看回来才算重扫；ITE 在等第一次扫描时不需要先移开视线；当前 Tour 在播时扫它的码一律重新定位。

**Architecture:** 标记层（`Assets/Scripts/Localization`）的判稳改为每次出现只提交一次，并提供 `ResetAll`。宿主适配层（`Assets/Scripts/IteHost`）负责四件事：
- 同帧落选的码重新判稳；
- 从配置资产统一提供丢失时长；
- ITE 发出扫码提示时放行；
- 输入层建会话时从宿主读丢失时长。

ITE 包（`Packages/com.uality.ite-tour`）的扫码决策改为：当前 Tour 在播时再扫它的码 = 重新定位。二次锚定许可状态整套删除。

**Tech Stack:** Unity 6000.4.4f1，C#，NUnit（Unity Test Framework，EditMode），通过 `unity` CLI 操作用户已打开的 Editor。

**Spec:** `docs/superpowers/specs/2026-09-24-marker-rescan-design.md`（决策 D1–D7）

## Global Constraints

- 代码注释里的决策编号写作 `marker-rescan Dn`；沿用的旧编号原样保留（`design Dn`、`ite-current-tour Dn` 等）。不要读、也不要恢复 `openspec/`。
- 丢失时长默认 **3 秒**，两个平台共用，放在 `MarkerStabilizerProfile` 顶层字段 `lostAfterSeconds`。
- 不在范围内，不要改：`MarkerHookTestRig` 及 `MarkerHookTest.unity` 里的 `lostAfterSeconds`；PICO 相机采样率（`sampleHz`）；ITE 的区域、当前 Tour、状态机规则。
- Unity 操作只能用 `unity` CLI 连用户已打开的 Editor。不开 headless，不开第二个 Unity 进程，**不打包、不装机**。
- 注释用中文，风格与周围代码一致。
- 每个 Task 结束时在 `master` 上提交一次，不 push。提交信息末尾加 `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`。
- 开始写代码前，先调用 `hindsight_capture_initiative` 记录本计划（CLAUDE.md 要求）。

## 如何编译和跑测试（所有 Task 通用）

**编译不要用 `unity command recompile`。** 由 CLI 触发的域重载常常让 Pipeline Server 绑不上端口，而用户在 Editor 里按 `Cmd+R` 触发的重载每次都正常（memory `unity-pipeline-server-port-bind-failure`）。所以需要编译时，一轮改动一次写完，然后请用户在 Editor 里按一次 `Cmd+R`，再轮询：

```bash
cd /Users/wwj/Desktop/unity/MR_Base
for i in $(seq 1 40); do s=$(unity command recompile_status --project-path . 2>&1 | tail -1); case "$s" in *completed*|*up_to_date*) echo "$s"; break;; esac; sleep 3; done
```

编译成功时，最后一行包含 `"failed":false,"errors":[]`。

跑一个程序集，只列出失败项。`A` 取 `MRBase.Localization.Tests`、`MRBase.Ite.Host.Tests` 或 `Uality.IteTour.Tests`：

```bash
cd /Users/wwj/Desktop/unity/MR_Base
A=MRBase.Ite.Host.Tests
unity command run_tests --project-path . --mode EditMode --filter "$A" --filter_type assembly --timeout 300 2>&1 | tail -1 > "$TMPDIR/r.json"
python3 - "$TMPDIR/r.json" "$A" <<'EOF'
import json,sys
raw=open(sys.argv[1]).read(); parts=raw.split('\t')
if len(parts)<3: print(sys.argv[2], "RAW:", raw[:300]); sys.exit()
j=json.loads(parts[2])
print(sys.argv[2], j.get("Summary"), j.get("error"))
for r in j.get("Results",[]):
    if r["Status"]!="Passed":
        print("  FAIL", r["FullName"], "|", (r["Message"] or "").strip().replace("\n"," ")[:200])
EOF
```

如果输出 `Cannot connect to Unity Editor Pipeline server`：每 3 秒重试一次 `unity command editor_status --project-path .`，最多重试 30 秒。仍然连不上，就请用户切到 Editor 按 `Cmd+R`。不要自己启动 Unity。

C# 里「测试先失败」有时表现为编译失败（测试引用的新成员还不存在）。这时只要看到编译错误里提到这个新成员就算确认失败，不要为了让测试「运行失败」去写桩代码。

## Review Focus

EditMode 测试覆盖不到、最可能让人踩坑的五种情况：

1. **扫码提示出现时，码一直在视野里**，例如摘下后 3 秒内戴回、重定位、当前 Tour 变成还没播的 normal。
   - 期望：一个稳定窗口内（0.4～0.5 秒）就能扫上，不需要先移开视线。
   - `IteHostBootstrap` 收到提示时调用 `Rearm()`，这是 MonoBehaviour 接线。ITE 在 `LateUpdate` 里才广播提示，EditMode 触发不了。
   - `Rearm` 本身由 Task 2 的 `Rearm_WhileVisible_ForwardsAgain` 覆盖；接线靠 spec §8 的真机验证第 4、5、6 条。
2. **Quest 上移开视线后，MRUK 多久才报这张码不再追踪（`IsTracked`）。**
   - MRUK 如果把「还在追踪」保持一段时间，实际要移开的时间就会超过 3 秒。
   - 期望：移开 3 秒多一点后再看回来，就会重扫。
   - 真机验证第 2、3 条要看日志里 `MarkerLost` 出现的时刻。EditMode 无法模拟 MRUK。
3. **放行时视野里有两张码。**
   - 期望：两张依次提交，间隔一个稳定窗口；ITE 按先后分别决定每张怎么处理。
   - 由 Task 1 的 `TwoMarkersStabilizingInSameTick_OnePerTick_LoserFollowsLater` 覆盖：放行后两张码同时开始判稳，就是这个用例的输入。
4. **应用休眠后恢复。**
   - 会话暂停期间不累计缺席时长，码不会被判为丢失，判稳状态停在「已提交」。靠戴上后 ITE 的扫码提示放行。
   - 期望：戴上后直接能扫上。
   - EditMode 测不了应用暂停，靠真机验证第 5 条。
5. **编辑器假扫码。**
   - 3 秒内再按同一个数字键：会话还没判丢失，不会再扫，这与真机行为一致。按别的键正常扫码。
   - 由 Task 2 改写的 `Driver_Trigger_DispatchesObservedThenLostOnce` 钉住 3 秒的丢失时长。

---

### Task 1：判稳每次出现只提交一次，同帧落选的码重新判稳（D1、D7）

**Files:**
- Modify: `Assets/Scripts/Localization/MarkerStabilizer.cs`
- Modify: `Assets/Scripts/IteHost/IteMarkerBridge.cs`
- Test: `Assets/Scripts/Localization/Tests/EditMode/MarkerStabilizerTests.cs`
- Test: `Assets/Scripts/IteHost/Tests/EditMode/IteMarkerBridgeTests.cs`

**Interfaces:**
- Consumes：无。
- Produces：`MarkerStabilizer.Feed` 的新语义：同一张码判稳后，不管位姿怎么变都不再提交 `Stabilized`，直到 `Reset(rawId)`。Task 2 在此基础上加 `ResetAll()`。

- [ ] **Step 1：写失败的测试**

打开 `MarkerStabilizerTests.cs`，把整个 `Feed_FiresOnce_ThenRefiresOnlyAfterMovingAgain` 测试（`[Test]` 行到方法结束的 `}`）替换为下面两个测试：

```csharp
    /// <summary>
    /// 每次出现只提交一次（marker-rescan D1）：判稳之后位姿再移动、再稳定，也不重发。
    /// 码固定贴在场地里，持续观测期间的移动只来自识别噪声——PICO 上旧规则约 4 秒误触发一次重扫。
    /// </summary>
    [Test]
    public void Feed_FiresOncePerAppearance_EvenAfterMovingAndSettlingAgain()
    {
        var stabilizer = new MarkerStabilizer(positionThreshold: 0.05f, rotationThreshold: 1f, smoothTime: 0.001f, stableSeconds: 2f);
        int firedCount = 0;
        stabilizer.Stabilized += (_, __) => firedCount++;

        var poseA = new Pose(new Vector3(1, 0, 0), Quaternion.identity);
        stabilizer.Feed("A", poseA, 1f);
        stabilizer.Feed("A", poseA, 1f); // 累计到窗口,触发
        Assert.AreEqual(1, firedCount);

        var poseB = new Pose(new Vector3(5, 0, 0), Quaternion.identity);
        for (int i = 0; i < 5; i++)
        {
            stabilizer.Feed("A", poseB, 1f); // 移动后再稳定,远超窗口
        }

        Assert.AreEqual(1, firedCount, "同一次出现只提交一次");
    }

    /// <summary>Reset 之后是新的一次出现：重新判稳、再提交一次（丢失时由桥接调用）。</summary>
    [Test]
    public void Reset_ThenStableAgain_FiresAgain()
    {
        var stabilizer = new MarkerStabilizer(positionThreshold: 0.05f, rotationThreshold: 1f, smoothTime: 0.001f, stableSeconds: 2f);
        int firedCount = 0;
        stabilizer.Stabilized += (_, __) => firedCount++;

        var pose = new Pose(new Vector3(1, 0, 0), Quaternion.identity);
        stabilizer.Feed("A", pose, 1f);
        stabilizer.Feed("A", pose, 1f);
        Assert.AreEqual(1, firedCount);

        stabilizer.Reset("A");
        stabilizer.Feed("A", pose, 1f);
        Assert.AreEqual(1, firedCount, "重新判稳要走满窗口");
        stabilizer.Feed("A", pose, 1f);
        Assert.AreEqual(2, firedCount);
    }
```

打开 `IteMarkerBridgeTests.cs`，在 `ContinuouslyVisible_ForwardsExactlyOnce` 测试之后加一个测试：

```csharp
        /// <summary>
        /// 一直盯着码看只扫一次（marker-rescan D1）。PICO 解出的位姿偶尔抖过防抖阈值，
        /// 旧规则把它当成「码移动了」，重新判稳后再提交一次（真机 2026-09-24：约 4 秒自动 Reanchor 一次）。
        /// </summary>
        [Test]
        public void ContinuouslyVisible_OccasionalJitterAboveThreshold_ForwardsOnce()
        {
            var source = new MockObservationSource();
            var session = new MarkerTrackingSession(source);
            var forwarded = new List<(MarkerKind kind, string payload, Pose pose)>();

            using (var bridge = new IteMarkerBridge(session, Profile(), null,
                       (k, p, pose) => forwarded.Add((k, p, pose))))
            {
                for (int i = 0; i < 100; i++)
                {
                    // 每 2 秒抖一次 3 度（阈值 1 度），其余时间不动
                    float yaw = i > 0 && i % 20 == 0 ? 3f : 0f;
                    source.SetNextPoll(new[]
                    {
                        new MarkerObservation(MarkerPlatform.Pico, "0",
                            new Pose(Vector3.zero, Quaternion.Euler(0f, yaw, 0f)))
                    });
                    bridge.Tick(Dt);
                }
            }

            Assert.AreEqual(1, forwarded.Count, "持续可见期间的位姿抖动不算重扫");
        }
```

在同一文件里，把整个 `TwoMarkersStabilizingInSameTick_OnlyFirstIsForwarded` 测试（连同它上面的 `<summary>` 注释）替换为：

```csharp
        /// <summary>
        /// design D20：同一 tick 只提交一张码。落选的码重新判稳、之后单独提交（marker-rescan D7）——
        /// 判稳每次出现只发一次，丢掉它就要移开视线才能再扫；放行会让视野里的码同时判稳，落选是常态。
        /// </summary>
        [Test]
        public void TwoMarkersStabilizingInSameTick_OnePerTick_LoserFollowsLater()
        {
            var source = new MockObservationSource();
            var session = new MarkerTrackingSession(source);
            var forwarded = new List<(string payload, int tick)>();
            int tick = 0;

            using (var bridge = new IteMarkerBridge(session, Profile(), null,
                       (k, p, pose) => forwarded.Add((p, tick))))
            {
                source.SetNextPoll(new[]
                {
                    new MarkerObservation(MarkerPlatform.Quest, "******a******", Pose.identity),
                    new MarkerObservation(MarkerPlatform.Quest, "******b******", Pose.identity),
                });

                for (tick = 0; tick < 10; tick++)
                {
                    bridge.Tick(Dt);
                }
            }

            Assert.AreEqual(2, forwarded.Count, "两张码都要提交");
            Assert.AreNotEqual(forwarded[0].payload, forwarded[1].payload);
            Assert.AreNotEqual(forwarded[0].tick, forwarded[1].tick, "同一 tick 内只提交一次");
        }
```

- [ ] **Step 2：编译并确认测试失败**

请用户按一次 `Cmd+R`，编译通过后分别跑 `A=MRBase.Localization.Tests` 和 `A=MRBase.Ite.Host.Tests`。

期望失败的测试（这几项之外不应有其他失败）：
- `Feed_FiresOncePerAppearance_EvenAfterMovingAndSettlingAgain`：实际触发 2 次；
- `ContinuouslyVisible_OccasionalJitterAboveThreshold_ForwardsOnce`：实际大于 1 次；
- `TwoMarkersStabilizingInSameTick_OnePerTick_LoserFollowsLater`：实际只转发了 1 次。

`Reset_ThenStableAgain_FiresAgain` 这时已经通过，这是正常的：它钉住的是既有行为。

- [ ] **Step 3：实现 D1**

在 `MarkerStabilizer.cs` 里，把

```csharp
        if (moved)
        {
            marker.StableSeconds = 0f;
            marker.HasFiredStableEvent = false;
        }
```

改为

```csharp
        if (moved)
        {
            // 判稳前一移动就重新计时；判稳之后不再因移动重发（marker-rescan D1）
            marker.StableSeconds = 0f;
        }
```

把类注释里的 `/// 移植自源工程的 <c>AnchorObject</c>，但两处按实测改掉了（design D9 / D22）：` 改为 `/// 移植自源工程的 <c>AnchorObject</c>，但三处按实测改掉了（design D9 / D22、marker-rescan D1）：`。

再在类注释里的这一句

```csharp
/// 三项参数按平台各存一套，见 <c>MarkerStabilizerProfile</c>；硬件不是纸面上的理想值，
```

之前插入第 3 条（放在第 2 条之后、空一行）：

```csharp
/// 3. **每次出现只提交一次**（marker-rescan D1）。码固定贴在场地里，持续观测期间的「移动」只来自
///    识别噪声或追踪漂移——PICO 抖 2–5 度，旧的「移动后重新判稳再发」约 4 秒误触发一次重扫。
///    要再提交，先 <see cref="Reset"/>（丢失）。
///
```

把 `Reset` 那一行

```csharp
    public void Reset(string rawId) => tracked.Remove(rawId);
```

改为

```csharp
    /// <summary>这张码当作没出现过：下次喂入重新平滑、重新判稳。</summary>
    public void Reset(string rawId) => tracked.Remove(rawId);
```

- [ ] **Step 4：实现 D7**

在 `IteMarkerBridge.cs` 的 `Submit` 里，把

```csharp
            // 同一帧内多张码同时判稳时只认第一张（design D20）。不规定就是未定义行为：
            // 跟踪表是字典、迭代顺序不保证，而后到者会把先到者刚激活的 tour 停用销毁。
            if (_submittedThisTick)
            {
                Debug.Log(
                    "[ITE Host] 同帧已提交过扫码，忽略后到的标记：" + rawPayload +
                    "（先判稳的赢）");
                return;
            }
```

改为

```csharp
            // 同一帧内多张码同时判稳时只提交第一张（design D20）。不规定就是未定义行为：
            // 跟踪表是字典、迭代顺序不保证。
            //
            // 落选的码重新判稳、之后单独提交（marker-rescan D7）：判稳每次出现只发一次（D1），
            // 丢掉它就要移开视线才能再扫。后到的码不会停掉先到者刚激活的 Tour——已定位后
            // ITE 只认当前 Tour 的码（ite-current-tour D6）。
            if (_submittedThisTick)
            {
                StabilizerFor(platform).Reset(rawPayload);
                Debug.Log("[ITE Host] 同帧已提交过扫码，" + rawPayload + " 重新判稳后再提交");
                return;
            }
```

`Reset` 在判稳器派发 `Stabilized` 的回调里调用是安全的：`Feed` 在派发之后直接返回，不会再访问被删掉的条目。

- [ ] **Step 5：编译并确认通过**

请用户按一次 `Cmd+R`。跑 `A=MRBase.Localization.Tests` 和 `A=MRBase.Ite.Host.Tests`，期望全部通过。

- [ ] **Step 6：提交**

```bash
cd /Users/wwj/Desktop/unity/MR_Base
git add Assets/Scripts/Localization/MarkerStabilizer.cs Assets/Scripts/IteHost/IteMarkerBridge.cs \
  Assets/Scripts/Localization/Tests/EditMode/MarkerStabilizerTests.cs \
  Assets/Scripts/IteHost/Tests/EditMode/IteMarkerBridgeTests.cs
git commit -m "$(cat <<'EOF'
fix(marker): 判稳每次出现只提交一次，同帧落选的码重新判稳

PICO 位姿抖动偶尔超过防抖阈值，旧规则当成码移动了，约 4 秒自动重扫一次（marker-rescan D1）。
同帧落选的码不再丢弃，重新判稳后单独提交（marker-rescan D7）。

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 2：丢失时长全局 3 秒，ITE 发出扫码提示时放行（D2、D3）

**Files:**
- Modify: `Assets/Scripts/Localization/MarkerStabilizer.cs`（加 `ResetAll`）
- Modify: `Assets/Scripts/Localization/MarkerStabilizerProfile.cs`（加 `lostAfterSeconds`）
- Modify: `Assets/Scripts/IteHost/IteMarkerBridge.cs`（加 `Rearm`，改注释）
- Modify: `Assets/Scripts/IteHost/IteHostBootstrap.cs`（`MarkerLostAfterSeconds`；提示可见时放行）
- Modify: `Assets/Scripts/IteHost/IteDeviceMarkerRig.cs`、`Assets/Scripts/IteHost/IteEditorFakeScan.cs`（删字段，改从宿主读）
- Modify（重新序列化）：`Assets/Settings/ITE/MarkerStabilizerProfile.asset`、`Assets/Prefabs/ITE/IteDeviceHarness.prefab`、`Assets/Prefabs/ITE/IteEditorHarness.prefab`
- Test: `Assets/Scripts/Localization/Tests/EditMode/MarkerStabilizerTests.cs`
- Test: `Assets/Scripts/IteHost/Tests/EditMode/IteMarkerBridgeTests.cs`
- Test: `Assets/Scripts/IteHost/Tests/EditMode/IteHostBootstrapTests.cs`
- Test: `Assets/Scripts/IteHost/Tests/EditMode/EditorFakeScanTests.cs`

**Interfaces:**
- Consumes：Task 1 的 `MarkerStabilizer.Feed`、`Reset` 语义。
- Produces：
  - `MarkerStabilizer.ResetAll()`
  - `MarkerStabilizerProfile.DefaultLostAfterSeconds`（`const float`，值为 3）
  - `MarkerStabilizerProfile.lostAfterSeconds`（`float`）
  - `IteMarkerBridge.Rearm()`
  - `IteHostBootstrap.MarkerLostAfterSeconds`（`float`，只读）

- [ ] **Step 1：写失败的测试**

`MarkerStabilizerTests.cs`：文件顶部加 `using System.Collections.Generic;`，然后在 `Reset_ThenStableAgain_FiresAgain` 之后加：

```csharp
    /// <summary>
    /// 放行（marker-rescan D3）：所有码当作新的一次出现，重新判稳、各自再提交一次。
    /// </summary>
    [Test]
    public void ResetAll_EveryMarkerFiresAgain()
    {
        var stabilizer = new MarkerStabilizer(positionThreshold: 0.05f, rotationThreshold: 1f, smoothTime: 0.001f, stableSeconds: 2f);
        var fired = new List<string>();
        stabilizer.Stabilized += (id, _) => fired.Add(id);

        var pose = new Pose(new Vector3(1, 0, 0), Quaternion.identity);
        for (int i = 0; i < 2; i++)
        {
            stabilizer.Feed("A", pose, 1f);
            stabilizer.Feed("B", pose, 1f);
        }

        CollectionAssert.AreEqual(new[] { "A", "B" }, fired);

        stabilizer.ResetAll();
        stabilizer.Feed("A", pose, 1f);
        stabilizer.Feed("B", pose, 1f);
        Assert.AreEqual(2, fired.Count, "重新判稳要走满窗口");

        stabilizer.Feed("A", pose, 1f);
        stabilizer.Feed("B", pose, 1f);
        CollectionAssert.AreEqual(new[] { "A", "B", "A", "B" }, fired);
    }
```

`IteMarkerBridgeTests.cs`：在 `LostThenSeenAgain_ForwardsASecondTime` 之后加两个测试：

```csharp
        /// <summary>
        /// 丢失时长就是重扫门槛（marker-rescan D2）：移开视线不满丢失时长就看回来，算同一次出现；
        /// 满了再看回来，才算一次新的扫描。
        /// </summary>
        [Test]
        public void Rescan_RequiresLookingAwayForTheLostTime()
        {
            var source = new MockObservationSource();
            var session = new MarkerTrackingSession(source, lostAfterSeconds: 3f);
            var forwarded = new List<(MarkerKind kind, string payload, Pose pose)>();
            var visible = new[] { new MarkerObservation(MarkerPlatform.Quest, QuestPayload, Pose.identity) };

            using (var bridge = new IteMarkerBridge(session, Profile(), null,
                       (k, p, pose) => forwarded.Add((k, p, pose))))
            {
                void Run(int ticks, bool seen)
                {
                    for (int i = 0; i < ticks; i++)
                    {
                        if (seen)
                        {
                            source.SetNextPoll(visible);
                        }
                        else
                        {
                            source.SetNextPollEmpty();
                        }

                        bridge.Tick(Dt);
                    }
                }

                Run(5, seen: true);
                Assert.AreEqual(1, forwarded.Count);

                Run(20, seen: false); // 移开 2 秒
                Run(5, seen: true);
                Assert.AreEqual(1, forwarded.Count, "移开不满 3 秒，算同一次出现");

                Run(31, seen: false); // 移开 3.1 秒
                Run(5, seen: true);
                Assert.AreEqual(2, forwarded.Count, "移开满 3 秒再看回来，算新的一次扫描");
            }
        }

        /// <summary>放行（marker-rescan D3）：ITE 在等第一次扫描时，视野里的码不必先移开视线。</summary>
        [Test]
        public void Rearm_WhileVisible_ForwardsAgain()
        {
            var source = new MockObservationSource();
            var session = new MarkerTrackingSession(source, lostAfterSeconds: 3f);
            var forwarded = new List<(MarkerKind kind, string payload, Pose pose)>();

            using (var bridge = new IteMarkerBridge(session, Profile(), null,
                       (k, p, pose) => forwarded.Add((k, p, pose))))
            {
                source.SetNextPoll(new[]
                {
                    new MarkerObservation(MarkerPlatform.Quest, QuestPayload, Pose.identity)
                });

                for (int i = 0; i < 5; i++)
                {
                    bridge.Tick(Dt);
                }

                Assert.AreEqual(1, forwarded.Count);

                bridge.Rearm();
                for (int i = 0; i < 20; i++)
                {
                    bridge.Tick(Dt);
                }
            }

            Assert.AreEqual(2, forwarded.Count, "放行后重新判稳、再转发一次，之后仍然只算一次");
        }
```

`IteHostBootstrapTests.cs`：在 `AttachMarkerSession_AfterCreate_BindsImmediately` 之后加：

```csharp
        /// <summary>
        /// 丢失时长只有一个来源：装配点的配置资产（marker-rescan D2）。没接资产时退回 3 秒，扫码仍可工作。
        /// </summary>
        [Test]
        public void MarkerLostAfterSeconds_ComesFromProfile_FallsBackToThreeSeconds()
        {
            var fixture = new HostFixture();
            var profile = ScriptableObject.CreateInstance<MarkerStabilizerProfile>();
            try
            {
                Assert.AreEqual(3f, fixture.Host.MarkerLostAfterSeconds);

                profile.lostAfterSeconds = 4.5f;
                var so = new UnityEditor.SerializedObject(fixture.Host);
                so.FindProperty("stabilizerProfile").objectReferenceValue = profile;
                so.ApplyModifiedPropertiesWithoutUndo();

                Assert.AreEqual(4.5f, fixture.Host.MarkerLostAfterSeconds);
            }
            finally
            {
                Object.DestroyImmediate(profile);
                fixture.Dispose();
            }
        }
```

`EditorFakeScanTests.cs`：在 `Driver_Trigger_DispatchesObservedThenLostOnce` 里，把

```csharp
                Step(1.1f);
                Assert.AreEqual(1, lost);
                Step(1f);
                Assert.AreEqual(1, lost, "持续缺席只派发一次 Lost");
```

改为

```csharp
                Step(1.1f);
                Assert.AreEqual(0, lost, "缺席不满丢失时长（没有装配点时默认 3 秒，marker-rescan D2）");

                Step(2f);
                Assert.AreEqual(1, lost);
                Step(1f);
                Assert.AreEqual(1, lost, "持续缺席只派发一次 Lost");
```

- [ ] **Step 2：编译并确认失败**

请用户按一次 `Cmd+R`。期望编译失败，错误里提到 `ResetAll`、`Rearm`、`MarkerLostAfterSeconds`、`lostAfterSeconds`（`MarkerStabilizerProfile` 上）。除此之外不应有其他编译错误。

- [ ] **Step 3：标记层——`ResetAll` 与丢失时长字段**

`MarkerStabilizer.cs`：在 `Reset` 之后加

```csharp

    /// <summary>所有码都当作没出现过：下次喂入重新平滑、重新判稳（放行，marker-rescan D3）。</summary>
    public void ResetAll() => tracked.Clear();
```

并把类注释第 3 条末尾的 `要再提交，先 <see cref="Reset"/>（丢失）。` 改为 `要再提交，先 <see cref="Reset"/>（丢失）或 <see cref="ResetAll"/>（放行）。`

`MarkerStabilizerProfile.cs`：在类注释的「缺省值是**起点不是结论**」一段之前插入：

```csharp
/// 丢失时长 <see cref="lostAfterSeconds"/> 是例外，两端共用一个值（marker-rescan D2）：它是
/// 「移开视线多久才能重扫」的门槛，属于行为约定，不是噪声参数。
///
```

在 `public class MarkerStabilizerProfile : ScriptableObject` 的 `{` 之后、`[Serializable] public struct Settings` 之前插入：

```csharp
    /// <summary>没接配置资产时的丢失时长（秒）。</summary>
    public const float DefaultLostAfterSeconds = 3f;

    [Header("在场判定（两端共用）")]
    [Tooltip("连续这么久认不出一张码，才算它丢失（秒）。也是重扫门槛：移开视线这么久再看回来，才算一次新的扫描")]
    public float lostAfterSeconds = DefaultLostAfterSeconds;

```

- [ ] **Step 4：桥接——`Rearm` 与注释**

在 `IteMarkerBridge.cs` 的 `Tick` 方法之后加：

```csharp

        /// <summary>
        /// 放行：视野里的码都当作新的一次出现，重新平滑、重新判稳（marker-rescan D3）。
        /// 宿主在 ITE 发出扫码提示时调用——那是需要第一次扫描的时候，码一直在视野里也要能扫上。
        /// </summary>
        public void Rearm()
        {
            _questStabilizer.ResetAll();
            _picoStabilizer.ResetAll();
        }
```

把 `MaxGapCreditSeconds` 注释里的 `没丢失（会话滞回 1 秒）但隔得久时，` 改为 `没丢失（会话丢失时长默认 3 秒）但隔得久时，`。

把 `HandleLost` 里的

```csharp
            // 丢失即重置：下次再出现要重新走完稳定窗口才算新的一次扫码。
            // 这条同时让「二次锚定」回到人有意重扫的动作，而不是连续观测的第 2 帧。
```

改为

```csharp
            // 丢失即重置：下次再出现要重新走完稳定窗口才算新的一次扫码。
            // 丢失时长就是重扫门槛（marker-rescan D2）：移开视线够久再看回来，才是人有意重扫。
```

- [ ] **Step 5：装配点——统一丢失时长、提示可见时放行**

在 `IteHostBootstrap.cs` 里：

把 `stabilizerProfile` 的 Tooltip 改为：

```csharp
        [Tooltip("防抖参数（两端各一套）与丢失时长（两端共用）。留空则用出厂参数，扫码仍可工作")]
```

在 `public IteMarkerBridge MarkerBridge => _bridge;` 之后加：

```csharp

        /// <summary>
        /// 标记连续认不出多久算丢失，也是重扫门槛（marker-rescan D2）。输入层建会话时从这里取，
        /// 真机与编辑器只有这一个来源。没接配置资产时退回默认值。
        /// </summary>
        public float MarkerLostAfterSeconds => stabilizerProfile != null
            ? stabilizerProfile.lostAfterSeconds
            : MarkerStabilizerProfile.DefaultLostAfterSeconds;
```

把

```csharp
        private static void HandleScanPromptChanged(ScanPrompt prompt)
            => Debug.Log("[ITE Host] OnScanPromptChanged " + prompt.State
                         + " [" + string.Join(",", prompt.TourIds ?? Array.Empty<string>()) + "]");
```

改为

```csharp
        private void HandleScanPromptChanged(ScanPrompt prompt)
        {
            Debug.Log("[ITE Host] OnScanPromptChanged " + prompt.State
                      + " [" + string.Join(",", prompt.TourIds ?? Array.Empty<string>()) + "]");

            // 有提示 = ITE 在等第一次扫描：视野里已经提交过的码也要能直接扫上，不必先移开视线（marker-rescan D3）。
            // 没提示时再扫同一张码是重扫，必须先移开满丢失时长（D2）。
            if (prompt.State == ScanPromptState.Visible)
            {
                _bridge?.Rearm();
            }
        }
```

- [ ] **Step 6：输入层——删字段，改从宿主读**

`IteDeviceMarkerRig.cs`：删掉这 4 行（字段和它前面的空行）：

```csharp

        [SerializeField]
        [Tooltip("标记连续缺席多久算丢失")]
        private float lostAfterSeconds = 1.0f;
```

并把

```csharp
            _session = new MarkerTrackingSession(_source, lostAfterSeconds);
```

改为

```csharp
            // 丢失时长由装配点统一提供，真机与编辑器同一个来源（marker-rescan D2）
            _session = new MarkerTrackingSession(_source, host.MarkerLostAfterSeconds);
```

`IteEditorFakeScan.cs`：删掉这一行：

```csharp
        [SerializeField] float lostAfterSeconds = 1f;
```

把整个 `EnsureSession` 方法替换为（先找装配点，再按它的丢失时长建会话）：

```csharp
        private void EnsureSession()
        {
            if (_session != null)
            {
                return;
            }

            // 与 IteDeviceMarkerRig 同一套：连线为空就自己找，找不到出声。
            // 这里的连线跨预制体（本组件在 harness 预制体里，装配点在 IteTourRig 里），
            // 预制体资产存不了场景引用——没有兜底时，把 harness 拖进新场景会得到
            // 「按键有响应、会话在跑、就是没人接」的静默失效。
            if (host == null)
            {
                host = FindFirstObjectByType<IteHostBootstrap>();
            }

            // 丢失时长由装配点统一提供（marker-rescan D2）。没有装配点时假扫码不驱动任何导览，按默认值建会话即可。
            _source = new MockObservationSource();
            _session = new MarkerTrackingSession(
                _source,
                host != null ? host.MarkerLostAfterSeconds : MarkerStabilizerProfile.DefaultLostAfterSeconds);
            _session.Open();

            if (host == null)
            {
                Debug.LogError("[ITE Editor] 场景里没有 IteHostBootstrap，假扫码不会驱动任何导览。", this);
                return;
            }

            host.AttachMarkerSession(_session);
        }
```

- [ ] **Step 7：编译并确认通过**

请用户按一次 `Cmd+R`。跑 `A=MRBase.Localization.Tests` 和 `A=MRBase.Ite.Host.Tests`，期望全部通过。

- [ ] **Step 8：重新序列化资产，让新字段写进资产、旧字段从预制体里去掉**

```bash
cd /Users/wwj/Desktop/unity/MR_Base
unity command eval --project-path . 'UnityEditor.AssetDatabase.ForceReserializeAssets(new[] { "Assets/Settings/ITE/MarkerStabilizerProfile.asset", "Assets/Prefabs/ITE/IteDeviceHarness.prefab", "Assets/Prefabs/ITE/IteEditorHarness.prefab" }); return "ok";'
git diff --stat -- Assets/Settings/ITE Assets/Prefabs/ITE
git diff -- Assets/Settings/ITE Assets/Prefabs/ITE
```

期望的 diff：
- `MarkerStabilizerProfile.asset` 多出一行 `lostAfterSeconds: 3`；
- 两个预制体各少一行 `lostAfterSeconds: 1`。

如果某个预制体还有其他改动（重新序列化带出的无关格式变化），用 `git checkout -- <那个预制体>` 还原它。残留的 `lostAfterSeconds: 1` 会被 Unity 忽略，下次保存预制体时自动去掉。

- [ ] **Step 9：提交**

```bash
cd /Users/wwj/Desktop/unity/MR_Base
git add Assets/Scripts/Localization/MarkerStabilizer.cs Assets/Scripts/Localization/MarkerStabilizerProfile.cs \
  Assets/Scripts/IteHost/IteMarkerBridge.cs Assets/Scripts/IteHost/IteHostBootstrap.cs \
  Assets/Scripts/IteHost/IteDeviceMarkerRig.cs Assets/Scripts/IteHost/IteEditorFakeScan.cs \
  Assets/Scripts/Localization/Tests/EditMode/MarkerStabilizerTests.cs \
  Assets/Scripts/IteHost/Tests/EditMode/IteMarkerBridgeTests.cs \
  Assets/Scripts/IteHost/Tests/EditMode/IteHostBootstrapTests.cs \
  Assets/Scripts/IteHost/Tests/EditMode/EditorFakeScanTests.cs \
  Assets/Settings/ITE/MarkerStabilizerProfile.asset
git add Assets/Prefabs/ITE/IteDeviceHarness.prefab Assets/Prefabs/ITE/IteEditorHarness.prefab  # 若 Step 8 已还原，git 会忽略未改动的文件
git commit -m "$(cat <<'EOF'
feat(marker): 丢失时长全局 3 秒作为重扫门槛，ITE 发出扫码提示时放行

丢失时长挪到 MarkerStabilizerProfile，由装配点统一提供给真机与编辑器输入层（marker-rescan D2）。
扫码提示变为可见时桥接 Rearm，视野里的码不必先移开视线（marker-rescan D3）。

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 3：ITE——在播时扫当前 Tour 的码 = 定位，删除二次锚定许可（D4、D5）

**Files:**
- Modify: `Packages/com.uality.ite-tour/Runtime/Core/TourScanPolicy.cs`
- Modify: `Packages/com.uality.ite-tour/Runtime/Core/TourGuide.cs`
- Modify: `Packages/com.uality.ite-tour/Runtime/Core/TourDirector.cs`
- Modify: `Packages/com.uality.ite-tour/Runtime/Core/IteTourObject.cs`
- Test: `Packages/com.uality.ite-tour/Tests/Editor/TourScanPolicyTests.cs`
- Test: `Packages/com.uality.ite-tour/Tests/Editor/TourGuideTests.cs`

**Interfaces:**
- Consumes：无，与 Task 1、2 相互独立。
- Produces：
  - `TourDescriptor` 只剩 `TourId`、`DisplayType`；
  - `ScanDecision` 只剩 `Action`、`TourId`；
  - `GuideEffect` 去掉 `ConsumesSecondAnchor`；
  - `TourScanPolicy.Decide` 签名不变。

- [ ] **Step 1：改测试**

`TourScanPolicyTests.cs`：

1. 类注释第 2 行 `/// D14 把它从「三个订阅者的相互作用 + `_canAnchor` 的 await 竞态」抽成纯函数，` 改为 `/// design D14 把它从「三个订阅者的相互作用」抽成纯函数，`。
2. `Tour` 辅助方法替换为：

```csharp
        private static TourDescriptor Tour(string id, IteSpaceScene.Tour.DisplayType type = Normal)
            => new TourDescriptor { TourId = id, DisplayType = type };
```

3. 在 `Decide_AwaitingScan_AlwaysDisplayed_AnchorsWithoutActivating` 里删掉 `Assert.That(decision.ConsumesSecondAnchor, Is.False);` 这一行。
4. 删除整个 `Decide_AwaitingScanOnRegionalTrigger_DoesNotConsumeSecondAnchor_D14`（连同它上面的 `<summary>` 注释）。
5. 删除 `Decide_CurrentNormalPlaying_Ignores`、`Decide_CurrentRegionalPlayingWithAllowance_ReanchorsAndConsumesIt`、`Decide_CurrentRegionalPlayingWithoutAllowance_Ignores` 三个测试。
6. 把 `Decide_CurrentRegionalNotPlaying_ActivatesWithoutConsumingSecondAnchor` 整个替换为：

```csharp
        [Test]
        public void Decide_CurrentRegionalNotPlaying_Activates()
        {
            var decision = TourScanPolicy.Decide(Anchored("t1"), Tours(Tour("t1", Regional)), "t1");

            Assert.That(decision.Action, Is.EqualTo(ScanAction.Activate));
            Assert.That(decision.TourId, Is.EqualTo("t1"));
        }

        // ---- 当前 Tour 在播 ----

        /// <summary>
        /// marker-rescan D4：在播时再扫它的码 = 重新定位，不分展示类型、不限次数
        /// （取代 design D14 的「每次激活只允许一次二次锚定」与「normal 在播时忽略」）。
        /// 防误触发由底层负责：每次出现只提交一次、移开视线够久才算重扫。
        /// </summary>
        [TestCase(Normal)]
        [TestCase(Regional)]
        public void Decide_CurrentPlaying_Reanchors(IteSpaceScene.Tour.DisplayType type)
        {
            var decision = TourScanPolicy.Decide(Anchored("t1", "t1"), Tours(Tour("t1", type)), "t1");

            Assert.That(decision.Action, Is.EqualTo(ScanAction.Reanchor));
            Assert.That(decision.TourId, Is.EqualTo("t1"));
        }
```

7. 在 `Decide_IsPurelyStateDriven_SameMarkerTwiceIsNotDeduplicated` 里，把 `var tours = Tours(Tour("t1", Regional, secondAnchorAvailable: true));` 改为 `var tours = Tours(Tour("t1", Regional));`。

`TourGuideTests.cs`：

1. `Tour` 辅助方法替换为：

```csharp
        private static TourDescriptor Tour(string id, IteSpaceScene.Tour.DisplayType type = Regional)
            => new TourDescriptor { TourId = id, DisplayType = type };
```

2. 把整个 `Anchored_RescanCurrentRegional_ReanchorsAndOpensSettle` 替换为：

```csharp
        /// <summary>marker-rescan D4：在播时每次再扫它的码都重新定位，不分类型、不限次数，每次都打开结算窗口。</summary>
        [TestCase(Normal)]
        [TestCase(Regional)]
        public void Anchored_RescanPlayingTour_ReanchorsEveryTime(IteSpaceScene.Tour.DisplayType type)
        {
            var tours = Tours(Tour("t1", type));
            var guide = AnchoredOn("t1", tours);

            for (int i = 0; i < 3; i++)
            {
                var newPose = new Pose(new Vector3(4f + i, 5f, 6f), Quaternion.identity);

                var effect = guide.SubmitScan("t1", newPose, tours, out var decision);

                Assert.That(decision.Action, Is.EqualTo(ScanAction.Reanchor), $"第 {i + 1} 次");
                Assert.That(effect.ReanchorTourId, Is.EqualTo("t1"));
                Assert.That(effect.ReanchorPose, Is.EqualTo(newPose));
                Assert.That(effect.ActivateTourId, Is.Null);
                Assert.That(effect.Deactivate, Is.False);
                Assert.That(effect.AlwaysDisplayedVisible, Is.Null, "状态没变，显隐不动");
                Assert.That(guide.IsSettling, Is.True);
                Assert.That(guide.ActiveTourId, Is.EqualTo("t1"));

                guide.AfterPhysicsStep();
                Frame(guide, tours);
            }
        }
```

3. 在 `ScanAlwaysDisplayedWhileAwaiting_AnchorsOnly_ThenTailBecomesCurrent` 里删掉 `Assert.That(effect.ConsumesSecondAnchor, Is.False);` 这一行。

- [ ] **Step 2：编译并确认失败**

请用户按一次 `Cmd+R`。编译应当通过：测试不再引用许可相关成员，但这些成员这时还在。然后跑 `A=Uality.IteTour.Tests`，期望失败的恰好是这五项：
- `Decide_CurrentPlaying_Reanchors(normal)`
- `Decide_CurrentPlaying_Reanchors(regionalTrigger)`
- `Anchored_RescanPlayingTour_ReanchorsEveryTime(normal)`
- `Anchored_RescanPlayingTour_ReanchorsEveryTime(regionalTrigger)`
- `Decide_IsPurelyStateDriven_SameMarkerTwiceIsNotDeduplicated`

它们失败的原因是决策给出了 `Ignore`：normal 在播时一律忽略；regionalTrigger 的描述里不再带许可，默认为 false。

- [ ] **Step 3：改决策（D4），删决策层的许可（D5）**

`TourScanPolicy.cs`：

1. `ScanAction.Reanchor` 的注释改为：

```csharp
        /// <summary>
        /// 不换 Tour，仅按新位姿重新锚定（不销毁重建内容）。当前 Tour 在播时再扫它的码走这里（marker-rescan D4）；
        /// 等待扫码时扫到 alwaysDisplayed 也走这里：只锚定，不当当前 Tour（ite-current-tour D10）。
        /// </summary>
```

2. `TourDescriptor` 删掉 `SecondAnchorAvailable` 字段和它的注释（连同前面的空行）。
3. `ScanDecision` 删掉 `ConsumesSecondAnchor` 字段和它的注释（连同前面的空行）。
4. 类注释（`/// 标记扫描的**纯决策**。` 开头、到 `public static class TourScanPolicy` 之前的整段 `<summary>`）替换为：

```csharp
    /// <summary>
    /// 标记扫描的**纯决策**。无副作用、不碰 GameObject、不依赖帧或 async 时序。
    ///
    /// 源实现把这段逻辑摊在三个订阅同一事件的处理器里，一次扫码的效果取决于处理器的执行顺序和 await 时序
    /// （design D14）；这里由一次决策明确规定。
    ///
    /// 只回答「这次扫到的码要怎么处理」：激活、定位还是忽略。「这是不是一次有意的扫描」不归这里——
    /// 底层保证同一张码每次出现只提交一次、移开视线够久再看回来才算新的一次（marker-rescan D1、D2），
    /// 所以这里不限次数（marker-rescan D4）。状态转换（等待扫码 → 已定位）不在这里，归 <see cref="TourGuide"/>。
    /// </summary>
```

5. `Decide` 方法里，从 `if (state.State == GuideState.AwaitingScan)` 到方法结尾（`TryFind` 之前的 `}`）整段替换为：

```csharp
            if (state.State == GuideState.AwaitingScan)
            {
                // 等待扫码定位：唯一不受规则约束的入口——不看区域、不看当前 Tour，任何匹配的码都认
                // （ite-scan-region-gate D2）。
                //
                // alwaysDisplayed 只拿来锚定：它没有触发体积，当了当前 Tour 就永远离不开
                // （ite-current-tour D10）。
                return new ScanDecision
                {
                    Action = TourAssembly.CanBeCurrent(tour.DisplayType) ? ScanAction.Activate : ScanAction.Reanchor,
                    TourId = tour.TourId,
                };
            }

            // 已定位：只认当前 Tour 的码（ite-current-tour D6，取代 ite-scan-region-gate D1）。
            // 当前 Tour 优先级最高：人站在别的 Tour 的区域里、扫别的码，一律不认。
            // alwaysDisplayed 不会是当前 Tour（I5）；万一是，也不参与扫码切换。
            if (tour.TourId != state.CurrentTourId || !TourAssembly.CanBeCurrent(tour.DisplayType))
            {
                return ScanDecision.Ignore;
            }

            // 在播时再扫它的码 = 重新定位，不分展示类型、不限次数（marker-rescan D4）；没在播就开始播。
            return new ScanDecision
            {
                Action = tour.TourId == state.ActiveTourId ? ScanAction.Reanchor : ScanAction.Activate,
                TourId = tour.TourId,
            };
        }
```

- [ ] **Step 4：删效果层与 Tour 对象里的许可（D5）**

`TourGuide.cs`：

1. `GuideEffect.ReanchorTourId` 的注释改为：

```csharp
        /// <summary>只重新锚定、不换 Tour（在播时再扫当前 Tour 的码，marker-rescan D4；等待扫码时扫到 alwaysDisplayed）；null 表示无。</summary>
```

2. 删掉这三行（连同前面的空行）：

```csharp

        /// <summary>重锚是否消耗该 Tour 的二次锚定许可。</summary>
        public bool ConsumesSecondAnchor;
```

3. 在 `SubmitScan` 的 `case ScanAction.Reanchor:` 里删掉 `ConsumesSecondAnchor = decision.ConsumesSecondAnchor,` 这一行。

`TourDirector.cs`：

1. 类注释里的

```csharp
    /// （ite-guide-state-machine D7）。与源实现的差异见 design D14：源实现把扫描逻辑摊在三个订阅
    /// 同一事件的处理器里、二次锚定许可依赖 await 时序，这里都由一次决策明确规定。
```

改为

```csharp
    /// （ite-guide-state-machine D7）。与源实现的差异见 design D14：源实现把扫描逻辑摊在三个订阅
    /// 同一事件的处理器里，这里由一次决策明确规定；二次锚定许可已删除（marker-rescan D5）。
```

2. `Apply` 里的 `Reanchor(_assembler.Find(effect.ReanchorTourId), effect.ReanchorPose, effect.ConsumesSecondAnchor);` 改为 `Reanchor(_assembler.Find(effect.ReanchorTourId), effect.ReanchorPose);`。
3. `Descriptors()` 里删掉 `SecondAnchorAvailable = tour.CanSecondAnchor(),` 这一行。
4. 整个 `Reanchor` 方法替换为：

```csharp
        private void Reanchor(IteTourObject tour, Pose pose)
        {
            if (tour == null)
            {
                return;
            }

            tour.ChangeTourObjectTransform(pose.position, pose.rotation);
            Debug.Log("[ITE] Reanchor " + tour.TourId);
        }
```

`IteTourObject.cs`：

1. 类注释第一行 `/// 一个 Tour 在场景中的载体：持有描述与资源、按需构建/销毁内容、维护二次锚定许可。` 改为 `/// 一个 Tour 在场景中的载体：持有描述与资源、按需构建/销毁内容。`。
2. 删掉这三行（连同前面的空行）：

```csharp

        /// <summary>本 Tour 当前是否还允许一次二次锚定。</summary>
        private bool _canAnchor;
```

3. `TearDownScene` 里删掉这四行（连同前面的空行）：

```csharp

            // 源实现此处先 ResetSecondAnchor()（置 true）再置 false，前者是死调用（D14）。
            // 连同只有它一个调用方的 ResetSecondAnchor 一并删除。
            _canAnchor = false;
```

4. 删掉 `CanSecondAnchor` 和 `SecondAnchored`（连同它们之间和之后多余的空行，保留与 `GetAsset` 之间的一个空行）：

```csharp
        /// <summary>本 Tour 当前是否还允许一次二次锚定。</summary>
        public bool CanSecondAnchor()
            => _displayType == IteSpaceScene.Tour.DisplayType.regionalTrigger && _canAnchor;

        public void SecondAnchored() => _canAnchor = false;
```

5. `BuildSceneAsync` 里删掉 `_canAnchor = true;` 这一行，以及它后面的空行。

- [ ] **Step 5：确认没有残留**

```bash
cd /Users/wwj/Desktop/unity/MR_Base
grep -rn "SecondAnchor\|_canAnchor\|SecondAnchored\|consumesSecondAnchor" --include='*.cs' Packages/com.uality.ite-tour Assets/Scripts
```

期望：没有输出。

- [ ] **Step 6：编译，跑全部三个程序集**

请用户按一次 `Cmd+R`。依次跑 `A=Uality.IteTour.Tests`、`A=MRBase.Ite.Host.Tests`、`A=MRBase.Localization.Tests`，期望全部通过。

- [ ] **Step 7：提交**

```bash
cd /Users/wwj/Desktop/unity/MR_Base
git add Packages/com.uality.ite-tour/Runtime/Core/TourScanPolicy.cs Packages/com.uality.ite-tour/Runtime/Core/TourGuide.cs \
  Packages/com.uality.ite-tour/Runtime/Core/TourDirector.cs Packages/com.uality.ite-tour/Runtime/Core/IteTourObject.cs \
  Packages/com.uality.ite-tour/Tests/Editor/TourScanPolicyTests.cs Packages/com.uality.ite-tour/Tests/Editor/TourGuideTests.cs
git commit -m "$(cat <<'EOF'
feat(ite): 当前 Tour 在播时扫它的码一律重新定位，删除二次锚定许可

不分展示类型、不限次数（marker-rescan D4）；防误触发交给底层判稳与丢失时长。
二次锚定许可只为被取代的 design D14 规则服务，整套删除（marker-rescan D5）。

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

## 完成后

- 三个程序集全部通过后，按 spec §8 列出真机验证清单交给用户。**用户说「打包」才出包**；Quest 和 PICO 各测一遍。
- 真机验证时要看的日志：
  - `[ITE] 扫码 … → …`：每次提交与 ITE 的决定；
  - `[ITE Host] OnScanPromptChanged Visible`：放行时刻；
  - `[ITE Host] 同帧已提交过扫码`：D7 生效。
