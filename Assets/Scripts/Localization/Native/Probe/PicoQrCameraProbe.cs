#if MRBASE_HAS_PICO_SDK
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Unity.XR.PXR;
using ZXing;
using ZXing.Common;
using Unity.XR.PICO.TOBSupport;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// Standalone PICO RGB-camera capability probe. It is intentionally separate from MarkerProbe.
/// The hot path is throttled and never performs decode/UI work every render frame.
/// A ZXing adapter can be attached later without changing the camera acquisition policy.
/// </summary>
[AddComponentMenu("MR Base/Probe/PICO QR Camera Probe")]
[DisallowMultipleComponent]
public sealed class PicoQrCameraProbe : MonoBehaviour
{
    [Header("Performance")]
    [Range(2, 15)] public int sampleHz = 6;
    [Range(320, 1280)] public int requestedWidth = 640;
    [Range(240, 960)] public int requestedHeight = 480;
    public bool useAntiDistortion = true;

    private readonly object gate = new object();
    private byte[] reusableBuffer;
    private bool workerBusy;
    private bool cameraOpen;
    private bool serviceBound;
    private bool cameraStarting;
    private byte[] cameraBuffer;
    private GCHandle cameraBufferHandle;
    private bool frameAvailable;
    private int callbackFrames;
    private float nextSampleTime;
    private int frameWidth;
    private int frameHeight;
    private int frameStatus;
    private int lastAcquireResult = int.MinValue;
    private long sampledFrames;
    private long droppedFrames;
    private long lastFrameTimestamp;
    private string decoderStatus = "ZXing.Net 已加载";
    private string lastDecode = "(未解码)";
    private double lastSampleMs;
    private Stopwatch stopwatch;
    private BarcodeReaderGeneric barcodeReader;
    private readonly ConcurrentQueue<string> decodeResults = new ConcurrentQueue<string>();

    private void Start()
    {
        stopwatch = Stopwatch.StartNew();
        // Required by PICO's official CameraRendering sample before opening the 4U camera.
        PXR_Manager.EnableVideoSeeThrough = true;
        PXR_Enterprise.UseGlobalPose(true);
        barcodeReader = new BarcodeReaderGeneric
        {
            AutoRotate = false,
            TryInverted = false
        };
        barcodeReader.Options.PossibleFormats = new List<BarcodeFormat> { BarcodeFormat.QR_CODE };
        barcodeReader.Options.TryHarder = false;
        Debug.Log("[PicoQrCameraProbe] start; sampling is throttled and frame buffers are reused");
#if !MRBASE_HAS_PICO_SDK
        decoderStatus = "PICO SDK 不可用";
#else
        // Camera mode requests PICO's camera authorization token before binding the
        // enterprise service. Without isCamera=true the service may bind successfully,
        // but pxrcaptureservice rejects the later camera connection at SELinux level.
        if (!PXR_Enterprise.InitEnterpriseService(true))
        {
            Debug.LogError("[PicoQrCameraProbe] InitEnterpriseService failed");
            return;
        }
        PXR_Enterprise.BindEnterpriseService(OnServiceBound);
#endif
    }

