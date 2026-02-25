using TMPro;
using UnityEngine;

public sealed class DisplayNameInputValidator : MonoBehaviour
{
    [SerializeField] TMP_InputField inputField;

    const int MaxLength = 12;
    const string Allowed = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ-!.?";

    void Awake()
    {
        if (inputField == null) inputField = GetComponent<TMP_InputField>();

        inputField.characterLimit = MaxLength;
        inputField.onValidateInput = ValidateChar;
        inputField.onEndEdit.AddListener(NormalizeAll);
    }

    char ValidateChar(string text, int charIndex, char addedChar)
    {
        // 小文字は大文字化（安全な方法）
        char upper = char.ToUpperInvariant(addedChar);

        // 許可文字のみ通す
        if (Allowed.IndexOf(upper) < 0)
        {
            return '\0';
        }

        return upper;
    }

    void NormalizeAll(string _)
    {
        string t = inputField.text;
        if (string.IsNullOrEmpty(t)) return;

        char[] buffer = new char[MaxLength];
        int w = 0;

        for (int i = 0; i < t.Length && w < MaxLength; i++)
        {
            char upper = char.ToUpperInvariant(t[i]);

            if (Allowed.IndexOf(upper) >= 0)
            {
                buffer[w++] = upper;
            }
        }

        inputField.text = new string(buffer, 0, w);
    }
}
