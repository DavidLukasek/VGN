using Firebase;
using Firebase.Auth;
using Firebase.Database;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;
using UnityEngine.UI;

public class GuestbookManager : MonoBehaviour
{
    [Header("UI - References")]
    public TMP_InputField inputField;
    public Button submitButton;
    public Button prevButton;
    public Button nextButton;
    public TMP_Text pageInfoText;
    public TMP_Text characterCounterText;

    [Header("Page Areas (Left / Right)")]
    public Transform leftPageArea;
    public Transform rightPageArea;
    public GameObject notePrefab;

    [Header("Paging")]
    public int itemsPerSide = 5;
    public float submitCooldown = 5f;

    [Header("Text layout / sizing")]
    public float fixedFontSize = 20f;
    public int maxWordLengthBeforeBreak = 20;

    public int inputCharacterLimit = 300;
    public int maxInputLines = 7;
    public int maxCharsPerLine = 50;

    int currentDoublePage = 0;
    List<NoteData> notes = new List<NoteData>();

    DatabaseReference dbRef;
    FirebaseAuth auth;
    string userId;

    float lastSubmitTime = -999f;

    bool isProcessingInput = false;

    void Start()
    {
        FirebaseApp.LogLevel = Firebase.LogLevel.Debug;

        var options = new AppOptions
        {
            ProjectId = "vgn-by-ddi",
            AppId = "1:825982060371:desktop:unity",
            ApiKey = "AIzaSyCPGZp4SWbpXnDAFSmCUiADtER8xt64IFQ",
            DatabaseUrl = new Uri("https://vgn-by-ddi-default-rtdb.europe-west1.firebasedatabase.app")
        };

        FirebaseApp app = FirebaseApp.Create(options);
        dbRef = FirebaseDatabase.GetInstance(app).RootReference;

        var query = dbRef
            .Child("guestbook")
            .OrderByChild("timestamp")
            .LimitToLast(2000);

        query.ChildAdded += OnChildAdded;
        query.ChildChanged += OnChildChanged;
        query.ChildRemoved += OnChildRemoved;

        if (submitButton) submitButton.onClick.AddListener(SubmitButtonClicked);
        if (prevButton) prevButton.onClick.AddListener(PrevDoublePage);
        if (nextButton) nextButton.onClick.AddListener(NextDoublePage);

        if (inputField != null)
        {
            inputField.characterLimit = inputCharacterLimit;
            inputField.lineType = TMP_InputField.LineType.MultiLineNewline;
            if (inputField.textComponent != null)
            {
                inputField.textComponent.textWrappingMode = TextWrappingModes.Normal;
                inputField.textComponent.overflowMode = TextOverflowModes.Overflow;
            }

            inputField.onValueChanged.AddListener(OnInputValueChanged);
            UpdateCharacterCounter();
        }
    }

    void OnDestroy()
    {
        if (dbRef != null)
        {
            dbRef.ChildAdded -= OnChildAdded;
            dbRef.ChildChanged -= OnChildChanged;
            dbRef.ChildRemoved -= OnChildRemoved;
        }

        if (inputField != null)
        {
            inputField.onValueChanged.RemoveListener(OnInputValueChanged);
        }
    }

    void OnChildAdded(object sender, ChildChangedEventArgs e)
    {
        if (e.DatabaseError != null) { Debug.LogError(e.DatabaseError.Message); return; }
        var n = SnapshotToNote(e.Snapshot);
        if (n == null) return;

        if (notes.Any(x => x.id == n.id)) return;

        notes.Add(n);
        notes = notes.OrderBy(x => x.timestamp).ToList();

        int lastDoublePage = Mathf.Max(0, (notes.Count - 1) / (itemsPerSide * 2));
        currentDoublePage = lastDoublePage;

        RefreshDoublePageUI();
    }

    void OnChildChanged(object sender, ChildChangedEventArgs e)
    {
        if (e.DatabaseError != null) { Debug.LogError(e.DatabaseError.Message); return; }
        var n = SnapshotToNote(e.Snapshot);
        if (n == null) return;
        int idx = notes.FindIndex(x => x.id == n.id);
        if (idx >= 0) { notes[idx] = n; notes = notes.OrderBy(x => x.timestamp).ToList(); RefreshDoublePageUI(); }
    }

    void OnChildRemoved(object sender, ChildChangedEventArgs e)
    {
        if (e.DatabaseError != null) { Debug.LogError(e.DatabaseError.Message); return; }
        string id = e.Snapshot.Key;
        int idx = notes.FindIndex(x => x.id == id);
        if (idx >= 0) { notes.RemoveAt(idx); RefreshDoublePageUI(); }
    }

