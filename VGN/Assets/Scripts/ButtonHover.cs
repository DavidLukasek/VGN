using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

public class ButtonHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public TMP_Text buttonTMPText;

    public string hoverText = "LOREM IPSUM";
    private string originalText;

    void Start()
    {
        originalText = buttonTMPText.text;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        buttonTMPText.text = hoverText;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        buttonTMPText.text = originalText;
    }
}
