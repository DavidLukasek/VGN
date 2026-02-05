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

    [Header("Font / Layout")]
    public TMP_FontAsset monoFont;
    public float inputFieldPreferredWidth = 500f;
    public bool forceDisableContentSizeFitter = true;

    [Header("Text layout / sizing")]
    public float fixedFontSize = 20f;
    public int maxWordLengthBeforeBreak = 20;
    public int inputCharacterLimit = 300;
    public int maxInputLines = 7;
    public int maxCharsPerLine = 50;

    [Header("Display formatting")]
    public int displayCharsPerLine = 0;
    public int displayMaxLines = 7;

    TMP_Text inputTextCompCached;
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

        if (inputField != null)
        {
            inputField.characterLimit = inputCharacterLimit;
            inputField.lineType = TMP_InputField.LineType.MultiLineNewline;
            if (inputField.textComponent != null)
            {
                if (monoFont != null)
                {
                    inputField.textComponent.font = monoFont;
                    inputField.textComponent.fontSharedMaterial = monoFont.material;
                }

                inputField.textComponent.textWrappingMode = TextWrappingModes.Normal;
                inputField.textComponent.overflowMode = TextOverflowModes.Truncate;
            }

            var le = inputField.GetComponent<LayoutElement>();
            if (le == null) le = inputField.gameObject.AddComponent<LayoutElement>();

            le.flexibleWidth = 0f;
            if (inputFieldPreferredWidth > 0f)
                le.preferredWidth = inputFieldPreferredWidth;

            if (forceDisableContentSizeFitter)
            {
                var csf = inputField.GetComponent<ContentSizeFitter>();
                if (csf != null) csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            }

            inputField.onValueChanged.AddListener(OnInputValueChanged);
            UpdateCharacterCounter();
        }

        if (inputField != null && inputField.textComponent != null)
        inputTextCompCached = inputField.textComponent;

        if (displayCharsPerLine <= 0 && monoFont != null)
        {
            RectTransform slotRect = null;
            if (leftPageArea != null)
            {
                var sample = notePrefab.GetComponentInChildren<RectTransform>();
                if (sample != null) slotRect = sample;
            }

            float targetWidth = 0f;
            if (slotRect != null) targetWidth = slotRect.rect.width;
            else if (inputTextCompCached != null) targetWidth = inputTextCompCached.rectTransform.rect.width;
            else targetWidth = inputFieldPreferredWidth;

            if (targetWidth <= 0f) targetWidth = inputFieldPreferredWidth;

            displayCharsPerLine = CalculateCharsThatFit(monoFont, fixedFontSize, targetWidth, 200);
            if (displayCharsPerLine <= 0) displayCharsPerLine = maxCharsPerLine;
        }

        RefreshDoublePageUI();
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

        int prevTotal = GetTotalDoublePages();
        bool wasAtEnd = (currentDoublePage >= prevTotal - 1);

        notes.Add(n);
        notes = notes.OrderBy(x => x.timestamp).ToList();

        int newTotal = GetTotalDoublePages();
        if (wasAtEnd)
        {
            currentDoublePage = Mathf.Max(0, newTotal - 1);
        }
        else
        {
            currentDoublePage = Mathf.Clamp(currentDoublePage, 0, newTotal - 1);
        }

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
            if (pageInfoText) pageInfoText.text = "Zatím žádné zápisky.";
            UpdatePagingButtons();
            return;
        }

        int totalDoublePages = GetTotalDoublePages();
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

        foreach (Transform child in parent)
        {
            Destroy(child.gameObject);
        }

        for (int i = 0; i < slotsCount; i++)
        {
            int globalIndex = startIndexForThisSide + i;
            GameObject go = Instantiate(notePrefab, parent);
            var tmp = go.GetComponentInChildren<TextMeshProUGUI>();
            if (tmp != null)
            {
                if (monoFont != null)
                {
                    tmp.font = monoFont;
                    tmp.fontSharedMaterial = monoFont.material;
                }

                tmp.enableAutoSizing = false;
                tmp.fontSize = fixedFontSize;
                tmp.textWrappingMode = TextWrappingModes.Normal;
                tmp.overflowMode = TextOverflowModes.Truncate;
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
                string date = n.timestamp > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(n.timestamp).ToLocalTime().ToString("dd.MM.yyyy   HH:mm:ss") : "—";

                string raw = (n.text ?? "").Replace("\r", "");

                int cols = displayCharsPerLine > 0 ? displayCharsPerLine : maxCharsPerLine;
                string wrapped = HardWrapText(raw, cols, displayMaxLines);

                if (tmp != null)
                {
                    tmp.text = $"<size=80%><b><color=#000>[{date}]</color></b></size>\n<color=#333>{wrapped}</color>";
                }
            }
            else
            {
                if (tmp != null) tmp.text = "";
            }
        }

        Canvas.ForceUpdateCanvases();
        if (parentRect != null)
        {
            var layoutGroup = parent.GetComponent<VerticalLayoutGroup>();
            if (layoutGroup != null) LayoutRebuilder.ForceRebuildLayoutImmediate(parentRect);
        }
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

    string RemoveZeroWidthBreaks(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        return s.Replace("\u200B", "");
    }

    (string newText, int newCaret) InsertWordBreaksAndMap(string input, int maxLen, int originalCaret)
    {
        if (string.IsNullOrEmpty(input) || maxLen <= 0)
            return (RemoveZeroWidthBreaks(input ?? ""), Mathf.Clamp(originalCaret, 0, (input ?? "").Length));

        string clean = RemoveZeroWidthBreaks(input);

        var sb = new System.Text.StringBuilder(clean.Length + clean.Length / maxLen + 4);
        int runCount = 0;
        int newCaretPos = 0;

        if (originalCaret == 0) newCaretPos = 0;

        for (int i = 0; i < clean.Length; i++)
        {
            char c = clean[i];
            bool isSep = char.IsWhiteSpace(c) || IsPunctuationChar(c);

            sb.Append(c);

            if (originalCaret == i + 1)
                newCaretPos = sb.Length;

            if (isSep)
            {
                runCount = 0;
            }
            else
            {
                runCount++;
                if (runCount >= maxLen)
                {
                    sb.Append('\u200B');
                    runCount = 0;
                    if (originalCaret == i + 1)
                    {
                        newCaretPos = sb.Length;
                    }
                }
            }
        }

        // pokud byl caret na konci původního textu
        if (originalCaret == clean.Length)
            newCaretPos = sb.Length;

        return (sb.ToString(), Mathf.Clamp(newCaretPos, 0, sb.Length));
    }

    void EnforceLineWrap()
    {
        if (inputField == null) return;

        string originalText = inputField.text ?? "";
        int originalCaret = Mathf.Clamp(inputField.caretPosition, 0, originalText.Length);

        if (maxWordLengthBeforeBreak > 0)
        {
            (string withBreaks, int mappedCaret) = InsertWordBreaksAndMap(originalText, maxWordLengthBeforeBreak, originalCaret);
            originalText = withBreaks;
            originalCaret = mappedCaret;
        }
        else
        {
            originalText = RemoveZeroWidthBreaks(originalText);
            originalCaret = Mathf.Clamp(originalCaret, 0, originalText.Length);
        }

        string prefixText = originalText.Substring(0, originalCaret);

        var lines = new List<string>(originalText.Split('\n'));
        var prefixLines = new List<string>(prefixText.Split('\n'));

        bool changed = false;

        for (int i = 0; i < lines.Count; i++)
        {
            if (prefixLines.Count <= i) prefixLines.Add(string.Empty);

            while (lines[i].Length > maxCharsPerLine)
            {
                int splitAt = -1;
                int searchIndex = Mathf.Min(maxCharsPerLine - 1, lines[i].Length - 1);
                for (int j = searchIndex; j >= 0; j--)
                {
                    if (char.IsWhiteSpace(lines[i][j]))
                    {
                        splitAt = j;
                        break;
                    }
                }

                string overflow;
                if (splitAt >= 0)
                {
                    overflow = lines[i].Substring(splitAt + 1).TrimStart();
                    lines[i] = lines[i].Substring(0, splitAt).TrimEnd();
                }
                else
                {
                    overflow = lines[i].Substring(maxCharsPerLine);
                    lines[i] = lines[i].Substring(0, maxCharsPerLine);
                }

                if (i + 1 < lines.Count)
                {
                    if (lines[i + 1].Length > 0 && !char.IsWhiteSpace(lines[i + 1][0]) && overflow.Length > 0)
                        lines[i + 1] = overflow + " " + lines[i + 1];
                    else
                        lines[i + 1] = overflow + lines[i + 1];
                }
                else
                {
                    lines.Add(overflow);
                }

                if (i < prefixLines.Count)
                {
                    var pLine = prefixLines[i];
                    if (!string.IsNullOrEmpty(pLine))
                    {
                        if (splitAt >= 0)
                        {
                            if (pLine.Length > splitAt)
                            {
                                string pOverflow = pLine.Length > splitAt + 1 ? pLine.Substring(splitAt + 1).TrimStart() : string.Empty;
                                prefixLines[i] = pLine.Length > splitAt ? pLine.Substring(0, Math.Min(pLine.Length, splitAt)) : pLine;
                                if (i + 1 < prefixLines.Count)
                                    prefixLines[i + 1] = pOverflow + prefixLines[i + 1];
                                else
                                    prefixLines.Add(pOverflow);
                            }
                        }
                        else
                        {
                            if (pLine.Length > maxCharsPerLine)
                            {
                                string pOverflow = pLine.Substring(maxCharsPerLine);
                                prefixLines[i] = pLine.Substring(0, maxCharsPerLine);
                                if (i + 1 < prefixLines.Count)
                                    prefixLines[i + 1] = pOverflow + prefixLines[i + 1];
                                else
                                    prefixLines.Add(pOverflow);
                            }
                        }
                    }
                }

                changed = true;

                if (prefixLines.Count < lines.Count)
                {
                    while (prefixLines.Count < lines.Count) prefixLines.Add(string.Empty);
                }
            }
        }

        if (lines.Count > maxInputLines)
        {
            lines = lines.Take(maxInputLines).ToList();
            changed = true;
        }

        string result = string.Join("\n", lines);
        if (result.Length > inputCharacterLimit)
        {
            result = result.Substring(0, inputCharacterLimit);
            changed = true;
        }

        if (!changed || result == originalText) return;

        string newPrefix = string.Join("\n", prefixLines.Take(Mathf.Min(prefixLines.Count, maxInputLines)));
        int newCaret = Mathf.Clamp(newPrefix.Length, 0, result.Length);

        inputField.text = result;
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
        int totalDoublePages = GetTotalDoublePages();
        if (prevButton) prevButton.interactable = currentDoublePage > 0;
        if (nextButton) nextButton.interactable = currentDoublePage < totalDoublePages - 1;
    }

    public void PrevDoublePage()
    {
        int totalDoublePages = GetTotalDoublePages();
        if (totalDoublePages <= 0) return;
        int newIndex = Mathf.Clamp(currentDoublePage - 1, 0, totalDoublePages - 1);
        if (newIndex != currentDoublePage)
        {
            currentDoublePage = newIndex;
            RefreshDoublePageUI();
        }
    }

    public void NextDoublePage()
    {
        int totalDoublePages = GetTotalDoublePages();
        if (totalDoublePages <= 0) return;
        int newIndex = Mathf.Clamp(currentDoublePage + 1, 0, totalDoublePages - 1);
        if (newIndex != currentDoublePage)
        {
            currentDoublePage = newIndex;
            RefreshDoublePageUI();
        }
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

    int CalculateCharsThatFit(TMP_FontAsset font, float fontSize, float targetWidth, int maxTest = 200)
    {
        TMP_Text probe = inputTextCompCached;
        if (probe == null)
        {
            return maxCharsPerLine > 0 ? maxCharsPerLine : 40;
        }

        int lo = 1, hi = maxTest;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            string s = new string('X', mid);
            Vector2 size = probe.GetPreferredValues(s, Mathf.Infinity, Mathf.Infinity);
            if (size.x <= targetWidth) lo = mid + 1;
            else hi = mid - 1;
        }
        return Mathf.Max(1, hi);
    }

    string HardWrapText(string text, int cols, int maxLines)
    {
        if (string.IsNullOrEmpty(text) || cols <= 0) return text;
        var parts = text.Split('\n');
        var outLines = new List<string>();
        foreach (var p in parts)
        {
            string line = p;
            int start = 0;
            while (start < line.Length)
            {
                int len = Mathf.Min(cols, line.Length - start);
                outLines.Add(line.Substring(start, len));
                start += len;
                if (outLines.Count >= maxLines) break;
            }
            if (outLines.Count >= maxLines) break;
        }

        return string.Join("\n", outLines.Take(maxLines));
    }

    int GetTotalDoublePages()
    {
        int perDouble = Mathf.Max(1, itemsPerSide * 2);
        return Mathf.Max(1, (notes.Count + perDouble - 1) / perDouble);
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
