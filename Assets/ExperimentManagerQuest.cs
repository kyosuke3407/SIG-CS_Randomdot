using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ExperimentManagerQuest : MonoBehaviour
{
    [Header("Experiment Mode")]
    public bool isExperimentMode = true;

    [Header("Experiment Settings")]
    public float radius = 30f;
    [Tooltip("QUEST法での1条件あたりの試行回数")]
    public int maxQuestTrials = 40;
    [Tooltip("閾値の事前分布の平均 (arcmin)")]
    public float questPriorMean = 5.0f;
    [Tooltip("閾値の事前分布の標準偏差 (log scale)")]
    public float questPriorSD = 1.0f;
    public bool isPresentationOrderRandom = true;

    public enum TargetDirectionMode { Both, CrossedOnly, UncrossedOnly }
    [Header("Trial Control")]
    public TargetDirectionMode directionMode = TargetDirectionMode.Both;
    [Tooltip("全地点（中心・周辺）を何回繰り返すか")]
    public int totalBlocks = 1;
    [Tooltip("ブロック開始時に円の位置をガイド表示する時間 (秒)")]
    public float debugViewDuration = 2.0f;

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

    // Quest Data Structure
    [Serializable]
    public class QuestSeries
    {
        public bool isCross; // true = 手前 (Cross), false = 奥 (Uncrossed)
        public float currentDisparityArcmin;
        public int trialCount;
        public bool isFinished;

        private int gridPoints = 200;
        private float minLogT = -1.0f; // log10(0.1)
        private float maxLogT = 1.778f;  // log10(100)

        private float[] tGrid;
        private double[] pdf;

        // Weibull parameters
        private double gamma = 0.5;
        private double epsilon = 0.01;
        private double beta = 3.5;

        private int maxTrials;

        public QuestSeries(bool isCross, int maxTrials, float priorMeanArcmin, float priorSDArcmin)
        {
            this.isCross = isCross;
            this.maxTrials = maxTrials;
            this.trialCount = 0;
            this.isFinished = false;

            tGrid = new float[gridPoints];
            pdf = new double[gridPoints];

            double priorMeanLog = Mathf.Log10(priorMeanArcmin);
            double priorSDLog = priorSDArcmin;

            double sum = 0;
            for (int i = 0; i < gridPoints; i++)
            {
                tGrid[i] = minLogT + (maxLogT - minLogT) * i / (gridPoints - 1);
                double val = Math.Exp(-0.5 * Math.Pow((tGrid[i] - priorMeanLog) / priorSDLog, 2));
                pdf[i] = val;
                sum += val;
            }

            for (int i = 0; i < gridPoints; i++) pdf[i] /= sum;

            UpdateTestIntensity();
        }

        public void UpdateStep(bool isCorrect)
        {
            trialCount++;
            double x = Mathf.Log10(currentDisparityArcmin);
            double sum = 0;

            for (int i = 0; i < gridPoints; i++)
            {
                double t = tGrid[i];
                // Weibull psychometric function for 2AFC
                double pCorrect = gamma + (1.0 - gamma - epsilon) * (1.0 - Math.Exp(-Math.Pow(10.0, beta * (x - t))));

                double likelihood = isCorrect ? pCorrect : (1.0 - pCorrect);
                pdf[i] = pdf[i] * likelihood;
                sum += pdf[i];
            }

            for (int i = 0; i < gridPoints; i++) pdf[i] /= sum;

            if (trialCount >= maxTrials)
            {
                isFinished = true;
            }
            else
            {
                UpdateTestIntensity();
            }
        }

        private void UpdateTestIntensity()
        {
            int maxIndex = 0;
            double maxVal = -1;
            for (int i = 0; i < gridPoints; i++)
            {
                if (pdf[i] > maxVal)
                {
                    maxVal = pdf[i];
                    maxIndex = i;
                }
            }

            float bestLogT = tGrid[maxIndex];
            currentDisparityArcmin = Mathf.Pow(10.0f, bestLogT);
        }
    }

    private QuestSeries crossSeries;
    private QuestSeries uncrossedSeries;

    private List<Vector2> locationList = new List<Vector2>();
    private List<string> locationNames = new List<string>();

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

        LogMessage($"QUEST Mode: Mean={questPriorMean} arcmin, SD={questPriorSD}, Trials={maxQuestTrials}");

        // Initialize CSV file
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string expFolderPath = Path.Combine(projectRoot, "EXP");

        if (!Directory.Exists(expFolderPath))
        {
            Directory.CreateDirectory(expFolderPath);
        }

        csvFilePath = Path.Combine(expFolderPath, $"ExperimentResult_{timestamp}.csv");
        File.AppendAllText(csvFilePath, "TrialID,Condition,Series,DisparityArcmin,IsTestSecond,IsCorrect,QuestTrial\n");

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
            if (CheckStartInput()) isStartPressed = true;
            yield return null;
        }
        if (startButton != null) startButton.gameObject.SetActive(false);

        // 条件リストの作成
        locationList.Clear();
        locationNames.Clear();
        locationList.Add(centerLocation); locationNames.Add("Center");
        locationList.Add(peripheralLocation); locationNames.Add("Peripheral");

        int totalConditions = totalBlocks * locationList.Count;
        int conditionsFinished = 0;

        for (int l = 0; l < totalBlocks; l++)
        {
            LogMessage($"--- Starting Global Block {l + 1} / {totalBlocks} ---");
            // 地点の順番をシャッフル
            List<int> indices = new List<int> { 0, 1 };
            for (int i = 0; i < indices.Count; i++)
            {
                int temp = indices[i];
                int randomIndex = UnityEngine.Random.Range(i, indices.Count);
                indices[i] = indices[randomIndex];
                indices[randomIndex] = temp;
            }

            foreach (int idx in indices)
            {
                currentLocation = locationList[idx];
                currentConditionName = locationNames[idx];
                yield return StartCoroutine(RunExperimentBlock());

                conditionsFinished++;
                if (conditionsFinished < totalConditions)
                {
                    yield return StartCoroutine(ShowRestBreak());
                }
            }
        }

        LogMessage("All Experiments Finished!");

        // 休憩モード（終了状態）へ
        if (rdsController != null)
        {
            rdsController.isResting = true;
            rdsController.restColor = Color.black;
            rdsController.UpdateRDSNow();
        }

        if (restartButton != null) restartButton.gameObject.SetActive(true);
    }

    IEnumerator ShowRestBreak()
    {
        LogMessage("Break Time. Press Enter, Space or Gamepad O (East) to resume next condition.");

        // 休憩モード（レスト画面）へ移行
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
            // 新Input System (キーボード：Enter または Space)
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null && (keyboard.enterKey.wasPressedThisFrame || keyboard.spaceKey.wasPressedThisFrame))
            {
                isRestartPressed = true;
            }

            // 新Input System (ゲームパッド：Eastボタン)
            var gamepad = UnityEngine.InputSystem.Gamepad.current;
            if (gamepad != null && gamepad.buttonEast.wasPressedThisFrame)
            {
                isRestartPressed = true;
            }
            yield return null;
        }

        if (restartButton != null) restartButton.gameObject.SetActive(false);

        // 休憩モード解除、真っ暗な状態（刺激表示前）に戻す
        if (rdsController != null)
        {
            rdsController.isResting = false;
            rdsController.fadeLevel = 0f;
            rdsController.UpdateRDSNow();
        }

        LogMessage("Resuming experiment...");
    }

    bool CheckStartInput()
    {
        var keyboard = UnityEngine.InputSystem.Keyboard.current;
        if (keyboard != null && (keyboard.enterKey.wasPressedThisFrame || keyboard.spaceKey.wasPressedThisFrame)) return true;

        var gamepad = UnityEngine.InputSystem.Gamepad.current;
        if (gamepad != null && gamepad.buttonEast.wasPressedThisFrame) return true;
        return false;
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

            // 新Input System (キーボード)
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

            // 新Input System (ゲームパッド)
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
        LogMessage($"Block Started. Condition: {currentConditionName}");

        // --- ブロック開始前の位置ガイド表示 ---
        if (debugViewDuration > 0)
        {
            LogMessage("Showing location guide...");
            bool originalDebug = this.debugSolidCircle;
            this.debugSolidCircle = true; // 強制的に白円表示

            // パラメータ設定
            ShowRDS(currentLocation, radius, 0f);
            rdsController.fadeLevel = 0f;
            rdsController.UpdateRDSNow();

            // ガイドもフェードインさせる
            yield return StartCoroutine(FadeIn());
            yield return new WaitForSeconds(debugViewDuration);
            yield return StartCoroutine(FadeOut());

            this.debugSolidCircle = originalDebug; // 元に戻す
            HideRDS();
            yield return new WaitForSeconds(0.5f); // 開始前の余白
        }

        crossSeries = (directionMode == TargetDirectionMode.UncrossedOnly) ? null : new QuestSeries(true, maxQuestTrials, questPriorMean, questPriorSD);
        uncrossedSeries = (directionMode == TargetDirectionMode.CrossedOnly) ? null : new QuestSeries(false, maxQuestTrials, questPriorMean, questPriorSD);

        while ((crossSeries != null && !crossSeries.isFinished) || (uncrossedSeries != null && !uncrossedSeries.isFinished))
        {
            // どちらの系列を提示するかランダムに選ぶ
            List<QuestSeries> activeSeries = new List<QuestSeries>();
            if (crossSeries != null && !crossSeries.isFinished) activeSeries.Add(crossSeries);
            if (uncrossedSeries != null && !uncrossedSeries.isFinished) activeSeries.Add(uncrossedSeries);

            QuestSeries currentSeries = activeSeries[UnityEngine.Random.Range(0, activeSeries.Count)];

            trialCount++;
            yield return StartCoroutine(RunTrial(currentSeries));

            if (currentSeries.isFinished)
            {
                LogMessage($"Series Finished: {(currentSeries.isCross ? "Cross" : "Uncrossed")}");
            }
        }

        LogMessage("Experiment Block Finished!");
    }

    IEnumerator RunTrial(QuestSeries series)
    {
        // テスト刺激の視差を分度(arcmin)と度(deg)で取得・計算
        float currentDisparityArcmin = series.currentDisparityArcmin;
        float currentDisparityDeg = currentDisparityArcmin / 60f;

        if (!series.isCross)
        {
            currentDisparityArcmin = -currentDisparityArcmin;
            currentDisparityDeg = -currentDisparityDeg; // 奥の場合は負の値と仮定
        }

        // 提示順の決定
        bool isTestSecond;
        if (isPresentationOrderRandom)
        {
            isTestSecond = (UnityEngine.Random.value > 0.5f);
        }
        else
        {
            isTestSecond = true; // 常に 標準(0) -> テスト(視差あり) の順
        }
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
        rdsController.fadeLevel = 0f;
        rdsController.UpdateRDSNow(); // まず透明な状態でパラメータを反映
        yield return StartCoroutine(FadeIn());
        yield return new WaitForSeconds(referenceDuration); // 1回目の提示時間

        // 2. ISI State
        yield return StartCoroutine(FadeOut());
        HideRDS(); // 刺激を非表示に
        // ★ここでのFadeInを削除し、真っ暗なまま待機する
        yield return new WaitForSeconds(isiDuration);

        // 3. Second Interval
        ShowRDS(currentLocation, radius, secondDisp);
        rdsController.fadeLevel = 0f;
        rdsController.UpdateRDSNow(); // まず透明な状態でパラメータを反映
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
        series.UpdateStep(isCorrect);

        // ログ出力
        string logLine = $"{trialCount},{currentConditionName},{(series.isCross ? "Cross" : "Uncross")},{currentDisparityArcmin},{isTestSecond},{isCorrect},{series.trialCount}\n";
        File.AppendAllText(csvFilePath, logLine);

        string userAnsStr = userResponseIsCross ? "Cross" : "Uncross";
        string correctStr = isCorrect ? "Correct" : "Incorrect";

        string trialLog = $"[Result] Answer: {userAnsStr} -> {correctStr}";
        trialLog += $" *** QUEST Trial ({series.trialCount}/{maxQuestTrials}) ***";

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

    // --- 追加：グレースケールランダムドットの切り替え用メソッド ---
    public void ToggleGrayscaleDots()
    {
        if (rdsController != null)
        {
            rdsController.useGrayscaleDots = !rdsController.useGrayscaleDots;
            rdsController.UpdateRDSNow();
            LogMessage($"Grayscale Dots: {(rdsController.useGrayscaleDots ? "ON" : "OFF")}");
        }
    }
}