    NoteData SnapshotToNote(DataSnapshot snap)
    {
        try
        {
            var n = new NoteData();
            n.id = snap.Key;
            n.userId = snap.Child("userId").Value != null ? snap.Child("userId").Value.ToString() : "anon";
            n.text = snap.Child("text").Value != null ? snap.Child("text").Value.ToString() : "";
            object tsObj = snap.Child("timestamp").Value;
            long ts = 0;
            if (tsObj is long) ts = (long)tsObj;
            else if (tsObj is double) ts = Convert.ToInt64((double)tsObj);
            else if (tsObj != null) ts = Convert.ToInt64(tsObj);
            n.timestamp = ts;
            return n;
        }
        catch (Exception ex)
        {
            Debug.LogWarning("Parse note failed: " + ex);
            return null;
        }
    }

    void RefreshDoublePageUI()
    {
        if (leftPageArea) { foreach (Transform t in leftPageArea) Destroy(t.gameObject); }
        if (rightPageArea) { foreach (Transform t in rightPageArea) Destroy(t.gameObject); }

        if (notes.Count == 0)
        {
            if (pageInfoText) pageInfoText.text = "Žádné zápisky";
            UpdatePagingButtons();
            return;
        }

        int totalDoublePages = Mathf.Max(1, Mathf.CeilToInt((float)notes.Count / (itemsPerSide * 2)));
        currentDoublePage = Mathf.Clamp(currentDoublePage, 0, totalDoublePages - 1);

        int startIndex = currentDoublePage * itemsPerSide * 2;
        int leftStart = startIndex;
        int leftEnd = Mathf.Min(notes.Count, leftStart + itemsPerSide);
        int rightStart = leftEnd;
        int rightEnd = Mathf.Min(notes.Count, rightStart + itemsPerSide);

        CreateSideSlots(leftPageArea, leftStart, itemsPerSide);
        CreateSideSlots(rightPageArea, rightStart, itemsPerSide);

        if (pageInfoText) pageInfoText.text = $"Strana {currentDoublePage + 1} / {totalDoublePages}";
        UpdatePagingButtons();
    }

    void CreateSideSlots(Transform parent, int startIndexForThisSide, int slotsCount)
    {
        if (parent == null || notePrefab == null) return;

        RectTransform parentRect = parent.GetComponent<RectTransform>();
        float parentHeight = parentRect != null ? parentRect.rect.height : 0f;

        for (int i = 0; i < slotsCount; i++)
        {
            int globalIndex = startIndexForThisSide + i;
            GameObject go = Instantiate(notePrefab, parent);
            var tmp = go.GetComponentInChildren<TextMeshProUGUI>();
            if (tmp != null)
            {
                tmp.enableAutoSizing = false;
                tmp.fontSize = fixedFontSize;
                tmp.textWrappingMode = TextWrappingModes.Normal;
                tmp.overflowMode = TextOverflowModes.Overflow;
                tmp.richText = true;
            }

            var le = go.GetComponent<LayoutElement>();
            if (le == null) le = go.AddComponent<LayoutElement>();

            if (parentHeight > 1f)
            {
                le.preferredHeight = parentHeight / slotsCount;
                le.flexibleHeight = 0f;
                le.minHeight = 0f;
            }
            else
            {
                le.preferredHeight = -1f;
                le.flexibleHeight = 1f;
                le.minHeight = 0f;
            }

            if (globalIndex < notes.Count)
            {
                var n = notes[globalIndex];
                string date = n.timestamp > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(n.timestamp).ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss") : "—";
                string safeText = InsertWordBreaks(n.text ?? "", maxWordLengthBeforeBreak);
                if (tmp != null) tmp.text = $"<size=80%><b><color=#000>[{date}]</color></b></size>\n<color=#333>{safeText}</color>";
            }
            else
            {
                if (tmp != null) tmp.text = "";
            }
        }

        Canvas.ForceUpdateCanvases();
        var layoutGroup = parent.GetComponent<VerticalLayoutGroup>();
        if (layoutGroup != null) LayoutRebuilder.ForceRebuildLayoutImmediate(parentRect);
    }

    string InsertWordBreaks(string input, int maxLen)
    {
        if (string.IsNullOrEmpty(input) || maxLen <= 0) return input;
        var sb = new System.Text.StringBuilder(input.Length + input.Length / maxLen + 4);
        int run = 0;
        for (int i = 0; i < input.Length; i++)
        {
            char c = input[i];
            sb.Append(c);
            if (!char.IsWhiteSpace(c) && !IsPunctuationChar(c))
            {
                run++;
                if (run >= maxLen)
                {
                    sb.Append('\u200B');
                    run = 0;
                }
            }
            else
            {
                run = 0;
            }
        }
        return sb.ToString();
    }

    bool IsPunctuationChar(char c)
    {
        return char.IsPunctuation(c) || char.IsSymbol(c);
    }


    void OnInputValueChanged(string value)
    {
        if (isProcessingInput) return;
        isProcessingInput = true;

        EnforceLineWrap();
        EnforceLineLimit();
        EnforceTotalCharLimit();
        UpdateCharacterCounter();

        isProcessingInput = false;
    }

    int GetGlobalCharIndex(List<string> lines, int lineIndex, int charIndexInLine)
    {
        int index = 0;
        for (int i = 0; i < lineIndex; i++)
            index += lines[i].Length + 1;

        return index + charIndexInLine;
    }

