using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Provisional button to check that the controller ray and trigger work.
// It will be replaced by the main menu.
public class RayTestButton : MonoBehaviour
{
    [SerializeField] Button button;
    [SerializeField] TMP_Text label;
    [SerializeField] Color pressedColor = new Color(0.2f, 0.65f, 0.3f);

    int presses;

    void Awake()
    {
        if (!button) button = GetComponent<Button>();
        if (!label) label = GetComponentInChildren<TMP_Text>();
        button.onClick.AddListener(OnPressed);
    }

    void OnPressed()
    {
        presses++;
        label.text = "Gatillo detectado (" + presses + ")";
        var colors = button.colors;
        colors.normalColor = pressedColor;
        colors.selectedColor = pressedColor;
        button.colors = colors;
    }
}
