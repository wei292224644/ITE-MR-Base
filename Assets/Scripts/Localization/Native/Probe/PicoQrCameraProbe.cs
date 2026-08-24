#if MRBASE_HAS_PICO_SDK
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Unity.XR.PXR;
using Unity.XR.PICO.TOBSupport;
using UnityEngine;
using Debug = UnityEngine.Debug;
// TOBSupport 里也有个 Pose，与 UnityEngine.Pose 同名。这里全用后者。
using Pose = UnityEngine.Pose;

/// <summary>
/// PICO 相机流上的 fiducial marker 度量台架。
///
/// 名字还叫 QrCameraProbe 是历史遗留——检测器已由 ZXing/QR 换成 AprilTag（change
/// pico-camera-fiducial-tracking）。改名要连 .cs、类名和场景引用一起动，等管线在真机上跑通再做，
/// 免得把重命名的风险和换检测器的风险混在一起查。
///
/// 检测本身不在这里，在 <see cref="AprilTagDetectorCore"/>——探针与生产 Provider 共用同一份
/// （design D8）。这里只负责取帧、节流、坐标合成和把数字打出来。
/// </summary>
[AddComponentMenu("MR Base/Probe/PICO Fiducial Camera Probe")]
[DisallowMultipleComponent]
public sealed class PicoQrCameraProbe : MonoBehaviour
{
    [Header("Performance")]
    [Range(2, 15)] public int sampleHz = 6;

    // 分辨率按最远工作距离反推，不是拍的（design D4）。
    // 检测四边形边长 88.9 mm，像素跨度 = 88.9mm × fx / 距离，AprilTag 实用下限约 20-25 px：
    //   640x480  (fx≈407)：2 m 处只有 18 px —— 不够
    //   1280x960 (fx≈814)：2 m 处 36 px    —— 够
    // 内参按同一尺寸查，焦距会跟着变，不需要额外的裁剪换算。
    [Range(320, 2048)] public int requestedWidth = 1280;
    [Range(240, 1536)] public int requestedHeight = 960;

    [Header("Fixture")]
    [Tooltip("检测四边形的边长（米），即 width_at_border 那一圈，不是整张标图的幅面。" +
             "tagStandard41h12 的检测框只占图幅的 5/9：160 mm 图幅 → 88.9 mm。用打印后实测值。")]
    public float tagSizeMeters = 0.0889f;

    [Header("Detector tuning")]
    [Tooltip("四边形检测阶段的降采样。只影响检测，位解码仍在全分辨率做。近距可开 2 省算力，远距落到 1。")]
    [Range(1f, 4f)] public float quadDecimate = 2f;
    [Tooltip("检测前的高斯模糊。噪声大时调高，代价是丢细节。")]
    [Range(0f, 2f)] public float quadSigma = 0f;
    [Tooltip("四边形边线的亚像素精修。角点精度直接进单应，默认应开。")]
    public bool refineEdges = true;
    [Range(0.1f, 1f)] public float decodeSharpening = 0.25f;
    [Tooltip("原生检测器的线程数。0 = 按 CPU 核数自动。")]
    [Range(0, 8)] public int detectorThreads = 0;
    [Tooltip("相机缓冲区行序。选错的表现是位姿沿 Y 镜像而非报错，所以必须显式，不能靠试。" +
             "EditMode 测试已断言两种行序恰好差一个 Y 镜像，真机上只需判定属于哪一种。")]
    public bool bottomUpRows;

    [Header("Feedback")]
    [Tooltip("在相机正前方挂一个红色方块。它与识别无关，只用来确认渲染本身是通的。")]
    public bool showSelfTestBox = true;
    [Tooltip("识别到 marker 后，在其位置显示的方块边长（米）。")]
    [Range(0.02f, 0.30f)] public float markerBoxSize = 0.08f;
    [Tooltip("超过这个时长没再检出就把方块隐藏，避免停在旧位置误导判断。")]
    [Range(0.2f, 5f)] public float markerBoxHoldSeconds = 1f;

    private readonly object gate = new object();
    private readonly ConcurrentQueue<DetectedMarker> detectResults = new ConcurrentQueue<DetectedMarker>();
    private readonly List<AprilTagDetectorCore.TagObservation> observations =
        new List<AprilTagDetectorCore.TagObservation>();

