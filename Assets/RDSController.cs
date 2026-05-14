using UnityEngine;

[System.Serializable]
public class DisplayProfile
{
    [Header("Resolution")]
    public int widthPx = 3840;
    public int heightPx = 2160;

    [Header("Pixel Pitch")]
    [Tooltip("pixel pitch [mm/px]")]
    public float pp = 0.1845236f;

    [Header("Quad Renderers")]
    public Renderer leftQuadRenderer;
    public Renderer rightQuadRenderer;

    [Header("Cameras")]
    public Camera leftCamera;
    public Camera rightCamera;
}

public class RDSController : MonoBehaviour
{
    // =========================================================
    // Display Profile
    // =========================================================

    [Header("Display Profile")]
    [Tooltip("0: curved display, 1: flat display")]
    public int displayNum = 0;

    [Tooltip("Camera L を出す Unity Display index。0 = Display 1, 1 = Display 2")]
    public int displayL = 1;

    [Tooltip("Camera R を出す Unity Display index。0 = Display 1, 1 = Display 2")]
    public int displayR = 2;

    public DisplayProfile[] displayProfiles;

    // =========================================================
    // Geometry constants
    // =========================================================

    // 1000R曲面ディスプレイの曲率半径 [mm]
    private const float Curvature = 1000.0f;

    private float worldUnitToMm = 1.0f;

    // =========================================================
    // Viewer
    // =========================================================

    [Header("Viewer Position [world]")]
    public float viewerXWorld = 0.0f;
    public float viewerYWorld = 0.0f;
    public float viewerZWorld = 1000.0f;

    [Header("IPD")]
    public float IPD = 63.0f;

    // =========================================================
    // Target disparity angle
    // =========================================================

    [Header("Target Disparity Angle [deg]")]
    public float targetAngleDeg = 0.5f;

    // =========================================================
    // Circle stimulus in display-world coordinates
    // =========================================================

    [Header("Circle Stimulus [world]")]
    public float circleCenterXWorld = 0.0f;

    [Tooltip("ディスプレイ中心基準。上が+")]
    public float circleCenterYWorld = 0.0f;

    [Tooltip("円の半径。worldUnitToMmでmmに変換される")]
    public float circleRadiusWorld = 30.0f;

    // =========================================================
    // Flat virtual display offset
    // =========================================================

    [Header("Flat Virtual Display Offset [world]")]
    [Tooltip("displayNum=1 の平面条件で，仮想曲面ディスプレイ全体を右方向へ動かす量")]
    public float flatVirtualOffsetXWorld = 0.0f;

    [Tooltip("displayNum=1 の平面条件で，仮想曲面ディスプレイ全体を上方向へ動かす量")]
    public float flatVirtualOffsetYWorld = 0.0f;

    // =========================================================
    // Disparity search
    // =========================================================

    [Header("Disparity Search Range [px]")]
    private int positiveDispMinPx = 1;
    private int positiveDispMaxPx = 400;
    private int negativeDispMinPx = -400;
    private int negativeDispMaxPx = -1;

    // =========================================================
    // Random dot
    // =========================================================

    [Header("Random Dot")]
    [Range(0.0f, 1.0f)]
    public float pWhite = 0.5f;

    public float backgroundSeed = 1.0f;
    public float objectSeed = 1.0f;

    // =========================================================
    // Debug / Update
    // =========================================================

    [Header("Fade")]
    [Range(0.0f, 1.0f)]
    public float fadeLevel = 1.0f;

    [Header("Rest State")]
    public bool isResting = false;
    public Color restColor = Color.gray;

    [Header("Debug / Update")]
    public bool showCircleGuide = false;
    public bool debugSolidCircle = false;
    public bool autoUpdate = true;
    public bool autoSetQuadAspect = false;

    // =========================================================
    // Result
    // =========================================================

    [Header("Result")]
    [SerializeField] private int bestDisparityPx;
    [SerializeField] private float halfDisparityPx;
    [SerializeField] private float actualAngleDeg;
    [SerializeField] private float errorDeg;

    [SerializeField] private float leftShiftPx;
    [SerializeField] private float rightShiftPx;

    [SerializeField] private float circleCenterXPx;
    [SerializeField] private float circleCenterYPx;
    [SerializeField] private float circleRadiusPx;

    [SerializeField] private float viewerXmm;
    [SerializeField] private float viewerYmm;
    [SerializeField] private float viewerZmm;

    [SerializeField] private float circleCenterXmm;
    [SerializeField] private float circleCenterYmm;
    [SerializeField] private float circleRadiusMm;

    [SerializeField] private int activeWidthPx;
    [SerializeField] private int activeHeightPx;
    [SerializeField] private float activePp;

