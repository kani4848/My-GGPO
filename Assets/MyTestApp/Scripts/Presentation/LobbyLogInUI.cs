using TMPro;
using UnityEngine;

public class LobbyLogInUI : MonoBehaviour
{
    [SerializeField] TMP_InputField nameInput;
    public void Activated()
    {
        gameObject.SetActive(true);
    }

    public void Deactivated()
    {
        gameObject.SetActive(false);
    }
    public string GetUserName()
    {
        return nameInput.text == "" ? LobbySceneManager.emptyPlayerName : nameInput.text;
    }
}
