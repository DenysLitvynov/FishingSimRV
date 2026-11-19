using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Small, smooth feedback for the original illustrated menu buttons.
public sealed class MenuRVButtonHover : MonoBehaviour,
    IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    readonly HashSet<int> hovering = new HashSet<int>();
    readonly HashSet<int> pressing = new HashSet<int>();
    RectTransform rect;
    Button button;
    Vector3 normalScale;

    void Awake()
    {
        rect = GetComponent<RectTransform>();
        button = GetComponent<Button>();
        normalScale = rect.localScale;
    }

    void Update()
    {
        bool available = button && button.IsInteractable();
        float scale = !available ? 1f : pressing.Count > 0 ? 0.98f : hovering.Count > 0 ? 1.035f : 1f;
        rect.localScale = Vector3.Lerp(rect.localScale, normalScale * scale,
            1f - Mathf.Exp(-14f * Time.unscaledDeltaTime));
    }

    public void OnPointerEnter(PointerEventData data)
    {
        if (button && button.IsInteractable() && hovering.Add(data.pointerId))
            MenuRVAudio.Hover();
    }
    public void OnPointerExit(PointerEventData data)
    {
        hovering.Remove(data.pointerId);
        pressing.Remove(data.pointerId);
    }
    public void OnPointerDown(PointerEventData data) { pressing.Add(data.pointerId); }
    public void OnPointerUp(PointerEventData data) { pressing.Remove(data.pointerId); }

    void OnDisable()
    {
        hovering.Clear();
        pressing.Clear();
        if (rect) rect.localScale = normalScale;
    }
}