    private AprilTagDetectorCore detector;
    private byte[] cameraBuffer;
    private GCHandle cameraBufferHandle;
    private byte[] frameBuffer;
    private bool frameAvailable;
    private int callbackFrames;
    private Pose latestFramePose = Pose.identity;
    private bool workerBusy;
    private bool cameraOpen;
    private bool serviceBound;
    private bool cameraStarting;
    private volatile bool pendingCameraStart;
    private float nextSampleTime;
    private int frameWidth;
    private int frameHeight;
    private int frameStatus;
    private Camera mainCamera;
    private long sampledFrames;
    private long droppedFrames;
    private long lastFrameTimestamp;
    private long detectAttempts;
    private long detectSuccesses;
    private double lastCopyMs;
    private double lastDetectMs;
    private Stopwatch stopwatch;

    private float cameraFx, cameraFy, cameraCx, cameraCy;
    private bool intrinsicsValid;

    private string detectorStatus = "(未启动)";
    private string lastDetection = "(未检出)";
    private string poseStatus = "(未解算)";
    private int poseSolveFailures;

    private GameObject markerBox;
    private float markerBoxHideTime;

    private readonly struct DetectedMarker
    {
        public readonly int Id;
        public readonly int Hamming;
        public readonly float DecisionMargin;
        public readonly bool HasPose;
        public readonly Pose WorldPose;

        public DetectedMarker(int id, int hamming, float decisionMargin, bool hasPose, Pose worldPose)
        {
            Id = id;
            Hamming = hamming;
            DecisionMargin = decisionMargin;
            HasPose = hasPose;
            WorldPose = worldPose;
        }
    }

    private void Start()
    {
        stopwatch = Stopwatch.StartNew();
        // Required by PICO's official CameraRendering sample before opening the 4U camera.
        PXR_Manager.EnableVideoSeeThrough = true;
        PXR_Enterprise.UseGlobalPose(true);
        if (showSelfTestBox) CreateSelfTestBox();

        // Camera mode requests PICO's camera authorization token before binding the
        // enterprise service. Without isCamera=true the service may bind successfully,
        // but pxrcaptureservice rejects the later camera connection at SELinux level.
        if (!PXR_Enterprise.InitEnterpriseService(true))
        {
            detectorStatus = "企业服务初始化失败";
            Debug.LogError("[PicoFiducialProbe] InitEnterpriseService failed");
            return;
        }

        PXR_Enterprise.BindEnterpriseService(OnServiceBound);
    }

    private void OnServiceBound(bool bound)
    {
        serviceBound = bound;
        Debug.Log($"[PicoFiducialProbe] BindEnterpriseService={bound}");
        if (!bound) return;

        // 4U 推送式取流，与官方 CameraRendering 样例同一条栈（design D13）。
        // 不走 OpenVSTCamera / AcquireVSTCameraFrameAntiDistortion：camOpenned 不共享，混用连续 result=-1。
        cameraStarting = true;
        PXR_Enterprise.Configurefor4U(new Dictionary<string, string>
        {
            { PXRCapture.KEY_OUTPUT_CAMERA_RAW_DATA, PXRCapture.VALUE_FALSE },
        });
        PXR_Enterprise.OpenCameraAsyncfor4U(OnCameraOpened, new Dictionary<string, string>
        {
            { PXRCapture.KEY_MCTF, PXRCapture.VALUE_TRUE },
            { PXRCapture.KEY_EIS, PXRCapture.VALUE_FALSE },
            { PXRCapture.KEY_MFNR, PXRCapture.VALUE_TRUE }
        });
    }

    private IntPtr UnsafeBufferPointer()
    {
        return cameraBufferHandle.IsAllocated ? cameraBufferHandle.AddrOfPinnedObject() : IntPtr.Zero;
    }

    // The SDK raises this from capturelib's binder thread while UnityMain's JNIEnv is still
    // attached to it. Calling back into the plugin from here aborts the process inside
    // startPerformance's FindClass (CheckJNI: "using JNIEnv* from thread ... UnityMain").
    // So only latch a flag; the real stream setup runs from Update on the main thread.
    private void OnCameraOpened(bool opened)
    {
        cameraStarting = false;
        cameraOpen = opened;
        pendingCameraStart = opened;
        Debug.Log($"[PicoFiducialProbe] OpenCameraAsyncfor4U={opened}");
    }

