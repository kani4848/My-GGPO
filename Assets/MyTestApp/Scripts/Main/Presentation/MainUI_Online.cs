using Cysharp.Threading.Tasks;
using Cysharp.Threading.Tasks.Triggers;
using DG.Tweening;
using TMPro;
using Unity.Collections;
using UnityEngine;
using UnityEngine.UI;

public class MainUI_Online : MonoBehaviour
{
    [SerializeField] Button qmButton;
    [SerializeField] Button goLobbyButton;
    [SerializeField] Button goTitleButton;
    [SerializeField] Button cancelButton;

    [SerializeField] GameObject searchingUI;
    [SerializeField] GameObject networkError;

    [SerializeField] GameObject bountyUI;
    [SerializeField] TextMeshProUGUI myBounty;
    [SerializeField] TextMeshProUGUI bountyChanged;

    private void Awake()
    {
        gameObject.SetActive(false);
        DeacetivateEndMenuButtons();
        DeactivateQmCancelButton();
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Z) && qmButton.interactable)
        {
            qmButton.onClick.Invoke();
        }

        if (Input.GetKeyDown(KeyCode.X) && cancelButton.interactable)
        {
            cancelButton.onClick.Invoke();
        }

        if (Input.GetKeyDown(KeyCode.X) && goLobbyButton.interactable)
        {
            goLobbyButton.onClick.Invoke();
        }

        if (Input.GetKeyDown(KeyCode.C) && goTitleButton.interactable)
        {
            goTitleButton.onClick.Invoke();
        }
    }

    //オンラインモード===============================================================================
    public void ActivateBountyUI()
    {
        gameObject.SetActive(true);
        bountyUI.SetActive(true);
        searchingUI.SetActive(false);
        networkError.SetActive(false);
        DeacetivateEndMenuButtons();
    }

    public UniTask<MainGameState> ActivateEndMenuButtons()
    {
        gameObject.SetActive(true);

        searchingUI.SetActive(false);

        qmButton.gameObject.SetActive(true);
        goLobbyButton.gameObject.SetActive(true);
        goTitleButton.gameObject.SetActive(true);

        qmButton.interactable = true;
        goLobbyButton.interactable = true;
        goTitleButton.interactable = true;

        var endMenuTask = new UniTaskCompletionSource<MainGameState>();

        qmButton.onClick.AddListener(() =>
        {
            SoundManager.Instance.PlaySE(SE_Handler.SoundType.BUTTON);
            endMenuTask.TrySetResult(MainGameState.QUICK_MATCH);
            DeacetivateEndMenuButtons();
            searchingUI.SetActive(true);
        });

        goLobbyButton.onClick.AddListener(() =>
        {
            SoundManager.Instance.PlaySE(SE_Handler.SoundType.BUTTON);
            endMenuTask.TrySetResult(MainGameState.GO_LOBBY);
            DeacetivateEndMenuButtons();
        });

        goTitleButton.onClick.AddListener(() =>
        {
            SoundManager.Instance.PlaySE(SE_Handler.SoundType.BUTTON);
            endMenuTask.TrySetResult(MainGameState.GO_TITLE);
            DeacetivateEndMenuButtons();
        });

        return endMenuTask.Task;
    }

    public void OnStartQuickMatcn()
    {
        searchingUI.SetActive(true);
        DeacetivateEndMenuButtons();

        cancelButton.gameObject.SetActive(true);
        cancelButton.interactable = true;
        cancelButton.onClick.AddListener(() =>
        {
            SoundManager.Instance.PlaySE(SE_Handler.SoundType.BUTTON);
            MainGameEvent.RaiseQuickMatchCancel();
            DeactivateQmCancelButton();
        });
    }

    public void DeactivateQmCancelButton()
    {
        cancelButton.interactable = false;
        cancelButton.onClick.RemoveAllListeners();
    }

    public void DeacetivateEndMenuButtons()
    {
        qmButton.gameObject.SetActive(false);
        goLobbyButton.gameObject.SetActive(false);
        goTitleButton.gameObject.SetActive(false);

        qmButton.interactable = false;
        goLobbyButton.interactable = false;
        goTitleButton.interactable = false;

        qmButton.onClick.RemoveAllListeners();
        goLobbyButton.onClick.RemoveAllListeners();
        goTitleButton.onClick.RemoveAllListeners();
    }

    public void ShowErrorWindow()
    {
        searchingUI.SetActive(false);
        DeacetivateEndMenuButtons();
        networkError.SetActive(true);
    }

    public void OnQuickMatchSuccess()
    {
        DeacetivateEndMenuButtons();
        DeactivateQmCancelButton();
        gameObject.SetActive(false);
    }

    public async UniTask ChangeBounty(int current, int delta)
    {
        int targetVal = current + delta;

        bool amountIsPlus = delta >= 0;

        string _changed = amountIsPlus ? "+" + delta.ToString("N0") : delta.ToString("N0");
        bountyChanged.text = _changed;
        bountyChanged.color = amountIsPlus? Color.yellow : Color.red;

        await DOVirtual.Int(current, targetVal, 3, val => { 
            myBounty.text = val.ToString("N0"); 
        });
    }
}
