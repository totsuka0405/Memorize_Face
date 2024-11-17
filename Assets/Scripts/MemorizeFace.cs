using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Cysharp.Threading.Tasks;
using System.Linq;

namespace MemorizeFace
{
    public class MemorizeFace : MonoBehaviour
    {
        [SerializeField]
        private FieldClass fieldClass;

        private UIManager uiManager;
        private SoundManager soundManager;
        private GameLogicManager gameLogicManager;
        private ScoreCalculator scoreCalculator;

        private float gameDuration; // ゲームの合計時間
        public float timeLeft;     // 残り時間
        public Action OnGameEnd; // ゲーム終了時の通知
        public Action OnGameStop; // ゲーム中断時の通知

        

        private void Awake()
        {
            Init();
        }
        private void Start()
        {
            fieldClass.StartPanel.SetActive(true);
            fieldClass.ScorePanel.SetActive(false);
            fieldClass.StartButton.onClick.AddListener(OnClickStart);
            fieldClass.ScoreButton.onClick.AddListener(OnClickEnd);
            fieldClass.QuitGameButton.onClick.AddListener(OnClickQuitGame);
            
        }

        void OnClickStart()
        {
            fieldClass.StartPanel.SetActive(false);
            StartGame(8).Forget();
        }

        void OnClickEnd()
        {
            fieldClass.ScorePanel.SetActive(false);
            fieldClass.StartPanel.SetActive(true);
        }

        void OnClickQuitGame()
        {
            // エディタ上では停止しないため、エディタ用の処理を追加
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false; // エディタモードを停止
#else
    Application.Quit(); // 実行中のアプリケーションを終了
#endif
        }

        private void Init()
        {
            uiManager = new UIManager(fieldClass);
            soundManager = new SoundManager(fieldClass.AudioSource, fieldClass);
            gameLogicManager = new GameLogicManager(fieldClass, uiManager, soundManager);
        }

        public async UniTaskVoid StartGame(int totalQuestions)
        {
            timeLeft = 0;
            fieldClass.isGameActive = true;

            gameLogicManager.InitializeGame(gameDuration, totalQuestions);
            gameLogicManager.StartGame();
            // タイマー開始
            await StartGameTimer();
        }

        public void EndGame()
        {
            gameLogicManager.EndGame();
            fieldClass.isGameActive = false;
            fieldClass.ScorePanel.SetActive(true);
            OnGameEnd?.Invoke();
        }

        public void StopGame()
        {
            fieldClass.isGameActive = false;
            gameLogicManager.EndGame();

            // ゲーム中断を通知
            OnGameStop?.Invoke();
        }

        private async UniTask StartGameTimer()
        {
            while (fieldClass.isGameActive && timeLeft >= 0)
            {
                timeLeft += Time.deltaTime;
                uiManager.UpdateTimerText(timeLeft);
                await UniTask.Yield();
            }
        }
    }

    public class GameLogicManager
    {
        private FieldClass fieldClass;
        private UIManager uiManager;
        private SoundManager soundManager;
        private FaceManager faceManager;
        ScoreCalculator scoreCalculator;


        private int currentScore;
        private int totalQuestions;
        private int correctAnswers;
        private float currentTime;
        private const int faceSize = 362;
        private const int spacing = 37; 
        private List<Sprite> correctFaces;

        public GameLogicManager(FieldClass fieldClass, UIManager uiManager, SoundManager soundManager)
        {
            this.fieldClass = fieldClass;
            this.uiManager = uiManager;
            this.soundManager = soundManager;
            faceManager = new FaceManager(fieldClass.FaceSprites);
            scoreCalculator = new ScoreCalculator(totalQuestions);
            correctFaces = new List<Sprite>();
        }

        public void InitializeGame(float gameDuration, int totalQuestions)
        {
            currentScore = 0;
            correctAnswers = 0;
            this.totalQuestions = totalQuestions;

            uiManager.UpdateScoreText(currentScore, totalQuestions);
            uiManager.DisplayRememberButton(true);
            SetupRememberButton();
        }

        public void StartGame()
        {
            currentTime = Time.time;
            GenerateQuestion();
        }

        public void EndGame()
        {
            float startTime = currentTime;
            currentTime = Time.time;
            scoreCalculator.CalculateScore((currentTime - startTime) / 8);
            uiManager.UpdateResult("点数 : " + scoreCalculator.GetScore());
            fieldClass.isGameActive = false;
            fieldClass.ScorePanel.SetActive(true);

        }

