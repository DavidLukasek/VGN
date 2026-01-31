using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

public class HelpManager : MonoBehaviour
{
    public TMP_Text helpText;
    public string originalText;
    public string newText;

    private bool isOriginalText;

    void Start()
    {
        isOriginalText = true;
    }

    public void OnHelp(InputValue value)
    {
        if(isOriginalText)
        {
            helpText.text = newText;
            isOriginalText = false;
        }
        else
        {
            helpText.text = originalText;
            isOriginalText = true;
        }

        UISoundManager.PlayHelp();
    }
}
