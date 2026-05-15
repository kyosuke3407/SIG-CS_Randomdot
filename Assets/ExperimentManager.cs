using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ExperimentManager : MonoBehaviour
{
    [Header("Experiment Mode")]
    public bool isExperimentMode = true;

    [Header("Experiment Settings")]
    public float radius = 30f;
    public float initialDisparityArcmin = 12.7f;
    public float initialStepArcmin = 6.38f;
    public float minStepArcmin = 0.798f;
    public int maxReversals = 10;

    [Header("Durations")]
    public float preTrialDuration = 0.5f;
    public float referenceDuration = 1.0f;
    public float isiDuration = 0.5f;
    public float testDuration = 1.0f;

    [Header("Conditions")]
    public Vector2 centerLocation = new Vector2(0, 0);
    public Vector2 peripheralLocation = new Vector2(100, 0); // Example peripheral X

    [Header("Rest State")]
    public Color restColor = Color.gray;

    [Header("UI & Fade")]
    public float fadeDuration = 0.2f;
    public Button startButton;
    public Button restartButton;
    public Button quitButton;
    public TextMeshProUGUI logText;

    [Header("Debug")]
    public bool debugSolidCircle = false;

    [Header("References")]
    public RDSController rdsController; // Assuming RDSController handles the display

    // Staircase Data Structure
    [Serializable]
    public class StaircaseSeries
    {
        public bool isCross; // true = 手前 (Cross), false = 奥 (Uncrossed)
        public float currentDisparityArcmin;
        public float currentStepArcmin;
        public int consecutiveCorrect;
        public int reversalCount;
        public bool isFinished;
        public int lastDirection; // 1 = harder (decreased disp), -1 = easier (increased disp), 0 = start

        public StaircaseSeries(bool isCross, float initialDispArcmin, float initialStepArcmin)
        {
            this.isCross = isCross;
            this.currentDisparityArcmin = initialDispArcmin;
            this.currentStepArcmin = initialStepArcmin;
            this.consecutiveCorrect = 0;
            this.reversalCount = 0;
            this.isFinished = false;
            this.lastDirection = 0;
        }

        public bool UpdateStep(bool isCorrect, float minStepArcmin)
        {
            bool isReversal = false;
            if (isCorrect)
            {
                consecutiveCorrect++;
                if (consecutiveCorrect >= 2)
                {
                    // 難化（視差を減らす）
                    int newDirection = 1;
                    if (lastDirection == -1) // 反転
                    {
                        isReversal = true;
                        reversalCount++;
                        currentStepArcmin = Mathf.Max(currentStepArcmin / 2f, minStepArcmin);
                    }
                    currentDisparityArcmin = Mathf.Max(minStepArcmin, currentDisparityArcmin - currentStepArcmin);
                    lastDirection = newDirection;
                    consecutiveCorrect = 0;
                }
            }
            else
            {
                // 易化（視差を増やす）
                consecutiveCorrect = 0;
                int newDirection = -1;
                if (lastDirection == 1) // 反転
                {
                    isReversal = true;
                    reversalCount++;
                    currentStepArcmin = Mathf.Max(currentStepArcmin / 2f, minStepArcmin);
                }
                currentDisparityArcmin += currentStepArcmin;
                lastDirection = newDirection;
            }
            return isReversal;
        }
    }

    private StaircaseSeries crossSeries;
    private StaircaseSeries uncrossedSeries;

    private int trialCount = 0;
    private Vector2 currentLocation;
    private string currentConditionName;
    private string csvFilePath;

    private bool isWaitingForResponse = false;
    private bool userResponseIsCross = false; // true = 手前と回答, false = 奥と回答

    private bool isStartPressed = false;
    private bool isRestartPressed = false;

    private float pixelArcmin;

    void Start()
    {
        if (logText != null) logText.text = "";

        LogMessage($"Init Disp: {initialDisparityArcmin} arcmin, Init Step: {initialStepArcmin} arcmin, Min Step: {minStepArcmin} arcmin");

        // Initialize CSV file
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string expFolderPath = Path.Combine(projectRoot, "EXP");

        if (!Directory.Exists(expFolderPath))
        {
            Directory.CreateDirectory(expFolderPath);
        }

        csvFilePath = Path.Combine(expFolderPath, $"ExperimentResult_{timestamp}.csv");
        File.AppendAllText(csvFilePath, "TrialID,Condition,Series,DisparityArcmin,IsTestSecond,IsCorrect,IsReversal,CurrentStepArcmin\n");

        if (startButton != null) startButton.onClick.AddListener(() => isStartPressed = true);
        if (restartButton != null) restartButton.onClick.AddListener(() => isRestartPressed = true);
        if (quitButton != null) quitButton.onClick.AddListener(QuitExperiment);

        if (restartButton != null) restartButton.gameObject.SetActive(false);

        // --- 初期モードに応じたUIと処理の振り分け ---
        if (!isExperimentMode)
        {
            // 実験モードOFF時はUIを隠す
            if (startButton != null) startButton.gameObject.SetActive(false);
            if (quitButton != null) quitButton.gameObject.SetActive(false);
            if (logText != null) logText.gameObject.SetActive(false);

            // 強制的に表示状態（真っ暗・グレーを解除）にする
            if (rdsController != null)
            {
                rdsController.fadeLevel = 1f;
                rdsController.isResting = false;
                rdsController.UpdateRDSNow();
            }
        }
        else
        {
            // 実験モードON時
            if (rdsController != null)
            {
                rdsController.fadeLevel = 0f;
                rdsController.debugSolidCircle = this.debugSolidCircle;
                rdsController.UpdateRDSNow();
            }
            StartCoroutine(ExperimentLoop());
        }
    }

    public void QuitExperiment()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    IEnumerator ExperimentLoop()
    {
        // Wait for start
        while (!isStartPressed)
        {
            var gamepad = UnityEngine.InputSystem.Gamepad.current;
            if (gamepad != null && gamepad.buttonEast.wasPressedThisFrame)
            {
                isStartPressed = true;
            }
            yield return null;
        }
        if (startButton != null) startButton.gameObject.SetActive(false);

        while (true)
        {
            yield return StartCoroutine(RunExperimentBlock());

            // 休憩モードへ移行
            if (rdsController != null)
            {
                rdsController.isResting = true;
                rdsController.restColor = this.restColor;
                rdsController.UpdateRDSNow();
            }

            if (restartButton != null) restartButton.gameObject.SetActive(true);
            isRestartPressed = false;

            while (!isRestartPressed)
            {
                var gamepad = UnityEngine.InputSystem.Gamepad.current;
                if (gamepad != null && gamepad.buttonEast.wasPressedThisFrame)
                {
                    isRestartPressed = true;
                }
                yield return null;
            }

            if (restartButton != null) restartButton.gameObject.SetActive(false);

            // 休憩モード解除、真っ暗な状態に戻す
            if (rdsController != null)
            {
                rdsController.isResting = false;
                rdsController.fadeLevel = 0f;
                rdsController.UpdateRDSNow();
            }
        }
    }

    void Update()
    {
        // デバッグ円のインスペクターでの変更検知は常に実行する
        if (rdsController != null && rdsController.debugSolidCircle != this.debugSolidCircle)
        {
            rdsController.debugSolidCircle = this.debugSolidCircle;
            rdsController.UpdateRDSNow();
        }

        // 実験モードOFFなら、以下の実験用キー入力は無視する
        if (!isExperimentMode) return;

        if (isWaitingForResponse)
        {
            bool responded = false;

            // キーボード入力
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.upArrowKey.wasPressedThisFrame)
                {
                    userResponseIsCross = false; // 奥
                    responded = true;
                }
                else if (keyboard.downArrowKey.wasPressedThisFrame)
                {
                    userResponseIsCross = true; // 手前
                    responded = true;
                }
            }

            // ゲームパッド入力
            var gamepad = UnityEngine.InputSystem.Gamepad.current;
            if (gamepad != null && !responded)
            {
                // D-padの上、または上ボタン（Y / △）
                if (gamepad.dpad.up.wasPressedThisFrame || gamepad.buttonNorth.wasPressedThisFrame)
                {
                    userResponseIsCross = false; // 奥
                    responded = true;
                }
                // D-padの下、または下ボタン（A / ✕）
                else if (gamepad.dpad.down.wasPressedThisFrame || gamepad.buttonSouth.wasPressedThisFrame)
                {
                    userResponseIsCross = true; // 手前
                    responded = true;
                }
            }

            if (responded)
            {
                isWaitingForResponse = false;
            }
        }
    }

    IEnumerator RunExperimentBlock()
    {
        // ランダムに条件（中心か周辺か）を決定
        if (UnityEngine.Random.value > 0.5f)
        {
            currentLocation = centerLocation;
            currentConditionName = "Center";
        }
        else
        {
            currentLocation = peripheralLocation;
            currentConditionName = "Peripheral";
        }

        LogMessage($"Block Started. Condition: {currentConditionName}");

        crossSeries = new StaircaseSeries(true, initialDisparityArcmin, initialStepArcmin);
        uncrossedSeries = new StaircaseSeries(false, initialDisparityArcmin, initialStepArcmin);

        while (!crossSeries.isFinished || !uncrossedSeries.isFinished)
        {
            // どちらの系列を提示するかランダムに選ぶ
            List<StaircaseSeries> activeSeries = new List<StaircaseSeries>();
            if (!crossSeries.isFinished) activeSeries.Add(crossSeries);
            if (!uncrossedSeries.isFinished) activeSeries.Add(uncrossedSeries);

            StaircaseSeries currentSeries = activeSeries[UnityEngine.Random.Range(0, activeSeries.Count)];

            trialCount++;
            yield return StartCoroutine(RunTrial(currentSeries));

            if (currentSeries.reversalCount >= maxReversals)
            {
                currentSeries.isFinished = true;
                LogMessage($"Series Finished: {(currentSeries.isCross ? "Cross" : "Uncrossed")}");
            }
        }

        LogMessage("Experiment Block Finished!");
    }

    IEnumerator RunTrial(StaircaseSeries series)
    {
        // テスト刺激の視差を分度(arcmin)と度(deg)で取得・計算
        float currentDisparityArcmin = series.currentDisparityArcmin;
        float currentDisparityDeg = currentDisparityArcmin / 60f;

        if (!series.isCross)
        {
            currentDisparityArcmin = -currentDisparityArcmin;
            currentDisparityDeg = -currentDisparityDeg; // 奥の場合は負の値と仮定
        }

        // ランダムに提示順を決定 (true: Reference -> Test, false: Test -> Reference)
        bool isTestSecond = (UnityEngine.Random.value > 0.5f);
        float firstDisp = isTestSecond ? 0.0f : currentDisparityDeg;
        float secondDisp = isTestSecond ? currentDisparityDeg : 0.0f;

        // デバッグ用に現在の正解方向を画面に表示
        string seriesName = series.isCross ? "Cross (手前)" : "Uncross (奥)";
        string orderName = isTestSecond ? "Ref -> Test" : "Test -> Ref";
        
        // 2回目に提示された刺激が1回目に対して手前か奥かの正解
        bool correctAnsIsCross = isTestSecond ? series.isCross : !series.isCross;
        string correctAnsStr = correctAnsIsCross ? "Cross (手前)" : "Uncross (奥)";

        LogMessage($"--- Trial {trialCount} [{currentConditionName}] ---");
        LogMessage($"[Target] {seriesName} | Disp: {Mathf.Abs(currentDisparityArcmin):F2} arcmin");
        LogMessage($"[Order] {orderName} | 2nd is: {correctAnsStr}");

        // 0. Pre-Trial State (真っ暗なまま待機)
        yield return new WaitForSeconds(preTrialDuration);

        // 1. First Interval
        ShowRDS(currentLocation, radius, firstDisp);
        yield return StartCoroutine(FadeIn());
        yield return new WaitForSeconds(referenceDuration); // 1回目の提示時間

        // 2. ISI State
        yield return StartCoroutine(FadeOut());
        HideRDS(); // 刺激を非表示に
        // ★ここでのFadeInを削除し、真っ暗なまま待機する
        yield return new WaitForSeconds(isiDuration);

        // 3. Second Interval
        ShowRDS(currentLocation, radius, secondDisp);
        yield return StartCoroutine(FadeIn());
        yield return new WaitForSeconds(testDuration); // 2回目の提示時間

        // 4. Response State
        yield return StartCoroutine(FadeOut());
        HideRDS();
        // ★応答待ちの間も真っ暗なままにする

        isWaitingForResponse = true;
        while (isWaitingForResponse)
        {
            yield return null;
        }

        // 判定
        bool isCorrect = (userResponseIsCross == correctAnsIsCross);
        bool isReversal = series.UpdateStep(isCorrect, minStepArcmin);

        // ログ出力
        float currentStepArcmin = series.currentStepArcmin;
        string logLine = $"{trialCount},{currentConditionName},{(series.isCross ? "Cross" : "Uncross")},{currentDisparityArcmin},{isTestSecond},{isCorrect},{isReversal},{currentStepArcmin}\n";
        File.AppendAllText(csvFilePath, logLine);

        string userAnsStr = userResponseIsCross ? "Cross" : "Uncross";
        string correctStr = isCorrect ? "Correct" : "Incorrect";

        string trialLog = $"[Result] Answer: {userAnsStr} -> {correctStr}";
        if (isReversal)
        {
            trialLog += $" *** REVERSAL! ({series.reversalCount}/{maxReversals}) ***";
        }

        LogMessage(trialLog);
    }

    IEnumerator FadeOut()
    {
        if (rdsController == null) yield break;
        float elapsed = 0;
        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            rdsController.fadeLevel = Mathf.Lerp(1, 0, elapsed / fadeDuration); // 黒へ
            rdsController.UpdateRDSNow();
            yield return null;
        }
        rdsController.fadeLevel = 0;
        rdsController.UpdateRDSNow();
    }

    IEnumerator FadeIn()
    {
        if (rdsController == null) yield break;
        float elapsed = 0;
        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            rdsController.fadeLevel = Mathf.Lerp(0, 1, elapsed / fadeDuration); // 表示
            rdsController.UpdateRDSNow();
            yield return null;
        }
        rdsController.fadeLevel = 1;
        rdsController.UpdateRDSNow();
    }

    // --- ユーザー提供の既存関数（想定） ---
    // もしすでに別の場所にある場合は、この関数から該当クラスのメソッドを呼び出してください。
    public void ShowRDS(Vector2 location, float radius, float disparityDegree)
    {
        if (rdsController != null)
        {
            rdsController.circleCenterXWorld = location.x;
            rdsController.circleCenterYWorld = location.y;
            rdsController.circleRadiusWorld = radius;
            rdsController.targetAngleDeg = disparityDegree;

            // ランダムドットのパターンを毎回変えるためにシード値を更新
            rdsController.backgroundSeed = UnityEngine.Random.Range(1f, 10000f);
            rdsController.objectSeed = UnityEngine.Random.Range(1f, 10000f);

            // 視差0のときは表示するがターゲット視差を0にする
            rdsController.UpdateRDSNow();
        }
        else
        {
            LogMessage($"[Mock] ShowRDS: loc={location}, radius={radius}, disp={disparityDegree}");
        }
    }

    public void HideRDS()
    {
        if (rdsController != null)
        {
            // 刺激を隠す処理（例：円の半径を0にするなど）
            rdsController.circleRadiusWorld = 0f;
            rdsController.UpdateRDSNow();
        }
        else
        {
            LogMessage("[Mock] HideRDS");
        }
    }

    private Queue<string> logQueue = new Queue<string>();
    private int maxLogLines = 8; // ログを見やすくするため行数を増やしました

    private void LogMessage(string msg)
    {
        Debug.Log(msg);
        if (logText != null)
        {
            logQueue.Enqueue(msg);
            if (logQueue.Count > maxLogLines)
            {
                logQueue.Dequeue();
            }
            logText.text = string.Join("\n", logQueue);
        }
    }

    // --- 追加：ボタンから呼び出すためのトグル用メソッド ---

    public void ToggleExperimentMode()
    {
        isExperimentMode = !isExperimentMode;

        if (!isExperimentMode)
        {
            // 実験モードをOFFにする
            StopAllCoroutines();

            if (startButton != null) startButton.gameObject.SetActive(false);
            if (restartButton != null) restartButton.gameObject.SetActive(false);
            if (quitButton != null) quitButton.gameObject.SetActive(false);
            if (logText != null) logText.gameObject.SetActive(false);

            // 強制的に表示状態（真っ暗・グレーを解除）にする
            if (rdsController != null)
            {
                rdsController.fadeLevel = 1f;
                rdsController.isResting = false;
                rdsController.UpdateRDSNow();
            }
        }
        else
        {
            // 実験モードをONにして、最初からやり直す
            if (startButton != null) startButton.gameObject.SetActive(true);
            if (restartButton != null) restartButton.gameObject.SetActive(false);
            if (quitButton != null) quitButton.gameObject.SetActive(true);
            if (logText != null)
            {
                logText.gameObject.SetActive(true);
                logText.text = "";
                logQueue.Clear();
            }

            isStartPressed = false;
            isRestartPressed = false;
            trialCount = 0;

            if (rdsController != null)
            {
                rdsController.fadeLevel = 0f;
                rdsController.isResting = false;
                rdsController.UpdateRDSNow();
            }

            StartCoroutine(ExperimentLoop());
        }
    }

    public void ToggleDebugSolidCircle()
    {
        debugSolidCircle = !debugSolidCircle;
        if (rdsController != null)
        {
            rdsController.debugSolidCircle = this.debugSolidCircle;
            rdsController.UpdateRDSNow();
        }
    }

    // --- 追加：サブピクセルシェーダーの切り替え用メソッド ---
    public void ToggleSubpixelShader()
    {
        if (rdsController != null)
        {
            rdsController.useSubpixelShader = !rdsController.useSubpixelShader;
            
            // マテリアルの再初期化を伴う更新を行う
            rdsController.UpdateRDSNow();
            
            LogMessage($"Subpixel Shader: {(rdsController.useSubpixelShader ? "ON" : "OFF")}");
        }
    }
}
