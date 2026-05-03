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

    public int displayL = 1;
    public int displayR = 2;

    public DisplayProfile[] displayProfiles;

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

    [Tooltip("ディスプレイ中心基準．上が+")]
    public float circleCenterYWorld = 0.0f;

    [Tooltip("円の半径．worldUnitToMmでmmに変換される")]
    public float circleRadiusWorld = 30.0f;

    // =========================================================
    // Disparity search
    // =========================================================

    [Header("Disparity Search Range [px]")]
    public int positiveDispMinPx = 1;
    public int positiveDispMaxPx = 200;
    public int negativeDispMinPx = -200;
    public int negativeDispMaxPx = -1;

    // =========================================================
    // Random dot
    // =========================================================

    [Header("Random Dot")]
    [Range(0.0f, 1.0f)]
    public float pWhite = 0.5f;

    public float backgroundSeed = 0.0f;
    public float objectSeed = 100.0f;

    // =========================================================
    // Debug / Update
    // =========================================================

    [Header("Debug / Update")]
    public bool showCircleGuide = false;
    public bool autoUpdate = true;
    public bool autoSetQuadAspect = false;

    // =========================================================
    // Result
    // =========================================================

    [Header("Result")]
    [SerializeField] private int bestDisparityPx;
    private float halfDisparityPx;
    private float actualAngleDeg;
    private float errorDeg;

    [SerializeField] private float leftShiftPx;
    [SerializeField] private float rightShiftPx;

    [SerializeField] private float circleCenterXPx;
    [SerializeField] private float circleCenterYPx;
    [SerializeField] private float circleRadiusPx;

    private float viewerXmm;
    private float viewerYmm;
    private float viewerZmm;

    private float circleCenterXmm;
    private float circleCenterYmm;
    private float circleRadiusMm;

    private int activeWidthPx;
    private int activeHeightPx;
    private float activePp;


    private float fixationZmm;
    private float viewerToFixationMm;
    private float leftEyeToFixationMm;
    private float rightEyeToFixationMm;

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

        if (displayProfiles == null || displayProfiles.Length == 0)
        {
            Debug.LogError("Display Profiles が設定されていません。");
            return;
        }

        displayNum = Mathf.Clamp(displayNum, 0, displayProfiles.Length - 1);

        DisplayProfile profile = displayProfiles[displayNum];

        if (profile == null)
        {
            Debug.LogError($"DisplayProfile {displayNum} が null です。");
            return;
        }

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
        if (activeLeftQuadRenderer == null || activeRightQuadRenderer == null)
        {
            Debug.LogError("Profile内の LeftQuadRenderer または RightQuadRenderer が未設定です。");
            return;
        }

        leftMat = activeLeftQuadRenderer.material;
        rightMat = activeRightQuadRenderer.material;

        if (activeLeftQuadRenderer.sharedMaterial == activeRightQuadRenderer.sharedMaterial)
        {
            Debug.LogWarning(
                "LeftQuad と RightQuad が同じMaterialアセットを参照しています。" +
                "RDS_L.mat と RDS_R.mat を別々に用意して割り当ててください。"
            );
        }

        if (leftMat == null || rightMat == null)
        {
            Debug.LogError("左右いずれかのMaterialがnullです。");
            return;
        }

        if (leftMat.shader == null || leftMat.shader.name != "Unlit/RDS")
        {
            Debug.LogWarning($"Left material shader is {leftMat.shader?.name}. Unlit/RDS を指定してください。");
        }

        if (rightMat.shader == null || rightMat.shader.name != "Unlit/RDS")
        {
            Debug.LogWarning($"Right material shader is {rightMat.shader?.name}. Unlit/RDS を指定してください。");
        }
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
    }

    // =========================================================
    // Material
    // =========================================================

    void ApplyMaterialParams(Material mat)
    {
        mat.SetFloat("_WidthPx", activeWidthPx);
        mat.SetFloat("_HeightPx", activeHeightPx);

        mat.SetFloat("_CircleCenterXPx", circleCenterXPx);
        mat.SetFloat("_CircleCenterYPx", circleCenterYPx);
        mat.SetFloat("_CircleRadiusPx", circleRadiusPx);

        mat.SetFloat("_HalfDisparityPx", halfDisparityPx);

        mat.SetFloat("_PWhite", pWhite);
        mat.SetFloat("_BackgroundSeed", backgroundSeed);
        mat.SetFloat("_ObjectSeed", objectSeed);

        mat.SetFloat("_ShowCircleGuide", showCircleGuide ? 1.0f : 0.0f);
    }

    // =========================================================
    // Display surface geometry
    // =========================================================

    Vector3 GetDisplayPointMm(float xMm, float yMm)
    {
        // displayNum = 1:
        // 平面ディスプレイ．表示面は z = 0 とする．
        // 円中心が横に動くと，視聴者から円中心までの距離は自然に長くなる．
        if (displayNum == 1)
        {
            return new Vector3(xMm, yMm, 0.0f);
        }

        // displayNum = 0:
        // 曲面ディスプレイ．
        // 水平方向について，視聴者から表示点までの距離が一定になるようにzを補正する．
        //
        // 視聴者位置: (viewerXmm, viewerYmm, viewerZmm)
        // ディスプレイ中心: (0, 0, 0)
        //
        // x-z平面で，
        // (x - viewerX)^2 + (z - viewerZ)^2 = R^2
        // R = ディスプレイ中心までの距離
        //
        // 中心 x=0 のとき z=0 になる方の解を使う．

        float radiusXZ = Mathf.Sqrt(
            viewerXmm * viewerXmm +
            viewerZmm * viewerZmm
        );

        float dx = xMm - viewerXmm;

        float inside = radiusXZ * radiusXZ - dx * dx;

        if (inside < 0.0f)
        {
            inside = 0.0f;
        }

        float zMm = viewerZmm - Mathf.Sqrt(inside);

        return new Vector3(xMm, yMm, zMm);
    }

    // =========================================================
    // Disparity angle calculation
    // =========================================================

    float DispPxToAngleDeg(float dispPx)
    {
        float shiftMm = dispPx * activePp;

        // 融合後の見かけ中心 = 円中心
        float mx = circleCenterXmm;
        float my = circleCenterYmm;

        // 左右画像上の対応点
        // 正のdisp:
        //   left  = +disp/2
        //   right = -disp/2
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

        // ここで displayNum に応じて，
        // 曲面なら z を補正，平面なら z=0 として扱う．
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

        if (start > end)
        {
            Debug.LogError("視差探索範囲が不正です。min <= max になるように設定してください。");
            return 0;
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