    [Header("Geometry Debug")]
    [SerializeField] private float fixationZmm;
    [SerializeField] private float viewerToFixationMm;
    [SerializeField] private float leftEyeToFixationMm;
    [SerializeField] private float rightEyeToFixationMm;

    [Header("Crop Debug")]
    [SerializeField] private bool cropEnabled;
    [SerializeField] private float cropHalfWidthMm;
    [SerializeField] private float cropHalfHeightMm;
    [SerializeField] private float curvedPhysicalWidthMm;
    [SerializeField] private float curvedPhysicalHeightMm;

    [Header("Flat Virtual Offset Debug")]
    [SerializeField] private float flatVirtualOffsetXmm;
    [SerializeField] private float flatVirtualOffsetYmm;
    [SerializeField] private float flatVirtualOffsetXPx;
    [SerializeField] private float flatVirtualOffsetYPx;

    private Renderer activeLeftQuadRenderer;
    private Renderer activeRightQuadRenderer;

    private Material leftMat;
    private Material rightMat;

    // =========================================================
    // Unity events
    // =========================================================

    void Start()
    {
        ApplyDisplayProfile();
        InitializeMaterials();
        UpdateRDS();
    }

    void Update()
    {
        if (autoUpdate)
        {
            UpdateRDS();
        }
    }

    void OnValidate()
    {
        ValidateParameters();
    }

    // =========================================================
    // Validation
    // =========================================================

    void ValidateParameters()
    {
        worldUnitToMm = Mathf.Max(0.000001f, worldUnitToMm);
        IPD = Mathf.Max(0.0f, IPD);
        circleRadiusWorld = Mathf.Max(0.0f, circleRadiusWorld);

        if (positiveDispMinPx > positiveDispMaxPx)
        {
            positiveDispMinPx = positiveDispMaxPx;
        }

        if (negativeDispMinPx > negativeDispMaxPx)
        {
            negativeDispMinPx = negativeDispMaxPx;
        }

        displayL = Mathf.Max(0, displayL);
        displayR = Mathf.Max(0, displayR);

        if (displayProfiles != null && displayProfiles.Length > 0)
        {
            displayNum = Mathf.Clamp(displayNum, 0, displayProfiles.Length - 1);

            for (int i = 0; i < displayProfiles.Length; i++)
            {
                if (displayProfiles[i] == null)
                {
                    continue;
                }

                displayProfiles[i].widthPx = Mathf.Max(1, displayProfiles[i].widthPx);
                displayProfiles[i].heightPx = Mathf.Max(1, displayProfiles[i].heightPx);
                displayProfiles[i].pp = Mathf.Max(0.000001f, displayProfiles[i].pp);
            }
        }
    }

    // =========================================================
    // Display Profile
    // =========================================================

    public void ApplyDisplayProfile()
    {
        ValidateParameters();

        displayNum = Mathf.Clamp(displayNum, 0, displayProfiles.Length - 1);

        DisplayProfile profile = displayProfiles[displayNum];



        activeWidthPx = Mathf.Max(1, profile.widthPx);
        activeHeightPx = Mathf.Max(1, profile.heightPx);
        activePp = Mathf.Max(0.000001f, profile.pp);

        activeLeftQuadRenderer = profile.leftQuadRenderer;
        activeRightQuadRenderer = profile.rightQuadRenderer;

        DisableAllProfileCameras();

        if (profile.leftCamera != null)
        {
            profile.leftCamera.targetDisplay = Mathf.Max(0, displayL);
            profile.leftCamera.enabled = true;
        }

        if (profile.rightCamera != null)
        {
            profile.rightCamera.targetDisplay = Mathf.Max(0, displayR);
            profile.rightCamera.enabled = true;
        }

        leftMat = null;
        rightMat = null;
        InitializeMaterials();
    }

    void DisableAllProfileCameras()
    {
        if (displayProfiles == null)
        {
            return;
        }

        for (int i = 0; i < displayProfiles.Length; i++)
        {
            DisplayProfile profile = displayProfiles[i];

            if (profile == null)
            {
                continue;
            }

            if (profile.leftCamera != null)
            {
                profile.leftCamera.enabled = false;
            }

            if (profile.rightCamera != null)
            {
                profile.rightCamera.enabled = false;
            }
        }
    }

    // =========================================================
    // Initialization
    // =========================================================

    void InitializeMaterials()
    {
        leftMat = activeLeftQuadRenderer.material;
        rightMat = activeRightQuadRenderer.material;

    }

    // =========================================================
    // Main update
    // =========================================================