    private void StartCameraStream()
    {
        // RGB32，4 字节/像素——4U 推送式回调缓冲区的格式。
        cameraBuffer = new byte[requestedWidth * requestedHeight * 4];
        cameraBufferHandle = GCHandle.Alloc(cameraBuffer, GCHandleType.Pinned);
        frameBuffer = new byte[cameraBuffer.Length];
        IntPtr bufferPointer = UnsafeBufferPointer();
        PXR_Enterprise.SetCameraFrameBufferfor4U(
            requestedWidth,
            requestedHeight,
            ref bufferPointer,
            OnImageAvailable);
        PXR_Enterprise.StartGetImageDatafor4U(
            PXRCaptureRenderMode.PXRCapture_RenderMode_LEFT,
            requestedWidth,
            requestedHeight);

        RGBCameraParamsNew p = PXR_Enterprise.GetCameraParametersNewfor4U(requestedWidth, requestedHeight);
        cameraFx = (float)p.fx;
        cameraFy = (float)p.fy;
        cameraCx = (float)p.cx;
        cameraCy = (float)p.cy;
        intrinsicsValid = cameraFx > 0f && cameraFy > 0f;
        Debug.Log($"[PicoFiducialProbe] intrinsics fx={p.fx:F2} fy={p.fy:F2} cx={p.cx:F2} cy={p.cy:F2} " +
                  $"l_pos={p.l_pos} l_rot={p.l_rot}");

        // 官方样例只打印外参，不乘进 FrameTarget（design D13）。这里同样只记日志。
        if (PXR_Enterprise.GetCameraExtrinsicsfor4U(out Matrix4x4 leftExtrinsics, out Matrix4x4 rightExtrinsics))
        {
            Debug.Log($"[PicoFiducialProbe] extrinsics L=\n{leftExtrinsics}\nR=\n{rightExtrinsics}");
        }
        else
        {
            Debug.LogWarning("[PicoFiducialProbe] GetCameraExtrinsicsfor4U 失败（只影响日志，不参与合成）");
        }

        detector = new AprilTagDetectorCore(
            requestedWidth,
            requestedHeight,
            detectorThreads > 0 ? detectorThreads : Mathf.Max(1, SystemInfo.processorCount - 1))
        {
            QuadDecimate = quadDecimate,
            QuadSigma = quadSigma,
            RefineEdges = refineEdges ? 1 : 0,
            DecodeSharpening = decodeSharpening,
            BottomUpRows = bottomUpRows
        };
        detectorStatus = "AprilTag tagStandard41h12 已就绪";
        Debug.Log(
            $"[PicoFiducialProbe] detector ready {requestedWidth}x{requestedHeight} " +
            $"decimate={quadDecimate} sigma={quadSigma} refine={refineEdges} threads={detector.ThreadCount}");
    }

    /// <summary>
    /// SDK 推送式回调，每帧触发一次。只锁存字段，不做任何重活——这个回调此前没有出现过
    /// 崩溃（真正的崩溃点是 OpenCameraAsyncfor4U 自己的开相机回调，见 OnCameraOpened）。
    /// </summary>
    private void OnImageAvailable(Frame frame)
    {
        callbackFrames++;
        frameWidth = (int)frame.width;
        frameHeight = (int)frame.height;
        frameStatus = frame.status;
        lastFrameTimestamp = (long)frame.timestamp;
        // 锁存该帧曝光时刻的传感器位姿。合成时由 PicoEnterpriseCameraPose 翻进 Unity 追踪系，
        // 不在这里转，也不套外参。Pose 不是原子写入，读写必须同一把锁。
        lock (gate)
        {
            latestFramePose = frame.pose;
        }
        frameAvailable = true;
    }

    private void Update()
    {
        if (pendingCameraStart)
        {
            pendingCameraStart = false;
            StartCameraStream();
        }

        DrainResults();

        if (markerBox != null && markerBox.activeSelf && Time.unscaledTime > markerBoxHideTime)
        {
            markerBox.SetActive(false);
        }

        if (!cameraOpen || cameraStarting || !serviceBound || detector == null || !frameAvailable) return;
        if (sampleHz <= 0 || Time.unscaledTime < nextSampleTime) return;
        nextSampleTime = Time.unscaledTime + 1f / sampleHz;

        Pose framePose;
        lock (gate)
        {
            if (workerBusy)
            {
                droppedFrames++;
                return;
            }
            workerBusy = true;
            framePose = latestFramePose;
        }

        long copyStart = stopwatch.ElapsedTicks;
        // cameraBuffer 由原生回调异步覆写（推送式），必须在这里拍一份快照给工作线程独占，
        // 不能像拉取式那样直接把 cameraBuffer 传给 worker——那样会跟下一帧的原生写入撞车。
        Buffer.BlockCopy(cameraBuffer, 0, frameBuffer, 0, cameraBuffer.Length);
        frameAvailable = false;
        lastCopyMs = (stopwatch.ElapsedTicks - copyStart) * 1000.0 / Stopwatch.Frequency;

        if (mainCamera == null) mainCamera = Camera.main;
        if (sampledFrames % 30 == 0)
        {
            Debug.Log(
                $"[PicoFiducialProbe] framePose={framePose.position:F3} " +
                $"unityCam={mainCamera?.transform.position:F3}（对照用；合成走 ToUnityTrackingPose(frame.pose)）");
        }

        sampledFrames++;
        DispatchDetection(framePose);
    }