        private void SetupRememberButton()
        {
            if (fieldClass.RememberButton != null)
            {
                fieldClass.RememberButton.SetActive(true);
                fieldClass.RememberButton.GetComponent<Button>().onClick.AddListener(() =>
                {
                    soundManager.PlayClickSound();
                    ShowChoices();
                });

                var eventTrigger = fieldClass.RememberButton.gameObject.AddComponent<EventTrigger>();
                AddPointerEvents(eventTrigger);
            }
        }

        private void AddPointerEvents(EventTrigger eventTrigger)
        {
            var downEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
            downEntry.callback.AddListener((eventData) => uiManager.ChangeButtonTextColor(new Color32(128, 128, 128, 255)));
            eventTrigger.triggers.Add(downEntry);

            var upEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerUp };
            upEntry.callback.AddListener((eventData) => uiManager.ChangeButtonTextColor(new Color32(255, 255, 255, 255)));
            eventTrigger.triggers.Add(upEntry);
        }

        private void GenerateQuestion()
        {
            uiManager.UpdateScoreText(currentScore, totalQuestions);
            ClearGameArea();
            uiManager.DisplayRememberButton(true);

            if (correctAnswers < totalQuestions)
            {
                correctFaces.Clear();
                int faceToRememberCount = 1 + (correctAnswers / 2);
                correctFaces.AddRange(faceManager.GetCorrectFaces(faceToRememberCount));
                DisplayFaces(correctFaces).Forget();
                uiManager.UpdateExplainText("顔を覚えよう！");

                uiManager.OnRememberButtonClick(() =>
                {
                    soundManager.PlayClickSound();
                    ShowChoices();
                });
            }
            else
            {
                EndGame();
            }
        }

        private async UniTaskVoid DisplayFaces(List<Sprite> faces)
        {
            int faceCount = faces.Count;
            List<CanvasGroup> canvasGroups = new List<CanvasGroup>();

            for (int i = 0; i < faceCount; i++)
            {
                GameObject newFaceObject = new GameObject("Face_" + i);
                newFaceObject.transform.SetParent(fieldClass.GameArea.transform, false);
                Image faceImage = newFaceObject.AddComponent<Image>();
                faceImage.sprite = faces[i];
                RectTransform rectTransform = newFaceObject.GetComponent<RectTransform>();
                rectTransform.sizeDelta = new Vector2(faceSize, faceSize);
                int row = i / 3;
                int column = i % 3;
                rectTransform.anchoredPosition = new Vector2(
                    GetXPosition(faceCount, column),
                    -row * (faceSize + spacing)
                );

                CanvasGroup canvasGroup = newFaceObject.AddComponent<CanvasGroup>();
                canvasGroup.alpha = 0;
                canvasGroups.Add(canvasGroup);
            }

            await FadeInAll(canvasGroups, 0.25f);
        }

        private float GetXPosition(int faceCount, int column)
        {
            if (faceCount == 1) return 0;
            if (faceCount == 2) return (column - 0.5f) * (faceSize + spacing);
            return (column - 1) * (faceSize + spacing);
        }

        private void ShowChoices()
        {
            uiManager.DisplayRememberButton(false);
            uiManager.UpdateExplainText("覚えた顔を選んで！");
            ClearGameArea();
            var choiceFaces = faceManager.GenerateChoiceFaces(correctFaces, 9);
            CreateChoiceButtons(choiceFaces);
        }

        private void CreateChoiceButtons(List<Sprite> choiceFaces)
        {
            for (int i = 0; i < choiceFaces.Count; i++)
            {
                Sprite face = choiceFaces[i];
                GameObject choiceButtonObject = new GameObject($"ChoiceButton_{i}");
                choiceButtonObject.transform.SetParent(fieldClass.GameArea.transform, false);
                Image faceImage = choiceButtonObject.AddComponent<Image>();
                faceImage.sprite = face;
                Button button = choiceButtonObject.AddComponent<Button>();
                RectTransform rectTransform = choiceButtonObject.GetComponent<RectTransform>();
                rectTransform.sizeDelta = new Vector2(faceSize, faceSize);
                int row = i / 3;
                int column = i % 3;
                float xPos = (column - 1) * (faceSize + spacing);
                float yPos = (1 - row) * (faceSize + spacing);
                rectTransform.anchoredPosition = new Vector2(xPos, yPos);

                if (correctFaces.Contains(face))
                {
                    button.onClick.AddListener(() => CorrectAnswer(choiceButtonObject));
                }
                else
                {
                    button.onClick.AddListener(WrongAnswer);
                }
            }
        }

        private void ClearGameArea()
        {
            foreach (Transform child in fieldClass.GameArea.transform)
            {
                UnityEngine.Object.Destroy(child.gameObject);
            }
        }