    public void UpdateRDS()
    {
        ApplyDisplayProfile();

        if (leftMat == null || rightMat == null)
        {
            InitializeMaterials();
        }

        if (leftMat == null || rightMat == null)
        {
            return;
        }

        UpdateDerivedParameters();
        UpdateCropParameters();

        bestDisparityPx = SolveDisparityForTargetAngle();
        halfDisparityPx = bestDisparityPx / 2.0f;

        actualAngleDeg = DispPxToAngleDeg(bestDisparityPx);
        errorDeg = Mathf.Abs(actualAngleDeg - targetAngleDeg);

        leftShiftPx = halfDisparityPx;
        rightShiftPx = -halfDisparityPx;

        ApplyMaterialParams(leftMat);
        ApplyMaterialParams(rightMat);

        if (autoSetQuadAspect)
        {
            ApplyQuadAspect();
        }
    }

    // =========================================================
    // Derived parameters
    // =========================================================

    void UpdateDerivedParameters()
    {
        viewerXmm = viewerXWorld * worldUnitToMm;
        viewerYmm = viewerYWorld * worldUnitToMm;
        viewerZmm = viewerZWorld * worldUnitToMm;

        circleCenterXmm = circleCenterXWorld * worldUnitToMm;
        circleCenterYmm = circleCenterYWorld * worldUnitToMm;
        circleRadiusMm = circleRadiusWorld * worldUnitToMm;

        circleCenterXPx = (activeWidthPx / 2.0f) + (circleCenterXmm / activePp);
        circleCenterYPx = (activeHeightPx / 2.0f) - (circleCenterYmm / activePp);
        circleRadiusPx = circleRadiusMm / activePp;

        if (displayNum == 1)
        {
            flatVirtualOffsetXmm = flatVirtualOffsetXWorld * worldUnitToMm;
            flatVirtualOffsetYmm = flatVirtualOffsetYWorld * worldUnitToMm;
        }
        else
        {
            flatVirtualOffsetXmm = 0.0f;
            flatVirtualOffsetYmm = 0.0f;
        }

        flatVirtualOffsetXPx = flatVirtualOffsetXmm / activePp;
        flatVirtualOffsetYPx = -flatVirtualOffsetYmm / activePp;
    }

    // =========================================================
    // Crop
    // =========================================================

    void UpdateCropParameters()
    {
        cropEnabled = false;
        cropHalfWidthMm = 0.0f;
        cropHalfHeightMm = 0.0f;
        curvedPhysicalWidthMm = 0.0f;
        curvedPhysicalHeightMm = 0.0f;

        // displayNum = 1 の平面条件だけ，
        // displayNum = 0 の曲面ディスプレイの物理サイズに合わせて黒マスクする
        if (displayNum != 1)
        {
            return;
        }

        if (displayProfiles == null || displayProfiles.Length < 1 || displayProfiles[0] == null)
        {
            return;
        }

        DisplayProfile curvedProfile = displayProfiles[0];

        float curvedPp = Mathf.Max(0.000001f, curvedProfile.pp);
        int curvedWidthPx = Mathf.Max(1, curvedProfile.widthPx);
        int curvedHeightPx = Mathf.Max(1, curvedProfile.heightPx);

        curvedPhysicalWidthMm = curvedPp * curvedWidthPx;
        curvedPhysicalHeightMm = curvedPp * curvedHeightPx;

        cropHalfWidthMm = curvedPhysicalWidthMm * 0.5f;
        cropHalfHeightMm = curvedPhysicalHeightMm * 0.5f;

        cropEnabled = true;
    }

    // =========================================================
    // Material
    // =========================================================

    void ApplyMaterialParams(Material mat)
    {
        mat.SetFloat("_WidthPx", activeWidthPx);
        mat.SetFloat("_HeightPx", activeHeightPx);
        mat.SetFloat("_Pp", activePp);

        mat.SetFloat("_CircleCenterXPx", circleCenterXPx);
        mat.SetFloat("_CircleCenterYPx", circleCenterYPx);
        mat.SetFloat("_CircleRadiusPx", circleRadiusPx);

        mat.SetFloat("_HalfDisparityPx", halfDisparityPx);

        mat.SetFloat("_PWhite", pWhite);
        mat.SetFloat("_BackgroundSeed", backgroundSeed);
        mat.SetFloat("_ObjectSeed", objectSeed);

        mat.SetFloat("_ShowCircleGuide", showCircleGuide ? 1.0f : 0.0f);
        mat.SetFloat("_DebugSolidCircle", debugSolidCircle ? 1.0f : 0.0f);

        mat.SetFloat("_IsResting", isResting ? 1.0f : 0.0f);
        mat.SetColor("_RestColor", restColor);

        mat.SetFloat("_CropEnabled", cropEnabled ? 1.0f : 0.0f);
        mat.SetFloat("_CropHalfWidthMm", cropHalfWidthMm);
        mat.SetFloat("_CropHalfHeightMm", cropHalfHeightMm);

        mat.SetFloat("_VirtualOffsetXPx", flatVirtualOffsetXPx);
        mat.SetFloat("_VirtualOffsetYPx", flatVirtualOffsetYPx);

        mat.SetFloat("_FadeLevel", fadeLevel);
    }

