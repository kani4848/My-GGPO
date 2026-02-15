using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;

public static class UiInputGuard
{
    public static bool IsTypingInTextField()
    {
        var es = EventSystem.current;
        if (es == null) return false;

        var go = es.currentSelectedGameObject;
        if (go == null) return false;

        // TMP_InputField にフォーカスがあるなら文字入力中
        if (go.GetComponent<TMP_InputField>() != null) return true;

        // 念のため標準InputFieldも
        if (go.GetComponent<UnityEngine.UI.InputField>() != null) return true;

        return false;
    }
}