        private void CorrectAnswer(GameObject clickedObject)
        {
            var clickedFace = clickedObject.GetComponent<Image>().sprite;
            correctFaces.Remove(clickedFace);
            soundManager.PlayCorrectSound();
            UnityEngine.Object.Destroy(clickedObject);

            if (correctFaces.Count == 0)
            {
                correctAnswers++;
                currentScore++;
                WaitBeforeNextQuestion(1f).Forget();
            }
        }

        private void WrongAnswer()
        {
            GameObject clickedObject = UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject;
            Image image = clickedObject.GetComponent<Image>();

            if (image != null)
            {
                image.color = new Color32(128, 128, 128, 255); // 不正解時の色
                clickedObject.GetComponent<Button>().enabled = false; // ボタンを無効化して選択不可にする
            }
            else
            {
                Debug.LogWarning("Image component not found on clicked object.");
            }
            soundManager.PlayIncorrectSound();
            scoreCalculator.AddIncorrectAnswer();
        }

        private async UniTask WaitBeforeNextQuestion(float waitTime)
        {
            await UniTask.Delay((int)(waitTime * 1000));
            GenerateQuestion();
        }

        /// <summary>
        /// 複数のCanvasGroupを同時にフェードイン
        /// </summary>
        private async UniTask FadeInAll(List<CanvasGroup> canvasGroups, float duration)
        {
            if (canvasGroups == null || canvasGroups.Count == 0)
            {
                Debug.LogWarning("FadeInAll aborted: canvasGroups is null or empty.");
                return;
            }
            var validCanvasGroups = canvasGroups.Where(cg => cg != null).ToList();

            if (validCanvasGroups.Count == 0)
            {
                Debug.LogWarning("FadeInAll aborted: all canvasGroups are null.");
                return;
            }

            float elapsedTime = 0f;

            while (elapsedTime < duration)
            {
                elapsedTime += Time.deltaTime;
                float alpha = Mathf.Clamp01(elapsedTime / duration);

                foreach (var canvasGroup in validCanvasGroups)
                {
                    canvasGroup.alpha = alpha;
                }

                await UniTask.Yield();
            }

            foreach (var canvasGroup in validCanvasGroups)
            {
                canvasGroup.alpha = 1f;
            }
        }
    }

    public class FaceManager
    {
        private List<Sprite> faceSprites;

        public FaceManager(List<Sprite> faceSprites)
        {
            this.faceSprites = faceSprites;
        }

        public List<Sprite> GetCorrectFaces(int faceCount)
        {
            List<Sprite> correctFaces = new List<Sprite>();
            List<Sprite> availableFaces = new List<Sprite>(faceSprites);

            for (int i = 0; i < faceCount; i++)
            {
                Sprite face = GetUniqueFace(availableFaces);
                correctFaces.Add(face);
                availableFaces.Remove(face);
            }

            return correctFaces;
        }

        public List<Sprite> GenerateChoiceFaces(List<Sprite> correctFaces, int totalChoices)
        {
            List<Sprite> choiceFaces = new List<Sprite>(correctFaces);
            List<Sprite> availableFaces = new List<Sprite>(faceSprites);
            availableFaces.RemoveAll(face => correctFaces.Contains(face));

            int remainingChoices = totalChoices - choiceFaces.Count;
            for (int i = 0; i < remainingChoices; i++)
            {
                if (availableFaces.Count > 0) // 追加: 利用可能な顔があるか確認
                {
                    Sprite randomFace = GetUniqueFace(availableFaces);
                    choiceFaces.Add(randomFace);
                    availableFaces.Remove(randomFace); // 追加: 重複を避けるため、選んだ顔を削除
                }
            }
            ShuffleList(choiceFaces);
            return choiceFaces;
        }

        private Sprite GetUniqueFace(List<Sprite> availableFaces = null)
        {
            if (availableFaces == null)
            {
                availableFaces = new List<Sprite>(faceSprites); // 使用可能な顔写真のリストを初期化
            }

            int index = UnityEngine.Random.Range(0, availableFaces.Count);
            Sprite face = availableFaces[index];

            availableFaces.RemoveAt(index); // 使用した顔写真をリストから削除

            return face;
        }

        private void ShuffleList(List<Sprite> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                Sprite temp = list[i];
                list[i] = list[j];
                list[j] = temp;
            }
        }
    }

    public class ScoreCalculator
    {
        private const float MaxScore = 1000.0f;
        private const float MinScore = 0.0f;
        private const float IncorrectPenalty = 100.0f;
        private const float MaxTimeForFullScore = 3.0f;
        private const int ScoreReductionPerSecond = 80;

        private float score;
        private int totalQuestions;
        private int incorrectCount;

        public ScoreCalculator(int totalQuestions)
        {
            this.totalQuestions = totalQuestions;
            score = MaxScore;
            incorrectCount = 0;
        }

        public void CalculateScore(float averageTime)
        {
            if (averageTime <= MaxTimeForFullScore)
            {
                score = MaxScore;
            }
            else
            {
                score = MaxScore - (int)((averageTime - MaxTimeForFullScore) * ScoreReductionPerSecond);
                score = Math.Max(MinScore, score);
            }

            ApplyIncorrectPenalty();
            // スコアを整数に丸める
            score = (float)Math.Round(score);
        }

        private void ApplyIncorrectPenalty()
        {
            float penalty = incorrectCount * IncorrectPenalty;
            score = Math.Max(MinScore, score - penalty);
        }

        public void AddIncorrectAnswer()
        {
            incorrectCount++;
        }

        public float GetScore()
        {
            return score;
        }
    }

    public class UIManager
    {
        private FieldClass fieldClass;

        public UIManager(FieldClass fieldClass)
        {
            this.fieldClass = fieldClass;
        }

        public void UpdateScoreText(int currentScore, int totalQuestions)
        {
            fieldClass.ScoreText.text = $"{currentScore} / {totalQuestions}";
        }

        public void UpdateTimerText(float timeLeft)
        {
            TimeSpan timeSpan = TimeSpan.FromSeconds(timeLeft);
            fieldClass.TimerText.text = string.Format("{0:D2} : {1:D2}", timeSpan.Minutes, timeSpan.Seconds);
        }

        public void UpdateExplainText(string message)
        {
            fieldClass.ExplainText.text = message;
        }
        public void UpdateResult(string message)
        {
            fieldClass.ResultText.text = message;
        }

        public void DisplayRememberButton(bool show)
        {
            fieldClass.RememberButton.SetActive(show);
        }

        public void OnRememberButtonClick(Action callback)
        {
            Button button = fieldClass.RememberButton.GetComponent<Button>();
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => callback());
        }

        public void ChangeButtonTextColor(Color32 color)
        {
            // ボタンの子オブジェクトからTextコンポーネントを取得
            Text buttonText = fieldClass.RememberButton.GetComponentInChildren<Text>();

            if (buttonText != null)
            {
                // テキストの色を変更
                buttonText.color = color;
            }
            else
            {
                Debug.LogWarning("Text component not found in children!");
            }
        }
    }

    public class SoundManager
    {
        private AudioSource audioSource;
        private FieldClass fieldClass;

        public SoundManager(AudioSource audioSource, FieldClass fieldClass)
        {
            this.audioSource = audioSource;
            this.fieldClass = fieldClass;
        }

        public void PlayCorrectSound()
        {
            audioSource.PlayOneShot(fieldClass.CorrectSound);
        }

        public void PlayIncorrectSound()
        {
            audioSource.PlayOneShot(fieldClass.IncorrectSound);
        }

        public void PlayClickSound()
        {
            audioSource.PlayOneShot(fieldClass.ClickSound);
        }
    }

    [System.Serializable]
    public class FieldClass
    {
        [SerializeField]
        private GameObject gameArea;
        [SerializeField]
        private GameObject rememberButton;
        [SerializeField]
        private Text timerText;
        [SerializeField]
        private Text scoreText;
        [SerializeField]
        private Text explainText;
        [SerializeField]
        private AudioClip correctSound;
        [SerializeField]
        private AudioClip incorrectSound;
        [SerializeField]
        private AudioClip clickSound;
        [SerializeField]
        private Text resultText;

        [SerializeField]
        private AudioSource audioSource; // AudioSource フィールドを追加
        [SerializeField]
        private List<Sprite> faceSprites; // 顔写真のリストを追加

        [SerializeField]
        GameObject startPanel;
        [SerializeField]
        GameObject scorePanel;
        [SerializeField]
        Button startButton;
        [SerializeField]
        Button scoreButton;
        [SerializeField]
        Button quitGameButton;

        // ゲッターは public を使わず、フィールドを直接アクセス
        public GameObject GameArea => gameArea;
        public GameObject RememberButton => rememberButton;
        public Text TimerText => timerText;
        public Text ScoreText => scoreText;
        public Text ExplainText => explainText;
        public Text ResultText => resultText;
        public AudioClip CorrectSound => correctSound;
        public AudioClip IncorrectSound => incorrectSound;
        public AudioClip ClickSound => clickSound;
        public List<Sprite> FaceSprites => faceSprites;
        public AudioSource AudioSource => audioSource;
        public bool isGameActive;  // ゲームがアクティブかどうか
        public GameObject StartPanel => startPanel;
        public GameObject ScorePanel => scorePanel;
        public Button StartButton => startButton;
        public Button ScoreButton => scoreButton;
        public Button QuitGameButton => quitGameButton;
    }
}
