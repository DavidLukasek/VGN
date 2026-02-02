using Firebase;
using Firebase.Auth;
using Firebase.Database;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI; //https://vgn-by-ddi-default-rtdb.europe-west1.firebasedatabase.app/

public class GuestbookManager : MonoBehaviour
{
    [Header("UI - References")]
    public TMP_InputField inputField;
    public Button submitButton;
    public Button prevButton;
    public Button nextButton;
    public TMP_Text pageInfoText;

    [Header("Page Areas (Left / Right)")]
    public Transform leftPageArea;   // parent for left page notes
    public Transform rightPageArea;  // parent for right page notes
    public GameObject notePrefab;    // prefab with TextMeshProUGUI

    [Header("Paging")]
    public int itemsPerSide = 3;     // how many notes per side (default: 3 => total 6 per double-page)
    int currentDoublePage = 0;       // 0 = první (nejstarší) dvojstránka, we'll open on last
    List<NoteData> notes = new List<NoteData>();

    // Firebase
    DatabaseReference dbRef;
    FirebaseAuth auth;
    string userId;

    // anti-spam
    float lastSubmitTime = -999f;
    public float submitCooldown = 5f;

    async void Start()
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

        Debug.Log("Database ready");

        var query = dbRef
            .Child("guestbook")
            .OrderByChild("timestamp")
            .LimitToLast(2000);

        query.ChildAdded += OnChildAdded;
    }

    void OnDestroy()
    {
        if (dbRef != null)
        {
            dbRef.ChildAdded -= OnChildAdded;
            dbRef.ChildChanged -= OnChildChanged;
            dbRef.ChildRemoved -= OnChildRemoved;
        }
    }

    void OnChildAdded(object sender, ChildChangedEventArgs e)
    {
        if (e.DatabaseError != null) { Debug.LogError(e.DatabaseError.Message); return; }
        var n = SnapshotToNote(e.Snapshot);
        if (n == null) return;

        // Avoid duplicates
        if (notes.Any(x => x.id == n.id)) return;

        notes.Add(n);
        notes = notes.OrderBy(x => x.timestamp).ToList(); // oldest -> newest

        // After we receive data: ensure we open on the last double-page (newest)
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

    // RENDERING: vypočítá co má být na levé/pravé straně pro currentDoublePage
    void RefreshDoublePageUI()
    {
        // clear old instances
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

        int startIndex = currentDoublePage * itemsPerSide * 2; // index první položky na levé straně
        int leftStart = startIndex;
        int leftEnd = Mathf.Min(notes.Count, leftStart + itemsPerSide); // exclusive
        int rightStart = leftEnd;
        int rightEnd = Mathf.Min(notes.Count, rightStart + itemsPerSide);

        // instantiate left column
        for (int i = leftStart; i < leftEnd; i++)
        {
            InstantiateNoteUI(notes[i], leftPageArea);
        }
        // instantiate right column
        for (int i = rightStart; i < rightEnd; i++)
        {
            InstantiateNoteUI(notes[i], rightPageArea);
        }

        if (pageInfoText) pageInfoText.text = $"Strana {currentDoublePage + 1} / {totalDoublePages}";
        UpdatePagingButtons();
    }

    void InstantiateNoteUI(NoteData n, Transform parent)
    {
        if (notePrefab == null || parent == null) return;
        var go = Instantiate(notePrefab, parent);
        var tmp = go.GetComponentInChildren<TextMeshProUGUI>();
        if (tmp != null)
        {
            string date = n.timestamp > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(n.timestamp).ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "—";
            tmp.text = $"<size=80%><color=#888>[{date}]</color></size>\n{n.text}";
        }
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

    // NAVIGATION (public - můžeš připojit ve Inspectoru)
    public void PrevDoublePage()
    {
        if (currentDoublePage > 0) { currentDoublePage--; RefreshDoublePageUI(); }
    }
    public void NextDoublePage()
    {
        int totalDoublePages = Mathf.Max(1, Mathf.CeilToInt((float)notes.Count / (itemsPerSide * 2)));
        if (currentDoublePage < totalDoublePages - 1) { currentDoublePage++; RefreshDoublePageUI(); }
    }

    // SUBMIT
    public void SubmitButtonClicked()
    {
        if (Time.time - lastSubmitTime < submitCooldown) { Debug.Log("Too fast."); return; }
        string text = inputField.text?.Trim();
        if (string.IsNullOrEmpty(text)) return;
        if (text.Length > 1000) text = text.Substring(0, 1000);
        SubmitNote(text);
        inputField.text = "";
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
}
