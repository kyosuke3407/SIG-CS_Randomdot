using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Globalization;

public class UIController : MonoBehaviour
{
    [Header("Target Controller")]
    public RDSController controller;

    // =========================================================
    // Target Theta
    // =========================================================

    [Header("Target Theta [deg]")]
    public Slider targetThetaSlider;
    public TMP_InputField targetThetaInput;
    public float targetThetaMin = -1.0f;
    public float targetThetaMax = 1.0f;

    // =========================================================
    // Circle Radius
    // =========================================================

    [Header("Circle Radius [world]")]
    public Slider radiusSlider;
    public TMP_InputField radiusInput;
    public float radiusMin = 1.0f;
    public float radiusMax = 500.0f;

    // =========================================================
    // Circle Center X
    // =========================================================

    [Header("Circle Center X [world]")]
    public Slider centerXSlider;
    public TMP_InputField centerXInput;
    public float centerXMin = -1000.0f;
    public float centerXMax = 1000.0f;

    // =========================================================
    // Circle Center Y
    // =========================================================

    [Header("Circle Center Y [world]")]
    public Slider centerYSlider;
    public TMP_InputField centerYInput;
    public float centerYMin = -500.0f;
    public float centerYMax = 500.0f;

    // =========================================================
    // Common
    // =========================================================

    [Header("Format")]
    public string numberFormat = "0.###";

    private bool isUpdatingUI = false;

    void Start()
    {
        SetupSliders();
        RegisterEvents();
        SyncUIFromController();
    }

    void OnEnable()
    {
        RegisterEvents();
    }

    void OnDisable()
    {
        UnregisterEvents();
    }

    // =========================================================
    // Setup
    // =========================================================

    void SetupSliders()
    {
        if (targetThetaSlider != null)
        {
            targetThetaSlider.minValue = targetThetaMin;
            targetThetaSlider.maxValue = targetThetaMax;
        }

        if (radiusSlider != null)
        {
            radiusSlider.minValue = radiusMin;
            radiusSlider.maxValue = radiusMax;
        }

        if (centerXSlider != null)
        {
            centerXSlider.minValue = centerXMin;
            centerXSlider.maxValue = centerXMax;
        }

        if (centerYSlider != null)
        {
            centerYSlider.minValue = centerYMin;
            centerYSlider.maxValue = centerYMax;
        }
    }

    void RegisterEvents()
    {
        if (targetThetaSlider != null)
        {
            targetThetaSlider.onValueChanged.RemoveListener(OnTargetThetaSliderChanged);
            targetThetaSlider.onValueChanged.AddListener(OnTargetThetaSliderChanged);
        }

        if (radiusSlider != null)
        {
            radiusSlider.onValueChanged.RemoveListener(OnRadiusSliderChanged);
            radiusSlider.onValueChanged.AddListener(OnRadiusSliderChanged);
        }

        if (centerXSlider != null)
        {
            centerXSlider.onValueChanged.RemoveListener(OnCenterXSliderChanged);
            centerXSlider.onValueChanged.AddListener(OnCenterXSliderChanged);
        }

        if (centerYSlider != null)
        {
            centerYSlider.onValueChanged.RemoveListener(OnCenterYSliderChanged);
            centerYSlider.onValueChanged.AddListener(OnCenterYSliderChanged);
        }

        if (targetThetaInput != null)
        {
            targetThetaInput.onEndEdit.RemoveListener(OnTargetThetaInputEndEdit);
            targetThetaInput.onEndEdit.AddListener(OnTargetThetaInputEndEdit);
        }

        if (radiusInput != null)
        {
            radiusInput.onEndEdit.RemoveListener(OnRadiusInputEndEdit);
            radiusInput.onEndEdit.AddListener(OnRadiusInputEndEdit);
        }

        if (centerXInput != null)
        {
            centerXInput.onEndEdit.RemoveListener(OnCenterXInputEndEdit);
            centerXInput.onEndEdit.AddListener(OnCenterXInputEndEdit);
        }

        if (centerYInput != null)
        {
            centerYInput.onEndEdit.RemoveListener(OnCenterYInputEndEdit);
            centerYInput.onEndEdit.AddListener(OnCenterYInputEndEdit);
        }
    }