    /// <summary>
    /// 检测跑在工作线程上。<see cref="AprilTagDetectorCore.Detect"/> 全程只做 P/Invoke 与数组读写，
    /// 不触碰任何 Unity API，所以不需要主线程。frameBuffer 在 workerBusy 期间由该线程独占，
    /// 期间新到的采样直接丢弃并计数。
    /// </summary>
    private void DispatchDetection(Pose cameraPose)
    {
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                long start = stopwatch.ElapsedTicks;
                Interlocked.Increment(ref detectAttempts);
                detector.Detect(frameBuffer, observations);
                lastDetectMs = (stopwatch.ElapsedTicks - start) * 1000.0 / Stopwatch.Frequency;

                if (observations.Count == 0)
                {
                    detectorStatus = "未检出";
                }
                else
                {
                    Interlocked.Increment(ref detectSuccesses);
                    detectorStatus = $"检出 {observations.Count} 个";
                }

                foreach (AprilTagDetectorCore.TagObservation observation in observations)
                {
                    bool hasPose = TrySolveWorldPose(observation, cameraPose, out Pose worldPose);
                    detectResults.Enqueue(new DetectedMarker(
                        observation.Id, observation.Hamming, observation.DecisionMargin, hasPose, worldPose));
                }

                // 把"准确率"变成数字而不是体感。同时把误检与漏检分开：未检出只增 attempts，
                // 检出但 Hamming 高的走 DecisionMargin 那条日志。
                long attempts = Interlocked.Read(ref detectAttempts);
                if (attempts % 30 == 0)
                {
                    long hits = Interlocked.Read(ref detectSuccesses);
                    Debug.Log(
                        $"[PicoFiducialProbe] detect {hits}/{attempts} = {(100.0 * hits / attempts):F0}% " +
                        $"@{frameWidth}x{frameHeight} detectMs={lastDetectMs:F1}");
                }
            }
            catch (Exception exception)
            {
                detectorStatus = "检测异常: " + exception.GetType().Name;
                Debug.LogWarning($"[PicoFiducialProbe] detect failed: {exception}");
            }
            finally
            {
                lock (gate) workerBusy = false;
            }
        });
    }

    private void DrainResults()
    {
        while (detectResults.TryDequeue(out DetectedMarker detected))
        {
            lastDetection = $"id={detected.Id} hamming={detected.Hamming} margin={detected.DecisionMargin:F1}";
            if (detected.HasPose)
            {
                ShowMarkerBox(detected.WorldPose);
                Debug.Log(
                    $"[PicoFiducialProbe] tag {detected.Id} hamming={detected.Hamming} " +
                    $"margin={detected.DecisionMargin:F1} pos={detected.WorldPose.position:F3} " +
                    $"rot={detected.WorldPose.rotation.eulerAngles:F1}");
            }
            else
            {
                Debug.Log($"[PicoFiducialProbe] tag {detected.Id} 检出但无位姿");
            }
        }
    }

    /// <summary>由四个真角点解出世界位姿。跑在检测线程上，只做纯数学。</summary>
    private bool TrySolveWorldPose(
        in AprilTagDetectorCore.TagObservation observation, Pose cameraPose, out Pose worldPose)
    {
        worldPose = default;
        if (!intrinsicsValid)
        {
            poseStatus = "内参不可用";
            return false;
        }

        if (!AprilTagDetectorCore.TrySolvePose(
                observation, tagSizeMeters, cameraFx, cameraFy, cameraCx, cameraCy,
                out Pose markerInCamera))
        {
            poseSolveFailures++;
            poseStatus = $"单应求解失败 x{poseSolveFailures}";
            return false;
        }

        worldPose = PicoEnterpriseCameraPose.ComposeWorld(cameraPose, markerInCamera);
        Debug.Log(
            $"[PicoFiducialProbe] cam={cameraPose.position:F3} marker@cam={markerInCamera.position:F3} " +
            $"dist={markerInCamera.position.magnitude:F3}m world={worldPose.position:F3}");
        poseStatus = "位姿已解算";
        return true;
    }

    /// <summary>
    /// 挂在主相机上的参考方块。看不到它 = 渲染/图层的问题；
    /// 看得到它但看不到绿方块 = 位姿算错了。两种故障不能混在一起查。
    /// </summary>
    private void CreateSelfTestBox()
    {
        Camera camera = Camera.main;
        if (camera == null)
        {
            Debug.LogWarning("[PicoFiducialProbe] 没有 Camera.main，跳过自检方块");
            return;
        }

        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = "PicoFiducialProbe SelfTestBox";
        Destroy(box.GetComponent<Collider>());
        ApplyBoxMaterial(box, new Color(0.9f, 0.15f, 0.15f));
        box.transform.SetParent(camera.transform, false);
        box.transform.localPosition = new Vector3(0f, -0.15f, 0.5f);
        box.transform.localScale = Vector3.one * 0.05f;
        Debug.Log("[PicoFiducialProbe] 自检方块已挂到 Camera.main 前方 0.5 m");
    }

    private void ApplyBoxMaterial(GameObject box, Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit")
                        ?? Shader.Find("Universal Render Pipeline/Lit");
        var renderer = box.GetComponent<Renderer>();
        if (shader != null)
        {
            renderer.material = new Material(shader);
        }
        else
        {
            Debug.LogWarning("[PicoFiducialProbe] 找不到 URP shader，方块会是默认材质");
        }

        renderer.material.color = color;
    }

    private void ShowMarkerBox(Pose pose)
    {
        if (markerBox == null)
        {
            markerBox = GameObject.CreatePrimitive(PrimitiveType.Cube);
            markerBox.name = "PicoFiducialProbe MarkerBox";
            Destroy(markerBox.GetComponent<Collider>());
            // 内置默认材质在 URP 下是洋红色。
            ApplyBoxMaterial(markerBox, new Color(0.1f, 0.9f, 0.3f));
        }

        // 方块贴在板面上而不是嵌进去一半：沿法线抬高半个边长。
        markerBox.transform.SetPositionAndRotation(
            pose.position + pose.forward * (markerBoxSize * 0.5f),
            pose.rotation);
        markerBox.transform.localScale = Vector3.one * markerBoxSize;
        markerBox.SetActive(true);
        markerBoxHideTime = Time.unscaledTime + markerBoxHoldSeconds;
    }

    private void OnGUI()
    {
        const int pad = 24;
        GUI.Label(new Rect(pad, pad, 900, 28), "PICO Fiducial Camera Probe (AprilTag)");
        GUI.Label(new Rect(pad, pad + 32, 1100, 24),
            $"camera={cameraOpen} bound={serviceBound} callbacks={callbackFrames} " +
            $"frame={frameWidth}x{frameHeight} status={frameStatus}");
        GUI.Label(new Rect(pad, pad + 58, 1100, 24),
            $"sampleHz={sampleHz} sampled={sampledFrames} dropped={droppedFrames} " +
            $"copyMs={lastCopyMs:F2} detectMs={lastDetectMs:F1}");
        GUI.Label(new Rect(pad, pad + 84, 1100, 24),
            $"detector: {detectorStatus}  last: {lastDetection}  hit={detectSuccesses}/{detectAttempts}");
        GUI.Label(new Rect(pad, pad + 110, 1100, 24), $"frameTimestamp(ns): {lastFrameTimestamp}");
        GUI.Label(new Rect(pad, pad + 136, 1100, 24),
            $"pose: {poseStatus}  box={(markerBox != null && markerBox.activeSelf ? markerBox.transform.position.ToString("F3") : "隐藏")}");
    }

    private void OnDestroy()
    {
        if (cameraOpen || cameraStarting) PXR_Enterprise.CloseCamerafor4U();
        if (cameraBufferHandle.IsAllocated) cameraBufferHandle.Free();
        if (serviceBound) PXR_Enterprise.UnBindEnterpriseService();
        detector?.Dispose();
        detector = null;
        cameraOpen = false;
        serviceBound = false;
        Debug.Log(
            $"[PicoFiducialProbe] stop; sampled={sampledFrames} dropped={droppedFrames} " +
            $"detect={detectSuccesses}/{detectAttempts}");
    }
}
#endif