    // =========================================================
    // Display surface geometry
    // =========================================================

    Vector3 GetDisplayPointMm(float xMm, float yMm)
    {
        // displayNum = 1:
        // 平面ディスプレイ．表示面を z = 0 とする．
        if (displayNum == 1)
        {
            return new Vector3(xMm, yMm, 0.0f);
        }

        float inside = Curvature * Curvature - xMm * xMm;

        if (inside < 0.0f)
        {
            inside = 0.0f;
        }

        float zMm = Curvature - Mathf.Sqrt(inside);

        return new Vector3(xMm, yMm, zMm);
    }

    // =========================================================
    // Disparity angle calculation
    // =========================================================

    float DispPxToAngleDeg(float dispPx)
    {
        float shiftMm = dispPx * activePp;

        float mx = circleCenterXmm + flatVirtualOffsetXmm;
        float my = circleCenterYmm + flatVirtualOffsetYmm;

        float lx = mx + shiftMm / 2.0f;
        float rx = mx - shiftMm / 2.0f;

        Vector3 leftEye = new Vector3(
            viewerXmm - IPD / 2.0f,
            viewerYmm,
            viewerZmm
        );

        Vector3 rightEye = new Vector3(
            viewerXmm + IPD / 2.0f,
            viewerYmm,
            viewerZmm
        );

        Vector3 leftPoint = GetDisplayPointMm(lx, my);
        Vector3 rightPoint = GetDisplayPointMm(rx, my);
        Vector3 midPoint = GetDisplayPointMm(mx, my);

        Vector3 l1 = leftPoint - leftEye;
        Vector3 r1 = rightPoint - rightEye;

        Vector3 l2 = midPoint - leftEye;
        Vector3 r2 = midPoint - rightEye;

        float alpha = Vector3.Angle(l1, r1);
        float beta = Vector3.Angle(l2, r2);

        UpdateGeometryDebug(leftEye, rightEye, midPoint);

        return alpha - beta;
    }

    void UpdateGeometryDebug(Vector3 leftEye, Vector3 rightEye, Vector3 fixationPoint)
    {
        Vector3 viewerCenter = new Vector3(
            viewerXmm,
            viewerYmm,
            viewerZmm
        );

        fixationZmm = fixationPoint.z;
        viewerToFixationMm = Vector3.Distance(viewerCenter, fixationPoint);
        leftEyeToFixationMm = Vector3.Distance(leftEye, fixationPoint);
        rightEyeToFixationMm = Vector3.Distance(rightEye, fixationPoint);
    }

    // =========================================================
    // Search
    // =========================================================

    int SolveDisparityForTargetAngle()
    {
        if (Mathf.Approximately(targetAngleDeg, 0.0f))
        {
            return 0;
        }

        int start;
        int end;

        if (targetAngleDeg > 0.0f)
        {
            start = positiveDispMinPx;
            end = positiveDispMaxPx;
        }
        else
        {
            start = negativeDispMinPx;
            end = negativeDispMaxPx;
        }

        int bestDisp = start;
        float bestTheta = DispPxToAngleDeg(start);
        float bestError = Mathf.Abs(bestTheta - targetAngleDeg);

        for (int d = start; d <= end; d++)
        {
            float theta = DispPxToAngleDeg(d);
            float err = Mathf.Abs(theta - targetAngleDeg);

            if (err < bestError)
            {
                bestDisp = d;
                bestTheta = theta;
                bestError = err;
            }
        }

        return bestDisp;
    }

    // =========================================================
    // Quad aspect
    // =========================================================

    void ApplyQuadAspect()
    {
        float aspect = (float)activeWidthPx / activeHeightPx;

        ApplyAspectToRenderer(activeLeftQuadRenderer, aspect);
        ApplyAspectToRenderer(activeRightQuadRenderer, aspect);
    }

    void ApplyAspectToRenderer(Renderer renderer, float aspect)
    {
        if (renderer == null)
        {
            return;
        }

        Transform t = renderer.transform;
        Vector3 s = t.localScale;

        t.localScale = new Vector3(s.y * aspect, s.y, s.z);
    }

    // =========================================================
    // Debug utility
    // =========================================================

    [ContextMenu("Apply Display Profile Now")]
    public void ApplyDisplayProfileNow()
    {
        ApplyDisplayProfile();
        UpdateRDS();
    }

    [ContextMenu("Update RDS Now")]
    public void UpdateRDSNow()
    {
        UpdateRDS();
    }
}