    void UnregisterEvents()
    {
        if (targetThetaSlider != null)
        {
            targetThetaSlider.onValueChanged.RemoveListener(OnTargetThetaSliderChanged);
        }

        if (radiusSlider != null)
        {
            radiusSlider.onValueChanged.RemoveListener(OnRadiusSliderChanged);
        }

        if (centerXSlider != null)
        {
            centerXSlider.onValueChanged.RemoveListener(OnCenterXSliderChanged);
        }

        if (centerYSlider != null)
        {
            centerYSlider.onValueChanged.RemoveListener(OnCenterYSliderChanged);
        }

        if (targetThetaInput != null)
        {
            targetThetaInput.onEndEdit.RemoveListener(OnTargetThetaInputEndEdit);
        }

        if (radiusInput != null)
        {
            radiusInput.onEndEdit.RemoveListener(OnRadiusInputEndEdit);
        }

        if (centerXInput != null)
        {
            centerXInput.onEndEdit.RemoveListener(OnCenterXInputEndEdit);
        }

        if (centerYInput != null)
        {
            centerYInput.onEndEdit.RemoveListener(OnCenterYInputEndEdit);
        }
    }

    // =========================================================
    // Slider Events
    // =========================================================

    void OnTargetThetaSliderChanged(float value)
    {
        SetTargetTheta(value, updateSlider: false);
    }

    void OnRadiusSliderChanged(float value)
    {
        SetRadius(value, updateSlider: false);
    }

    void OnCenterXSliderChanged(float value)
    {
        SetCenterX(value, updateSlider: false);
    }

    void OnCenterYSliderChanged(float value)
    {
        SetCenterY(value, updateSlider: false);
    }

    // =========================================================
    // InputField Events
    // =========================================================

    void OnTargetThetaInputEndEdit(string text)
    {
        float value = ParseOrCurrent(text, controller.targetAngleDeg);
        SetTargetTheta(value, updateSlider: true);
    }

    void OnRadiusInputEndEdit(string text)
    {
        float value = ParseOrCurrent(text, controller.circleRadiusWorld);
        SetRadius(value, updateSlider: true);
    }

    void OnCenterXInputEndEdit(string text)
    {
        float value = ParseOrCurrent(text, controller.circleCenterXWorld);
        SetCenterX(value, updateSlider: true);
    }

    void OnCenterYInputEndEdit(string text)
    {
        float value = ParseOrCurrent(text, controller.circleCenterYWorld);
        SetCenterY(value, updateSlider: true);
    }

    // =========================================================
    // Setters
    // =========================================================

    void SetTargetTheta(float value, bool updateSlider)
    {
        if (controller == null || isUpdatingUI)
        {
            return;
        }

        value = Mathf.Clamp(value, targetThetaMin, targetThetaMax);
        controller.targetAngleDeg = value;

        UpdateTargetThetaUI(value, updateSlider);
        ApplyControllerUpdate();
    }

    void SetRadius(float value, bool updateSlider)
    {
        if (controller == null || isUpdatingUI)
        {
            return;
        }

        value = Mathf.Clamp(value, radiusMin, radiusMax);
        controller.circleRadiusWorld = value;

        UpdateRadiusUI(value, updateSlider);
        ApplyControllerUpdate();
    }

    void SetCenterX(float value, bool updateSlider)
    {
        if (controller == null || isUpdatingUI)
        {
            return;
        }

        value = Mathf.Clamp(value, centerXMin, centerXMax);
        controller.circleCenterXWorld = value;

        UpdateCenterXUI(value, updateSlider);
        ApplyControllerUpdate();
    }