    void EnforceLineWrap()
    {
        if (inputField == null) return;

        string originalText = inputField.text ?? "";
        int originalCaret = inputField.caretPosition;

        List<string> lines = new List<string>(originalText.Split('\n'));

        bool changed = false;
        int caretShift = 0;

        for (int i = 0; i < lines.Count; i++)
        {
            while (lines[i].Length > maxCharsPerLine)
            {
                string overflow = lines[i].Substring(maxCharsPerLine);
                lines[i] = lines[i].Substring(0, maxCharsPerLine);

                if (i + 1 < lines.Count)
                    lines[i + 1] = overflow + lines[i + 1];
                else
                    lines.Add(overflow);

                int wrapIndexGlobal = GetGlobalCharIndex(lines, i, maxCharsPerLine);
                if (originalCaret > wrapIndexGlobal)
                    caretShift += 1;

                changed = true;
            }
        }

        if (lines.Count > maxInputLines)
        {
            lines = lines.Take(maxInputLines).ToList();
            changed = true;
        }

        string result = string.Join("\n", lines);

        if (result.Length > inputCharacterLimit)
            result = result.Substring(0, inputCharacterLimit);

        if (!changed || result == originalText) return;

        inputField.text = result;

        int newCaret = Mathf.Clamp(originalCaret + caretShift, 0, result.Length);
        inputField.caretPosition = newCaret;
    }

    void EnforceLineLimit()
    {
        if (inputField == null) return;

        string text = inputField.text ?? "";
        string[] lines = text.Split('\n');
        if (lines.Length <= maxInputLines) return;

        string[] allowed = lines.Take(maxInputLines).ToArray();
        string result = string.Join("\n", allowed);

        if (result.Length > inputCharacterLimit)
            result = result.Substring(0, inputCharacterLimit);

        inputField.text = result;
        inputField.caretPosition = inputField.text.Length;
    }

    void EnforceTotalCharLimit()
    {
        if (inputField == null) return;

        string text = inputField.text ?? "";
        if (text.Length <= inputCharacterLimit) return;

        inputField.text = text.Substring(0, inputCharacterLimit);
        inputField.caretPosition = inputField.text.Length;
    }

    void UpdateCharacterCounter()
    {
        if (characterCounterText == null || inputField == null) return;

        int current = inputField.text.Length;
        characterCounterText.text = $"{current} / {inputCharacterLimit}";

        if (current >= inputCharacterLimit)
            characterCounterText.color = Color.red;
        else if (current >= inputCharacterLimit * 0.8f)
            characterCounterText.color = new Color(1f, 0.6f, 0.2f);
        else
            characterCounterText.color = Color.white;
    }

    string ShortId(string uid)
    {
        if (string.IsNullOrEmpty(uid)) return "anon";
        return uid.Length <= 8 ? uid : uid.Substring(0, 8);
    }

    void UpdatePagingButtons()
    {
        int totalDoublePages = Mathf.Max(1, Mathf.CeilToInt((float)notes.Count / (itemsPerSide * 2)));
        if (prevButton) prevButton.interactable = currentDoublePage > 0;
        if (nextButton) nextButton.interactable = currentDoublePage < totalDoublePages - 1;
    }

    public void PrevDoublePage()
    {
        if (currentDoublePage > 0) { currentDoublePage--; RefreshDoublePageUI(); }
    }
    public void NextDoublePage()
    {
        int totalDoublePages = Mathf.Max(1, Mathf.CeilToInt((float)notes.Count / (itemsPerSide * 2)));
        if (currentDoublePage < totalDoublePages - 1) { currentDoublePage++; RefreshDoublePageUI(); }
    }

    public void SubmitButtonClicked()
    {
        if (Time.time - lastSubmitTime < submitCooldown) { Debug.Log("Too fast."); return; }

        string text = inputField.text?.Trim();
        if (string.IsNullOrEmpty(text)) return;

        var lines = text.Split('\n').Take(maxInputLines).Select(l => l.Length > maxCharsPerLine ? l.Substring(0, maxCharsPerLine) : l);
        text = string.Join("\n", lines);

        if (text.Length > inputCharacterLimit)
            text = text.Substring(0, inputCharacterLimit);

        SubmitNote(text);
        inputField.text = "";
        UpdateCharacterCounter();
        lastSubmitTime = Time.time;
    }

    void SubmitNote(string text)
    {
        if (dbRef == null) { Debug.LogWarning("DB ref null"); return; }
        var noteDict = new Dictionary<string, object>()
        {
            { "userId", userId ?? "anon" },
            { "text", text },
            { "timestamp", ServerValue.Timestamp }
        };
        dbRef.Child("guestbook").Push().SetValueAsync(noteDict).ContinueWith(t => {
            if (t.IsFaulted) Debug.LogError("Write failed: " + t.Exception);
        });
    }

    [Serializable]
    public class NoteData
    {
        public string id;
        public string userId;
        public string text;
        public long timestamp;
    }
}