    private void OnServiceBound(bool bound)
    {
        serviceBound = bound;
        Debug.Log($"[PicoQrCameraProbe] BindEnterpriseService={bound}");
        if (!bound) return;
        cameraStarting = true;
        PXR_Enterprise.Configurefor4U(new Dictionary<string, string>
        {
            { PXRCapture.KEY_OUTPUT_CAMERA_RAW_DATA, PXRCapture.VALUE_FALSE },
        });
        // Match PICO's CameraRendering sample: open first, then register the output buffer
        // only from the successful open callback. This ordering is required by the native
        // pxrcaptureservice on PICO 4 Ultra Enterprise.
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

    private void OnCameraOpened(bool opened)
    {
        cameraStarting = false;
        cameraOpen = opened;
        Debug.Log($"[PicoQrCameraProbe] OpenCameraAsyncfor4U={opened}");
        if (opened)
        {
            cameraBuffer = new byte[requestedWidth * requestedHeight * 4];
            cameraBufferHandle = GCHandle.Alloc(cameraBuffer, GCHandleType.Pinned);
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
            var p = PXR_Enterprise.GetCameraParametersNewfor4U(requestedWidth, requestedHeight);
            Debug.Log($"[PicoQrCameraProbe] intrinsics fx={p.fx:F2} fy={p.fy:F2} cx={p.cx:F2} cy={p.cy:F2}");
        }
    }

    private void OnImageAvailable(Frame frame)
    {
        callbackFrames++;
        frameWidth = (int)frame.width;
        frameHeight = (int)frame.height;
        frameStatus = frame.status;
        lastFrameTimestamp = (long)frame.timestamp;
        frameAvailable = true;
    }

    private void Update()
    {
        while (decodeResults.TryDequeue(out string decoded))
        {
            lastDecode = decoded;
            decoderStatus = "ZXing 解码完成";
            Debug.Log($"[PicoQrCameraProbe] QR decoded: {decoded}");
        }
        if (!cameraOpen || cameraStarting || !serviceBound || !frameAvailable || sampleHz <= 0 || Time.unscaledTime < nextSampleTime) return;
        nextSampleTime = Time.unscaledTime + 1f / sampleHz;
        lock (gate)
        {
            if (workerBusy)
            {
                droppedFrames++;
                return;
            }
            workerBusy = true;
        }

        var start = stopwatch.ElapsedTicks;
        int result = 0;
        lastAcquireResult = result;
        if (cameraBuffer == null || cameraBuffer.Length == 0)
        {
            lock (gate) workerBusy = false;
            return;
        }

        int size = cameraBuffer.Length;
        if (reusableBuffer == null || reusableBuffer.Length < size) reusableBuffer = new byte[size];
        Buffer.BlockCopy(cameraBuffer, 0, reusableBuffer, 0, size);
        frameAvailable = false;
        sampledFrames++;
        lastSampleMs = (stopwatch.ElapsedTicks - start) * 1000.0 / Stopwatch.Frequency;

        // Decode is deliberately isolated from Update. The reusable buffer remains owned by this
        // worker until it finishes; the next capture is dropped while workerBusy is true.
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                Result decoded = barcodeReader.Decode(
                    reusableBuffer,
                    frameWidth,
                    frameHeight,
                    RGBLuminanceSource.BitmapFormat.RGB32);
                if (decoded != null && !string.IsNullOrEmpty(decoded.Text))
                    decodeResults.Enqueue(decoded.Text);
                else
                    decoderStatus = "ZXing 未识别到 QR";
            }
            catch (Exception exception)
            {
                decoderStatus = "ZXing 解码异常: " + exception.GetType().Name;
                Debug.LogWarning($"[PicoQrCameraProbe] ZXing decode failed: {exception.Message}");
            }
            finally { lock (gate) workerBusy = false; }
        });
    }

    private void OnGUI()
    {
        const int pad = 24;
        GUI.Label(new Rect(pad, pad, 900, 28), "PICO QR Camera Probe (低开销取帧)");
        GUI.Label(new Rect(pad, pad + 32, 1100, 24),
            $"camera={cameraOpen} bound={serviceBound} acquire={lastAcquireResult} frame={frameWidth}x{frameHeight} status={frameStatus}");
        GUI.Label(new Rect(pad, pad + 58, 1100, 24),
            $"sampleHz={sampleHz} sampled={sampledFrames} dropped={droppedFrames} copyMs={lastSampleMs:F2}");
        GUI.Label(new Rect(pad, pad + 84, 1100, 24), $"decoder: {decoderStatus}  result: {lastDecode}");
        GUI.Label(new Rect(pad, pad + 110, 1100, 24), $"frameTimestamp(ns): {lastFrameTimestamp}");
    }

    private void OnDestroy()
    {
        if (cameraOpen || cameraStarting) PXR_Enterprise.CloseCamerafor4U();
        if (cameraBufferHandle.IsAllocated) cameraBufferHandle.Free();
        if (serviceBound) PXR_Enterprise.UnBindEnterpriseService();
        cameraOpen = false;
        serviceBound = false;
        Debug.Log($"[PicoQrCameraProbe] stop; sampled={sampledFrames} dropped={droppedFrames}");
    }
}
#endif