    void SetCenterY(float value, bool updateSlider)
    {
        if (controller == null || isUpdatingUI)
        {
            return;
        }

        value = Mathf.Clamp(value, centerYMin, centerYMax);
        controller.circleCenterYWorld = value;

        UpdateCenterYUI(value, updateSlider);
        ApplyControllerUpdate();
    }

    // =========================================================
    // UI Update
    // =========================================================

    public void SyncUIFromController()
    {
        if (controller == null)
        {
            Debug.LogError("RDSController が未設定です。");
            return;
        }

        SetupSliders();

        isUpdatingUI = true;

        float theta = Mathf.Clamp(controller.targetAngleDeg, targetThetaMin, targetThetaMax);
        float radius = Mathf.Clamp(controller.circleRadiusWorld, radiusMin, radiusMax);
        float cx = Mathf.Clamp(controller.circleCenterXWorld, centerXMin, centerXMax);
        float cy = Mathf.Clamp(controller.circleCenterYWorld, centerYMin, centerYMax);

        controller.targetAngleDeg = theta;
        controller.circleRadiusWorld = radius;
        controller.circleCenterXWorld = cx;
        controller.circleCenterYWorld = cy;

        if (targetThetaSlider != null) targetThetaSlider.value = theta;
        if (radiusSlider != null) radiusSlider.value = radius;
        if (centerXSlider != null) centerXSlider.value = cx;
        if (centerYSlider != null) centerYSlider.value = cy;

        if (targetThetaInput != null) targetThetaInput.text = Format(theta);
        if (radiusInput != null) radiusInput.text = Format(radius);
        if (centerXInput != null) centerXInput.text = Format(cx);
        if (centerYInput != null) centerYInput.text = Format(cy);

        isUpdatingUI = false;

        ApplyControllerUpdate();
    }

    void UpdateTargetThetaUI(float value, bool updateSlider)
    {
        isUpdatingUI = true;

        if (updateSlider && targetThetaSlider != null)
        {
            targetThetaSlider.value = value;
        }

        if (targetThetaInput != null)
        {
            targetThetaInput.text = Format(value);
        }

        isUpdatingUI = false;
    }

    void UpdateRadiusUI(float value, bool updateSlider)
    {
        isUpdatingUI = true;

        if (updateSlider && radiusSlider != null)
        {
            radiusSlider.value = value;
        }

        if (radiusInput != null)
        {
            radiusInput.text = Format(value);
        }

        isUpdatingUI = false;
    }

    void UpdateCenterXUI(float value, bool updateSlider)
    {
        isUpdatingUI = true;

        if (updateSlider && centerXSlider != null)
        {
            centerXSlider.value = value;
        }

        if (centerXInput != null)
        {
            centerXInput.text = Format(value);
        }

        isUpdatingUI = false;
    }

    void UpdateCenterYUI(float value, bool updateSlider)
    {
        isUpdatingUI = true;

        if (updateSlider && centerYSlider != null)
        {
            centerYSlider.value = value;
        }

        if (centerYInput != null)
        {
            centerYInput.text = Format(value);
        }

        isUpdatingUI = false;
    }

    // =========================================================
    // Utility
    // =========================================================

    float ParseOrCurrent(string text, float current)
    {
        if (float.TryParse(
                text,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out float value
            ))
        {
            return value;
        }

        // 日本語環境などで小数点カンマが使われた場合の保険
        if (float.TryParse(
                text,
                NumberStyles.Float,
                CultureInfo.CurrentCulture,
                out value
            ))
        {
            return value;
        }

        return current;
    }

    string Format(float value)
    {
        return value.ToString(numberFormat, CultureInfo.InvariantCulture);
    }

    void ApplyControllerUpdate()
    {
        if (controller == null)
        {
            return;
        }

        controller.UpdateRDS();
    }

    [ContextMenu("Sync UI From Controller")]
    public void SyncUIFromControllerByMenu()
    {
        SyncUIFromController();
    }
}