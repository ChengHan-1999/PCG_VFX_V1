using System.Collections;
using System.IO;
using UnityEngine;

namespace PCG.VFX
{
    /// <summary>
    /// Captures the final camera output at a reproducible elapsed time after Play begins.
    /// Intended for offline DreamSim evaluation, not for runtime gameplay screenshots.
    /// </summary>
    public class PcgDreamSimScreenCaptureRunner : MonoBehaviour
    {
        [Header("Capture Timing")]
        [Tooltip("Capture once after this many real-time seconds from entering Play mode. The PCG Runner applies its theme immediately; its VFX event is sent on the next frame.")]
        [SerializeField, Min(0f)] private float captureDelaySeconds = 3f;
        [SerializeField] private bool captureOnStart = true;

        [Header("Exact PNG Resolution")]
        [Tooltip("Requested portrait output. Set this to 1920 x 1080 if you later want landscape captures.")]
        [SerializeField, Min(1)] private int captureWidth = 1080;
        [SerializeField, Min(1)] private int captureHeight = 1920;

        [Header("Scene References")]
        [Tooltip("Leave empty to use Main Camera.")]
        [SerializeField] private Camera captureCamera;
        [Tooltip("Assign the PcgGenerationTestRunner on VFX_MagicCircle so PNGs are named after its current Profile Id.")]
        [SerializeField] private PcgGenerationTestRunner pcgRunner;

        [Header("Output")]
        [Tooltip("Written below the Unity project folder. Default resolves to EvaluationCaptures/DreamSim/Static.")]
        [SerializeField] private string outputRelativePath = "EvaluationCaptures/DreamSim/Static";
        [Tooltip("Optional file-name override. Leave blank to use the PcgGenerationTestRunner Profile Id, such as Player_01.")]
        [SerializeField] private string captureLabelOverride;
        [SerializeField] private bool overwriteExisting = true;
        [SerializeField] private bool appendUtcTimestamp;

        private Coroutine captureCoroutine;

        private void Start()
        {
            if (captureOnStart)
            {
                CaptureAfterConfiguredDelay();
            }
        }

        [ContextMenu("Capture After Configured Delay")]
        public void CaptureAfterConfiguredDelay()
        {
            if (captureCoroutine != null)
            {
                StopCoroutine(captureCoroutine);
            }

            captureCoroutine = StartCoroutine(CaptureRoutine());
        }

        [ContextMenu("Capture Immediately")]
        public void CaptureImmediately()
        {
            if (captureCoroutine != null)
            {
                StopCoroutine(captureCoroutine);
            }

            captureCoroutine = StartCoroutine(CaptureRoutine(true));
        }

        private IEnumerator CaptureRoutine(bool captureImmediately = false)
        {
            if (!captureImmediately && captureDelaySeconds > 0f)
            {
                yield return new WaitForSecondsRealtime(captureDelaySeconds);
            }

            // Ensures the PCG Runner's Start call, material-property blocks, and delayed VFX event
            // have all been processed before the camera is rendered for the PNG.
            yield return new WaitForEndOfFrame();

            Camera resolvedCamera = ResolveCamera();
            if (resolvedCamera == null)
            {
                Debug.LogError("[PCG VFX] DreamSim capture failed: no camera was assigned and Main Camera was not found.", this);
                captureCoroutine = null;
                yield break;
            }

            string outputPath = BuildOutputPath();
            yield return CaptureCameraToPng(resolvedCamera, outputPath);
            captureCoroutine = null;
        }

        private IEnumerator CaptureCameraToPng(Camera sourceCamera, string outputPath)
        {
            RenderTexture temporaryTarget = RenderTexture.GetTemporary(
                captureWidth,
                captureHeight,
                24,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.sRGB);
            RenderTexture previousTarget = sourceCamera.targetTexture;
            RenderTexture previousActive = RenderTexture.active;
            Texture2D outputTexture = null;

            try
            {
                sourceCamera.targetTexture = temporaryTarget;
                sourceCamera.Render();

                RenderTexture.active = temporaryTarget;
                outputTexture = new Texture2D(captureWidth, captureHeight, TextureFormat.RGB24, false, false);
                outputTexture.ReadPixels(new Rect(0, 0, captureWidth, captureHeight), 0, 0, false);
                outputTexture.Apply(false, false);

                Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
                File.WriteAllBytes(outputPath, outputTexture.EncodeToPNG());
                Debug.Log(
                    "[PCG VFX] DreamSim screenshot captured: " + outputPath +
                    " (" + captureWidth + "x" + captureHeight + ", t=" +
                    captureDelaySeconds.ToString("0.###") + "s)",
                    this);
            }
            finally
            {
                sourceCamera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(temporaryTarget);

                if (outputTexture != null)
                {
                    Destroy(outputTexture);
                }
            }

            yield return null;
        }

        private Camera ResolveCamera()
        {
            return captureCamera != null ? captureCamera : Camera.main;
        }

        private string BuildOutputPath()
        {
            string fileLabel = !string.IsNullOrWhiteSpace(captureLabelOverride)
                ? captureLabelOverride.Trim()
                : ResolveProfileLabel();
            fileLabel = MakeFileNameSafe(fileLabel);

            if (appendUtcTimestamp)
            {
                fileLabel += "_" + System.DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            }

            string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", outputRelativePath));
            string filename = fileLabel + ".png";
            string path = Path.Combine(directory, filename);

            if (!overwriteExisting && !appendUtcTimestamp)
            {
                int suffix = 1;
                while (File.Exists(path))
                {
                    path = Path.Combine(directory, fileLabel + "_" + suffix + ".png");
                    suffix++;
                }
            }

            return path;
        }

        private string ResolveProfileLabel()
        {
            PcgGenerationTestRunner runner = pcgRunner != null
                ? pcgRunner
                : GetComponent<PcgGenerationTestRunner>();

            return runner != null && !string.IsNullOrWhiteSpace(runner.ProfileId)
                ? runner.ProfileId
                : "DreamSimCapture";
        }

        private static string MakeFileNameSafe(string value)
        {
            foreach (char invalidCharacter in Path.GetInvalidFileNameChars())
            {
                value = value.Replace(invalidCharacter, '_');
            }

            return string.IsNullOrWhiteSpace(value) ? "DreamSimCapture" : value;
        }
    }
}
